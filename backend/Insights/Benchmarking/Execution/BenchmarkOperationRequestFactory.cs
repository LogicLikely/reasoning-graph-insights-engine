using System.Text.Json;
using Backend.Calculation;
using Backend.Insights.Contracts;
using Backend.Insights.Workers;

namespace Backend.Insights.Benchmarking;

public sealed record PreparedBenchmarkOperation(
    WorkerRequestFrame Request,
    StrategySelection Strategy,
    string? TargetNodeId);

public static class BenchmarkOperationRequestFactory
{
    private static readonly JsonSerializerOptions JsonOptions = CanonicalJson.CreateSerializerOptions();

    public static PreparedBenchmarkOperation Create(
        BenchmarkScenarioDefinition scenario,
        DeterministicStressGraphFixture fixture,
        JsonElement parameters,
        Guid runId,
        Guid sampleId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scenario);
        ArgumentNullException.ThrowIfNull(fixture);
        cancellationToken.ThrowIfCancellationRequested();

        var canonicalParameters = new CanonicalParameters(
            parameters.Clone(),
            CanonicalJson.ComputeSha256(parameters));

        if (scenario.OperationKey is
            OperationKeys.GraphCatalog or
            OperationKeys.GraphFetch or
            OperationKeys.GraphSearch)
        {
            var restInput = JsonSerializer.SerializeToElement(new
            {
                scenarioKey = scenario.Key,
                datasetId = scenario.DatasetId,
                executionBoundary = "rest-api"
            }, JsonOptions);
            return new PreparedBenchmarkOperation(
                new WorkerRequestFrame(
                    runId,
                    sampleId,
                    scenario.OperationKey,
                    InsightOperationRegistry.Get(scenario.OperationKey).SemanticIdentity,
                    canonicalParameters,
                    restInput),
                new StrategySelection(null, null),
                null);
        }

        var graph = fixture.CreateGraph();
        object input;
        StrategySelection strategy;
        string? targetNodeId;

        switch (scenario.OperationKey)
        {
            case OperationKeys.PathStrongest:
            {
                var value = Read<StrongestPathV1WorkerParameters>(parameters);
                input = new StrongestPathV1WorkerInput(scenario.Key, graph, value.StartNodeId, value.Direction);
                strategy = new StrategySelection(null, null);
                targetNodeId = value.StartNodeId;
                break;
            }
            case OperationKeys.PathSinglePair:
            {
                var value = Read<SinglePairPathV0WorkerParameters>(parameters);
                input = new SinglePairPathV0WorkerInput(
                    scenario.Key, graph, value.StartNodeId, value.TargetNodeId, value.RequestedStrategy);
                strategy = new StrategySelection(value.RequestedStrategy, value.RequestedStrategy);
                targetNodeId = value.TargetNodeId;
                break;
            }
            case OperationKeys.EvidenceImpactRanking:
            {
                var value = Read<EvidenceImpactV0WorkerParameters>(parameters);
                input = new EvidenceImpactV0WorkerInput(scenario.Key, graph, value.TargetNodeId);
                strategy = new StrategySelection(null, null);
                targetNodeId = value.TargetNodeId;
                break;
            }
            case OperationKeys.CounterCriticalSet:
            {
                var value = Read<CriticalCounterV1WorkerParameters>(parameters);
                if (value.RequestedStrategy is OperationStrategyNames.Exact or OperationStrategyNames.Auto &&
                    value.CandidateLimit is not > 0)
                {
                    throw new ArgumentException(
                        "Quick exact and auto critical-counter scenarios require a positive candidateLimit.",
                        nameof(parameters));
                }

                var candidates = value.CandidateLimit.HasValue
                    ? CriticalCounterCandidateGuard.RequireAtMost(
                        graph, value.TargetNodeId, value.CandidateLimit.Value, cancellationToken)
                    : new CriticalCounterCandidateLimit(
                        int.MaxValue,
                        CriticalCounterV1Contract.GetEligibleCandidateNodeIds(
                            graph, value.TargetNodeId, cancellationToken));
                var used = value.RequestedStrategy switch
                {
                    OperationStrategyNames.Auto when value.AutoCandidateCutoff.HasValue &&
                        candidates.ActualCandidateCount <= value.AutoCandidateCutoff.Value => OperationStrategyNames.Exact,
                    OperationStrategyNames.Auto when value.AutoCandidateCutoff.HasValue => OperationStrategyNames.Greedy,
                    OperationStrategyNames.Exact => OperationStrategyNames.Exact,
                    OperationStrategyNames.Greedy => OperationStrategyNames.Greedy,
                    _ => throw new ArgumentException("Critical-counter strategy or auto cutoff is invalid.", nameof(parameters))
                };
                input = new CriticalCounterV1WorkerInput(
                    scenario.Key,
                    graph,
                    value.TargetNodeId,
                    value.RequestedStrategy,
                    value.ThresholdLogOdds,
                    value.AutoCandidateCutoff,
                    value.CandidateLimit);
                strategy = new StrategySelection(value.RequestedStrategy, used);
                targetNodeId = value.TargetNodeId;
                break;
            }
            case OperationKeys.NodeRobustness:
                _ = Read<RobustnessV0WorkerParameters>(parameters);
                input = new RobustnessV0WorkerInput(scenario.Key, graph);
                strategy = new StrategySelection(null, null);
                targetNodeId = null;
                break;
            case OperationKeys.LikelihoodRecalculate:
            {
                var value = Read<LikelihoodRecalculateV0WorkerParameters>(parameters);
                input = new LikelihoodRecalculateV0WorkerInput(scenario.Key, graph, value.ChangedNodeId);
                strategy = new StrategySelection(null, null);
                targetNodeId = value.ChangedNodeId;
                break;
            }
            default:
                throw new NotSupportedException(
                    $"Operation '{scenario.OperationKey}' is not a Goal 1 in-memory operation.");
        }

        return new PreparedBenchmarkOperation(
            new WorkerRequestFrame(
                runId,
                sampleId,
                scenario.OperationKey,
                InsightOperationRegistry.Get(scenario.OperationKey).SemanticIdentity,
                canonicalParameters,
                JsonSerializer.SerializeToElement(input, JsonOptions)),
            strategy,
            targetNodeId);
    }

    private static T Read<T>(JsonElement parameters) =>
        parameters.Deserialize<T>(JsonOptions)
        ?? throw new ArgumentException($"Parameters do not match {typeof(T).Name}.", nameof(parameters));
}
