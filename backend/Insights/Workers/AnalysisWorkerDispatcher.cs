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
    [property: JsonRequired] int? AutoCandidateCutoff);

public sealed record RobustnessV0WorkerInput(
    [property: JsonRequired] string ScenarioKey,
    [property: JsonRequired] Graph Graph);

public sealed record StrongestPathV1WorkerParameters(
    [property: JsonRequired] string StartNodeId,
    [property: JsonRequired] PathDirection Direction);

public sealed record EvidenceImpactV0WorkerParameters(
    [property: JsonRequired] string TargetNodeId);

public sealed record CriticalCounterV1WorkerParameters(
    [property: JsonRequired] string TargetNodeId,
    [property: JsonRequired] string RequestedStrategy,
    [property: JsonRequired] decimal ThresholdLogOdds,
    [property: JsonRequired] int? AutoCandidateCutoff);

public sealed record RobustnessV0WorkerParameters;

/// <summary>
/// Stateless projection from the Phase 1 worker request envelope to the four
/// Phase 3 analysis result contracts. Process lifetime, cancellation-frame
/// handling, and hard deadlines remain outside this class.
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
                input.AutoCandidateCutoff));
        cancellationToken.ThrowIfCancellationRequested();

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
}
