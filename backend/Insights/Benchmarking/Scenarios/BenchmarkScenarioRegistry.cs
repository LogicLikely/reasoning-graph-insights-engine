using System.Text.Json;
using Backend.Insights.Contracts;
using Backend.Seeding;

namespace Backend.Insights.Benchmarking;

public static class BenchmarkScenarioRegistry
{
    private static readonly IReadOnlyList<BenchmarkScenarioDefinition> OrderedScenarios =
    [
        Skipped("quick.graph-catalog.deferred", OperationKeys.GraphCatalog,
            "Graph catalog measurement is deferred to Phase 4 Goal 2."),
        Skipped("quick.graph-fetch.deferred", OperationKeys.GraphFetch,
            "Database, HTTP, and transfer measurement is deferred to Phase 4 Goal 2."),
        Skipped("quick.graph-search.deferred", OperationKeys.GraphSearch,
            "Search and browser adaptation measurement is deferred to Phase 4 Goal 2."),
        Scenario(
            "quick.strongest.balanced-1k",
            "Strongest downstream paths from the balanced root.",
            OperationKeys.PathStrongest,
            StressGraphSeedIds.Balanced1K,
            new { startNodeId = "n-00000", direction = "down" }),
        Scenario(
            "quick.single-pair.deep-1k.maximum",
            "Maximum bounded deep-chain path to root, isolated for recursive stack safety.",
            OperationKeys.PathSinglePair,
            StressGraphSeedIds.Deep1K,
            new { startNodeId = "n-00064", targetNodeId = "n-00000", requestedStrategy = OperationStrategyNames.Maximum },
            OperationStrategyNames.Maximum,
            requiresIsolation: true),
        Scenario(
            "quick.evidence.wide-1k",
            "Evidence-impact ranking at the wide root.",
            OperationKeys.EvidenceImpactRanking,
            StressGraphSeedIds.Wide1K,
            new { targetNodeId = "n-00000" }),
        Scenario(
            "quick.counter.exact.balanced-1k",
            "Candidate-limited exact critical-counter execution.",
            OperationKeys.CounterCriticalSet,
            StressGraphSeedIds.Balanced1K,
            new { targetNodeId = "n-00015", requestedStrategy = OperationStrategyNames.Exact, thresholdLogOdds = -1m, autoCandidateCutoff = (int?)null, candidateLimit = 8 },
            OperationStrategyNames.Exact,
            requiresIsolation: true),
        Scenario(
            "quick.counter.greedy.balanced-1k",
            "Bounded greedy critical-counter execution.",
            OperationKeys.CounterCriticalSet,
            StressGraphSeedIds.Balanced1K,
            new { targetNodeId = "n-00015", requestedStrategy = OperationStrategyNames.Greedy, thresholdLogOdds = -1m, autoCandidateCutoff = (int?)null, candidateLimit = 8 },
            OperationStrategyNames.Greedy),
        Scenario(
            "quick.counter.auto-exact.balanced-1k",
            "Auto critical-counter selection resolving to exact.",
            OperationKeys.CounterCriticalSet,
            StressGraphSeedIds.Balanced1K,
            new { targetNodeId = "n-00015", requestedStrategy = OperationStrategyNames.Auto, thresholdLogOdds = -1m, autoCandidateCutoff = 8, candidateLimit = 8 },
            OperationStrategyNames.Auto,
            requiresIsolation: true),
        Scenario(
            "quick.counter.auto-greedy.balanced-1k",
            "Auto critical-counter selection resolving to greedy.",
            OperationKeys.CounterCriticalSet,
            StressGraphSeedIds.Balanced1K,
            new { targetNodeId = "n-00015", requestedStrategy = OperationStrategyNames.Auto, thresholdLogOdds = -1m, autoCandidateCutoff = 1, candidateLimit = 8 },
            OperationStrategyNames.Auto,
            requiresIsolation: true),
        Scenario(
            "quick.robustness.balanced-1k",
            "Robustness ranking on bounded ordinary depth.",
            OperationKeys.NodeRobustness,
            StressGraphSeedIds.Balanced1K,
            new { }),
        Scenario(
            "quick.robustness.deep-1k",
            "Recursive deep robustness execution isolated from the runner.",
            OperationKeys.NodeRobustness,
            StressGraphSeedIds.Deep1K,
            new { },
            requiresIsolation: true),
        Scenario(
            "quick.likelihood.balanced-1k",
            "Likelihood recalculation from a deep balanced node.",
            OperationKeys.LikelihoodRecalculate,
            StressGraphSeedIds.Balanced1K,
            new { changedNodeId = "n-00999" }),
        new BenchmarkScenarioDefinition(
            "quick.counter.exact-wide-uncapped.skipped",
            "Unbounded wide exact search is excluded from quick.",
            BenchmarkProfiles.QuickKey,
            OperationKeys.CounterCriticalSet,
            StressGraphSeedIds.Wide1K,
            JsonSerializer.SerializeToElement(new { targetNodeId = "n-00000", requestedStrategy = OperationStrategyNames.Exact }),
            OperationStrategyNames.Exact,
            requiresIsolation: true,
            new BenchmarkSkipReason("exact-candidate-limit-required", "Quick exact scenarios require an explicit tractable candidate limit.")),
        new BenchmarkScenarioDefinition(
            "quick.robustness.deep-1k-in-process.skipped",
            "Unsafe in-process recursive robustness is excluded from quick.",
            BenchmarkProfiles.QuickKey,
            OperationKeys.NodeRobustness,
            StressGraphSeedIds.Deep1K,
            JsonSerializer.SerializeToElement(new { }),
            null,
            requiresIsolation: false,
            new BenchmarkSkipReason("hazardous-scenario-requires-isolation", "Recursive deep robustness must execute in the Phase 1 worker."))
    ];

    public static IReadOnlyList<BenchmarkScenarioDefinition> All => OrderedScenarios;

    public static IReadOnlyList<BenchmarkScenarioDefinition> ForProfile(string profileKey) =>
        OrderedScenarios.Where(scenario => string.Equals(scenario.ProfileKey, profileKey, StringComparison.Ordinal)).ToArray();

    public static BenchmarkScenarioDefinition Get(string key) =>
        OrderedScenarios.SingleOrDefault(scenario => string.Equals(scenario.Key, key, StringComparison.Ordinal))
        ?? throw new KeyNotFoundException($"Unknown benchmark scenario '{key}'.");

    private static BenchmarkScenarioDefinition Scenario<T>(
        string key,
        string description,
        string operationKey,
        string datasetId,
        T parameters,
        string? requestedStrategy = null,
        bool requiresIsolation = false) => new(
            key,
            description,
            BenchmarkProfiles.QuickKey,
            operationKey,
            datasetId,
            JsonSerializer.SerializeToElement(parameters),
            requestedStrategy,
            requiresIsolation);

    private static BenchmarkScenarioDefinition Skipped(
        string key,
        string operationKey,
        string message) => new(
            key,
            message,
            BenchmarkProfiles.QuickKey,
            operationKey,
            StressGraphSeedIds.Balanced1K,
            JsonSerializer.SerializeToElement(new { }),
            null,
            requiresIsolation: false,
            new BenchmarkSkipReason("deferred-to-phase-4-goal-2", message));
}
