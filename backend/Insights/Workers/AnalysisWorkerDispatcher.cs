using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Backend.Calculation;
using Backend.Insights.Analysis;
using Backend.Insights.Contracts;
using Backend.Models.Domain;

namespace Backend.Insights.Workers;

public sealed record StrongestPathV1WorkerInput(
    [property: JsonRequired] string ScenarioKey,
    [property: JsonRequired] Graph Graph,
    [property: JsonRequired] string StartNodeId,
    [property: JsonRequired] PathDirection Direction);

public sealed record EvidenceImpactV0WorkerInput(
    [property: JsonRequired] string ScenarioKey,
    [property: JsonRequired] Graph Graph,
    [property: JsonRequired] string TargetNodeId);

public sealed record CriticalCounterV1WorkerInput(
    [property: JsonRequired] string ScenarioKey,
    [property: JsonRequired] Graph Graph,
    [property: JsonRequired] string TargetNodeId,
    [property: JsonRequired] string RequestedStrategy,
    [property: JsonRequired] decimal ThresholdLogOdds,
    [property: JsonRequired] int? AutoCandidateCutoff,
    int? CandidateLimit = null);

public sealed record RobustnessV0WorkerInput(
    [property: JsonRequired] string ScenarioKey,
    [property: JsonRequired] Graph Graph);

public sealed record SinglePairPathV0WorkerInput(
    [property: JsonRequired] string ScenarioKey,
    [property: JsonRequired] Graph Graph,
    [property: JsonRequired] string StartNodeId,
    [property: JsonRequired] string TargetNodeId,
    [property: JsonRequired] string RequestedStrategy);

public sealed record LikelihoodRecalculateV0WorkerInput(
    [property: JsonRequired] string ScenarioKey,
    [property: JsonRequired] Graph Graph,
    [property: JsonRequired] string ChangedNodeId);

public sealed record StrongestPathV1WorkerParameters(
    [property: JsonRequired] string StartNodeId,
    [property: JsonRequired] PathDirection Direction);

public sealed record EvidenceImpactV0WorkerParameters(
    [property: JsonRequired] string TargetNodeId);

public sealed record CriticalCounterV1WorkerParameters(
    [property: JsonRequired] string TargetNodeId,
    [property: JsonRequired] string RequestedStrategy,
    [property: JsonRequired] decimal ThresholdLogOdds,
    [property: JsonRequired] int? AutoCandidateCutoff,
    int? CandidateLimit = null);

public sealed record RobustnessV0WorkerParameters;

public sealed record SinglePairPathV0WorkerParameters(
    [property: JsonRequired] string StartNodeId,
    [property: JsonRequired] string TargetNodeId,
    [property: JsonRequired] string RequestedStrategy);

public sealed record LikelihoodRecalculateV0WorkerParameters(
    [property: JsonRequired] string ChangedNodeId);

/// <summary>
/// Stateless projection from the Phase 1 worker request envelope to the
/// retained in-memory analysis and diagnostic result contracts. Process
/// lifetime, cancellation-frame handling, and hard deadlines remain outside
/// this class.
/// </summary>
public sealed class AnalysisWorkerDispatcher
{
    private static readonly JsonSerializerOptions SerializerOptions =
        CanonicalJson.CreateSerializerOptions();

    public CompactRunOutput Dispatch(
        WorkerRequestFrame request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        return request.OperationKey switch
        {
            OperationKeys.PathStrongest => DispatchStrongestPath(
                request,
                DeserializeInput<StrongestPathV1WorkerInput>(request.Input),
                cancellationToken),
            OperationKeys.EvidenceImpactRanking => DispatchEvidenceImpact(
                request,
                DeserializeInput<EvidenceImpactV0WorkerInput>(request.Input),
                cancellationToken),
            OperationKeys.CounterCriticalSet => DispatchCriticalCounter(
                request,
                DeserializeInput<CriticalCounterV1WorkerInput>(request.Input),
                cancellationToken),
            OperationKeys.NodeRobustness => DispatchRobustness(
                request,
                DeserializeInput<RobustnessV0WorkerInput>(request.Input),
                cancellationToken),
            OperationKeys.PathSinglePair => DispatchSinglePair(
                request,
                DeserializeInput<SinglePairPathV0WorkerInput>(request.Input),
                cancellationToken),
            OperationKeys.LikelihoodRecalculate => DispatchLikelihoodRecalculation(
                request,
                DeserializeInput<LikelihoodRecalculateV0WorkerInput>(request.Input),
                cancellationToken),
            _ => throw new ArgumentException(
                $"Operation '{request.OperationKey}' is not a Phase 3 analysis worker operation.",
                nameof(request))
        };
    }

    private static CompactRunOutput DispatchStrongestPath(
        WorkerRequestFrame request,
        StrongestPathV1WorkerInput input,
        CancellationToken cancellationToken)
    {
        ValidateCommonInput(input.ScenarioKey, input.Graph);
        ValidateParameters(
            request.CanonicalParameters.Value,
            new StrongestPathV1WorkerParameters(input.StartNodeId, input.Direction));
        cancellationToken.ThrowIfCancellationRequested();

        var result = new StrongestPathV1Analysis().Analyze(
            input.Graph,
            input.StartNodeId,
            input.Direction,
            cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        return CreateOutput(
            request,
            input.ScenarioKey,
            input.Graph,
            input.StartNodeId,
            new StrategySelection(null, null),
            SerializeItems(result.TopItems, cancellationToken),
            result.TotalResultCardinality,
            result.ResultDigest,
            result.Summary,
            result.Distribution,
            result.TopItems.Select(item => new OrderedPathProjection(
                item.NodeIds,
                item.EdgeIds,
                item.AccumulatedLogLikelihoodRatio)),
            cancellationToken);
    }

    private static CompactRunOutput DispatchEvidenceImpact(
        WorkerRequestFrame request,
        EvidenceImpactV0WorkerInput input,
        CancellationToken cancellationToken)
    {
        ValidateCommonInput(input.ScenarioKey, input.Graph);
        ValidateParameters(
            request.CanonicalParameters.Value,
            new EvidenceImpactV0WorkerParameters(input.TargetNodeId));
        cancellationToken.ThrowIfCancellationRequested();

        var result = new EvidenceImpactV0Analysis().Analyze(
            input.Graph,
            input.TargetNodeId,
            cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        return CreateOutput(
            request,
            input.ScenarioKey,
            input.Graph,
            input.TargetNodeId,
            new StrategySelection(null, null),
            SerializeItems(result.TopItems, cancellationToken),
            result.TotalResultCardinality,
            result.ResultDigest,
            result.Summary,
            result.Distribution,
            result.TopItems.Select(item => new OrderedPathProjection(
                item.NodeIds,
                item.EdgeIds,
                item.AccumulatedPathLogLikelihoodRatio)),
            cancellationToken);
    }

    private static CompactRunOutput DispatchCriticalCounter(
        WorkerRequestFrame request,
        CriticalCounterV1WorkerInput input,
        CancellationToken cancellationToken)
    {
        ValidateCommonInput(input.ScenarioKey, input.Graph);
        ValidateParameters(
            request.CanonicalParameters.Value,
            new CriticalCounterV1WorkerParameters(
                input.TargetNodeId,
                input.RequestedStrategy,
                input.ThresholdLogOdds,
                input.AutoCandidateCutoff,
                input.CandidateLimit));
        cancellationToken.ThrowIfCancellationRequested();

        if (input.CandidateLimit.HasValue)
        {
            _ = Benchmarking.CriticalCounterCandidateGuard.RequireAtMost(
                input.Graph,
                input.TargetNodeId,
                input.CandidateLimit.Value,
                cancellationToken);
        }

        var result = new CriticalCounterV1Analyzer().Analyze(
            new CriticalCounterV1AnalysisRequest(
                input.Graph,
                input.TargetNodeId,
                input.RequestedStrategy,
                input.ThresholdLogOdds,
                input.AutoCandidateCutoff),
            cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        var item = result.DeterministicTopItem;
        var summary = new CriticalCounterV1WorkerSummary(
            item.TargetNodeId,
            result.RequestedStrategy,
            result.UsedStrategy,
            item.CandidateCount,
            result.AutoCandidateCutoff,
            result.StrategySelectionReason,
            item.BaselineLogOdds,
            item.BaselineProbability,
            item.ResultingLogOdds,
            item.ResultingProbability,
            item.ThresholdLogOdds,
            item.ThresholdProbability,
            item.BelowThresholdMargin,
            item.ThresholdAttained,
            item.SelectedNodeIds.Count,
            item.SearchExhausted,
            item.ProvedUnattainable,
            item.OptimalCardinalityProven);
        var distribution = new CriticalCounterV1WorkerDistribution(
            item.CandidateCount,
            item.SelectedCounters.Count,
            item.SelectedCounters.Count(counter =>
                counter.RecognizedByLikelihoodRecalculationV0),
            item.SelectedCounters.Count(counter =>
                !counter.RecognizedByLikelihoodRecalculationV0),
            result.EvaluationCount);
        var paths = item.SelectedCounters
            .Where(counter => counter.ResponsiblePath is not null)
            .Select(counter => new OrderedPathProjection(
                counter.ResponsiblePath!.NodeIds,
                counter.ResponsiblePath.EdgeIds,
                counter.ResponsiblePath.AccumulatedLogLikelihoodRatio));

        return CreateOutput(
            request,
            input.ScenarioKey,
            input.Graph,
            input.TargetNodeId,
            new StrategySelection(result.RequestedStrategy, result.UsedStrategy),
            SerializeItems(result.Items, cancellationToken),
            result.TotalResultCardinality,
            result.ResultDigest,
            summary,
            distribution,
            paths,
            cancellationToken);
    }

    private static CompactRunOutput DispatchRobustness(
        WorkerRequestFrame request,
        RobustnessV0WorkerInput input,
        CancellationToken cancellationToken)
    {
        ValidateCommonInput(input.ScenarioKey, input.Graph);
        ValidateParameters(
            request.CanonicalParameters.Value,
            new RobustnessV0WorkerParameters());
        cancellationToken.ThrowIfCancellationRequested();

        var result = new RobustnessV0Analyzer().Analyze(input.Graph, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        var leastRobust = result.LeastRobust is null
            ? null
            : new RobustnessV0WorkerLeastRobust(
                result.LeastRobust.NodeId,
                result.LeastRobust.Title,
                result.LeastRobust.Kind,
                result.LeastRobust.Rank,
                CanonicalResultNumber.Normalize(result.LeastRobust.RobustnessScore),
                CanonicalResultNumber.Normalize(result.LeastRobust.OriginalProbability),
                CanonicalResultNumber.Normalize(result.LeastRobust.HypotheticalProbability),
                CanonicalResultNumber.Normalize(result.LeastRobust.AbsoluteProbabilityDelta),
                CanonicalResultNumber.Normalize(
                    result.LeastRobust.AccumulatedPathLogLikelihoodRatio));
        var summary = new RobustnessV0WorkerSummary(
            result.TotalResultCardinality,
            leastRobust);

        return CreateOutput(
            request,
            input.ScenarioKey,
            input.Graph,
            null,
            new StrategySelection(null, null),
            CloneItems(result.RetainedItems, cancellationToken),
            result.TotalResultCardinality,
            result.ResultDigest,
            summary,
            result.Distribution,
            result.Top100.Select(item => new OrderedPathProjection(
                item.NodeIds,
                item.EdgeIds,
                CanonicalResultNumber.Normalize(
                    item.AccumulatedPathLogLikelihoodRatio))),
            cancellationToken);
    }

    private static CompactRunOutput DispatchSinglePair(
        WorkerRequestFrame request,
        SinglePairPathV0WorkerInput input,
        CancellationToken cancellationToken)
    {
        ValidateCommonInput(input.ScenarioKey, input.Graph);
        ValidateParameters(
            request.CanonicalParameters.Value,
            new SinglePairPathV0WorkerParameters(
                input.StartNodeId,
                input.TargetNodeId,
                input.RequestedStrategy));
        var selection = input.RequestedStrategy switch
        {
            OperationStrategyNames.Minimum => LogPathSelection.Minimum,
            OperationStrategyNames.Maximum => LogPathSelection.Maximum,
            _ => throw new ArgumentException("Single-pair strategy must be minimum or maximum.", nameof(input))
        };

        var context = GraphCalculationContext.From(input.Graph.Nodes, input.Graph.Edges, cancellationToken);
        var accumulated = new GraphLikelihoodCalculator().GetLogPath(
            context,
            input.StartNodeId,
            input.TargetNodeId,
            selection,
            cancellationToken);
        var item = new SinglePairPathV0WorkerItem(
            input.StartNodeId,
            input.TargetNodeId,
            input.RequestedStrategy,
            accumulated.HasValue,
            accumulated.HasValue ? CanonicalResultNumber.Normalize(accumulated.Value) : null);
        var items = new[] { item };

        return CreateOutput(
            request,
            input.ScenarioKey,
            input.Graph,
            input.TargetNodeId,
            new StrategySelection(input.RequestedStrategy, input.RequestedStrategy),
            SerializeItems(items, cancellationToken),
            1,
            CanonicalJson.ComputeSha256Sequence(items, cancellationToken),
            new SinglePairPathV0WorkerSummary(input.StartNodeId, input.TargetNodeId, accumulated.HasValue),
            new SinglePairPathV0WorkerDistribution(accumulated.HasValue ? 1 : 0),
            Array.Empty<OrderedPathProjection>(),
            cancellationToken);
    }

    private static CompactRunOutput DispatchLikelihoodRecalculation(
        WorkerRequestFrame request,
        LikelihoodRecalculateV0WorkerInput input,
        CancellationToken cancellationToken)
    {
        ValidateCommonInput(input.ScenarioKey, input.Graph);
        ValidateParameters(
            request.CanonicalParameters.Value,
            new LikelihoodRecalculateV0WorkerParameters(input.ChangedNodeId));

        var context = GraphCalculationContext.From(input.Graph.Nodes, input.Graph.Edges, cancellationToken);
        var before = context.NodesById.ToDictionary(
            entry => entry.Key,
            entry => entry.Value.PosteriorOdds,
            StringComparer.Ordinal);
        var recalculated = new GraphLikelihoodCalculator().RecalculateAncestors(
            context,
            input.ChangedNodeId,
            cancellationToken);
        var items = recalculated.Select((entry, index) => new LikelihoodRecalculateV0WorkerItem(
                index + 1,
                entry.Key,
                CanonicalResultNumber.Normalize(before[entry.Key]),
                CanonicalResultNumber.Normalize(entry.Value)))
            .ToArray();
        var retained = items.Take(OperationResultEnvelope.MaximumRetainedItems).ToArray();
        var deltas = items.Select(item => item.AfterLogPosteriorOdds - item.BeforeLogPosteriorOdds).ToArray();

        return CreateOutput(
            request,
            input.ScenarioKey,
            input.Graph,
            input.ChangedNodeId,
            new StrategySelection(null, null),
            SerializeItems(retained, cancellationToken),
            items.LongLength,
            CanonicalJson.ComputeSha256Sequence(items, cancellationToken),
            new LikelihoodRecalculateV0WorkerSummary(input.ChangedNodeId, items.LongLength),
            new LikelihoodRecalculateV0WorkerDistribution(
                items.LongLength,
                deltas.Length == 0 ? null : CanonicalResultNumber.Normalize(deltas.Min()),
                deltas.Length == 0 ? null : CanonicalResultNumber.Normalize(deltas.Max())),
            Array.Empty<OrderedPathProjection>(),
            cancellationToken);
    }

    private static CompactRunOutput CreateOutput<TSummary, TDistribution>(
        WorkerRequestFrame request,
        string scenarioKey,
        Graph graph,
        string? targetNodeId,
        StrategySelection strategy,
        IReadOnlyList<JsonElement> retainedItems,
        long totalResultCardinality,
        string resultDigest,
        TSummary summary,
        TDistribution distribution,
        IEnumerable<OrderedPathProjection> retainedPaths,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var paths = new List<OrderedPathProjection>();
        foreach (var path in retainedPaths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            paths.Add(new OrderedPathProjection(
                Array.AsReadOnly(path.NodeIds.ToArray()),
                Array.AsReadOnly(path.EdgeIds.ToArray()),
                CanonicalResultNumber.Normalize(path.AccumulatedScore)));
        }

        cancellationToken.ThrowIfCancellationRequested();
        var output = new CompactRunOutput(
            request.RunId,
            request.SampleId,
            scenarioKey,
            request.OperationKey,
            request.AlgorithmSemanticIdentity,
            strategy,
            CreateIdentifiers(graph, targetNodeId),
            request.CanonicalParameters,
            new ExecutionOutcome(ExecutionStatus.Succeeded),
            JsonSerializer.SerializeToElement(summary, SerializerOptions),
            JsonSerializer.SerializeToElement(distribution, SerializerOptions),
            totalResultCardinality,
            retainedItems,
            resultDigest,
            null,
            paths);
        cancellationToken.ThrowIfCancellationRequested();
        return output;
    }

    private static TInput DeserializeInput<TInput>(JsonElement input)
    {
        if (input.ValueKind != JsonValueKind.Object)
        {
            throw new ArgumentException(
                "A Phase 3 analysis worker input must be a JSON object.",
                nameof(input));
        }

        try
        {
            return input.Deserialize<TInput>(SerializerOptions)
                   ?? throw new ArgumentException(
                       "The Phase 3 analysis worker input must not be null.",
                       nameof(input));
        }
        catch (JsonException exception)
        {
            throw new ArgumentException(
                "The Phase 3 analysis worker input does not match its operation contract.",
                nameof(input),
                exception);
        }
    }

    private static void ValidateParameters<TParameters>(
        JsonElement value,
        TParameters expected)
    {
        if (value.ValueKind != JsonValueKind.Object)
        {
            throw new ArgumentException(
                "Canonical analysis parameters must be a JSON object.",
                nameof(value));
        }

        TParameters actual;
        try
        {
            actual = value.Deserialize<TParameters>(SerializerOptions)
                     ?? throw new ArgumentException(
                         "Canonical analysis parameters must not be null.",
                         nameof(value));
        }
        catch (JsonException exception)
        {
            throw new ArgumentException(
                "Canonical analysis parameters do not match the operation contract.",
                nameof(value),
                exception);
        }

        if (!EqualityComparer<TParameters>.Default.Equals(actual, expected))
        {
            throw new ArgumentException(
                "Canonical analysis parameters do not match the worker input.",
                nameof(value));
        }
    }

    private static IReadOnlyList<JsonElement> SerializeItems<TItem>(
        IEnumerable<TItem> items,
        CancellationToken cancellationToken)
    {
        var serialized = new List<JsonElement>();
        foreach (var item in items)
        {
            cancellationToken.ThrowIfCancellationRequested();
            serialized.Add(JsonSerializer.SerializeToElement(item, SerializerOptions));
        }

        cancellationToken.ThrowIfCancellationRequested();
        return Array.AsReadOnly(serialized.ToArray());
    }

    private static IReadOnlyList<JsonElement> CloneItems(
        IEnumerable<JsonElement> items,
        CancellationToken cancellationToken)
    {
        var cloned = new List<JsonElement>();
        foreach (var item in items)
        {
            cancellationToken.ThrowIfCancellationRequested();
            cloned.Add(item.Clone());
        }

        cancellationToken.ThrowIfCancellationRequested();
        return Array.AsReadOnly(cloned.ToArray());
    }

    private static GraphTargetIdentifiers CreateIdentifiers(Graph graph, string? targetNodeId)
    {
        var graphId = graph.Id > 0
            ? graph.Id.ToString(CultureInfo.InvariantCulture)
            : null;
        return new GraphTargetIdentifiers(
            graph.Slug,
            graphId,
            targetNodeId,
            Array.Empty<string>());
    }

    private static void ValidateCommonInput(string scenarioKey, Graph graph)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scenarioKey);
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentException.ThrowIfNullOrWhiteSpace(graph.Slug);
        if (graph.Nodes is null)
        {
            throw new ArgumentException("The analysis graph node collection must not be null.", nameof(graph));
        }

        if (graph.Edges is null)
        {
            throw new ArgumentException("The analysis graph edge collection must not be null.", nameof(graph));
        }
    }

    private sealed record CriticalCounterV1WorkerSummary(
        string TargetNodeId,
        string RequestedStrategy,
        string UsedStrategy,
        int CandidateCount,
        int? AutoCandidateCutoff,
        string StrategySelectionReason,
        decimal BaselineLogOdds,
        decimal BaselineProbability,
        decimal ResultingLogOdds,
        decimal ResultingProbability,
        decimal ThresholdLogOdds,
        decimal ThresholdProbability,
        decimal BelowThresholdMargin,
        bool ThresholdAttained,
        int SelectedCounterCount,
        bool SearchExhausted,
        bool ProvedUnattainable,
        bool OptimalCardinalityProven);

    private sealed record CriticalCounterV1WorkerDistribution(
        int CandidateCount,
        int SelectedCounterCount,
        int RecognizedSelectedCounterCount,
        int UnrecognizedSelectedCounterCount,
        long EvaluatedSubsetCount);

    private sealed record RobustnessV0WorkerLeastRobust(
        string NodeId,
        string Title,
        string Kind,
        int Rank,
        decimal RobustnessScore,
        decimal OriginalProbability,
        decimal HypotheticalProbability,
        decimal AbsoluteProbabilityDelta,
        decimal AccumulatedPathLogLikelihoodRatio);

    private sealed record RobustnessV0WorkerSummary(
        long RankedNodeCount,
        RobustnessV0WorkerLeastRobust? LeastRobust);

    private sealed record SinglePairPathV0WorkerItem(
        string StartNodeId,
        string TargetNodeId,
        string Strategy,
        bool PathFound,
        decimal? AccumulatedLogLikelihoodRatio);

    private sealed record SinglePairPathV0WorkerSummary(
        string StartNodeId,
        string TargetNodeId,
        bool PathFound);

    private sealed record SinglePairPathV0WorkerDistribution(long FoundPathCount);

    private sealed record LikelihoodRecalculateV0WorkerItem(
        int Order,
        string NodeId,
        decimal BeforeLogPosteriorOdds,
        decimal AfterLogPosteriorOdds);

    private sealed record LikelihoodRecalculateV0WorkerSummary(
        string ChangedNodeId,
        long RecalculatedNodeCount);

    private sealed record LikelihoodRecalculateV0WorkerDistribution(
        long Count,
        decimal? MinimumLogPosteriorOddsDelta,
        decimal? MaximumLogPosteriorOddsDelta);
}
