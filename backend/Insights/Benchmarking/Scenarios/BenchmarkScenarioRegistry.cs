using System.Text.Json;
using Backend.Insights.Contracts;
using Backend.Seeding;

namespace Backend.Insights.Benchmarking;

public static class BenchmarkScenarioRegistry
{
    private static readonly IReadOnlyList<BenchmarkScenarioDefinition> OrderedScenarios =
    [
        Scenario(
            "quick.graph-catalog.rest",
            "Catalog retrieval through real HTTP with every canonical stress graph installed during setup.",
            OperationKeys.GraphCatalog,
            StressGraphSeedIds.Balanced1K,
            new
            {
                stressGraphIds = StressGraphSeedCatalog.All.Select(specification => specification.Id).ToArray()
            },
            executionTarget: BenchmarkScenarioExecutionTarget.RestDatabaseLoaded),
        Scenario(
            "quick.graph-fetch.balanced-1k.rest",
            "Complete balanced 1K graph fetch through the real PostgreSQL-backed REST API.",
            OperationKeys.GraphFetch,
            StressGraphSeedIds.Balanced1K,
            new { },
            executionTarget: BenchmarkScenarioExecutionTarget.RestDatabaseLoaded),
        BrowserScenario(
            "quick.browser.collapsed.balanced-1k",
            "Initial collapsed GraphMap presentation after a real browser REST fetch.",
            OperationKeys.GraphFetch,
            StressGraphSeedIds.Balanced1K,
            new { },
            new BrowserJourneyDefinition(BrowserJourneyActions.Collapsed)),
        BrowserScenario(
            "quick.browser.full-expansion.balanced-1k",
            "Complete GraphMap expansion for the designated bounded 1K dataset.",
            OperationKeys.GraphFetch,
            StressGraphSeedIds.Balanced1K,
            new { },
            new BrowserJourneyDefinition(BrowserJourneyActions.FullExpansion)),
        BrowserScenario(
            "quick.browser.search.no-hit.balanced-1k",
            "Representative no-hit GraphMap search with observable zero-result metadata.",
            OperationKeys.GraphSearch,
            StressGraphSeedIds.Balanced1K,
            new { query = "__logiclikely_no_such_node__" },
            new BrowserJourneyDefinition(
                BrowserJourneyActions.Search,
                "__logiclikely_no_such_node__")),
        BrowserScenario(
            "quick.browser.search.compact.balanced-1k",
            "Representative compact GraphMap search for one shallow bounded result.",
            OperationKeys.GraphSearch,
            StressGraphSeedIds.Balanced1K,
            new { query = "n-00015" },
            new BrowserJourneyDefinition(BrowserJourneyActions.Search, "n-00015")),
        BrowserScenario(
            "quick.browser.result-render.strongest.balanced-1k",
            "Bounded textual strongest-path result rendering in the Storybook-only performance harness.",
            OperationKeys.PathStrongest,
            StressGraphSeedIds.Balanced1K,
            new { startNodeId = "n-00000", direction = "down" },
            new BrowserJourneyDefinition(BrowserJourneyActions.ResultRender)),
        Scenario(
            "quick.evidence.wide-1k.rest.database-loaded",
            "Evidence-impact ranking after the API loads the graph from PostgreSQL.",
            OperationKeys.EvidenceImpactRanking,
            StressGraphSeedIds.Wide1K,
            new { targetNodeId = "n-00000" },
            executionTarget: BenchmarkScenarioExecutionTarget.RestDatabaseLoaded),
        Scenario(
            "quick.evidence.wide-1k.rest.supplied-graph",
            "Evidence-impact ranking with the same canonical graph supplied in the HTTP request body.",
            OperationKeys.EvidenceImpactRanking,
            StressGraphSeedIds.Wide1K,
            new { targetNodeId = "n-00000" },
            executionTarget: BenchmarkScenarioExecutionTarget.RestSuppliedGraph),
        Scenario(
            "quick.robustness.balanced-1k.rest.database-loaded",
            "Robustness ranking after the API loads the graph from PostgreSQL.",
            OperationKeys.NodeRobustness,
            StressGraphSeedIds.Balanced1K,
            new { },
            executionTarget: BenchmarkScenarioExecutionTarget.RestDatabaseLoaded),
        Scenario(
            "quick.robustness.balanced-1k.rest.supplied-graph",
            "Robustness ranking with the same canonical graph supplied in the HTTP request body.",
            OperationKeys.NodeRobustness,
            StressGraphSeedIds.Balanced1K,
            new { },
            executionTarget: BenchmarkScenarioExecutionTarget.RestSuppliedGraph),
        new BenchmarkScenarioDefinition(
            "quick.strongest.rest.unsupported",
            "No semantically matching strongest-path REST endpoint is currently exposed.",
            BenchmarkProfiles.QuickKey,
            OperationKeys.PathStrongest,
            StressGraphSeedIds.Balanced1K,
            JsonSerializer.SerializeToElement(new { startNodeId = "n-00000", direction = "down" }),
            null,
            requiresIsolation: false,
            new BenchmarkSkipReason(
                "rest-operation-not-exposed",
                "The API does not expose the versioned strongest-path operation."),
            BenchmarkScenarioExecutionTarget.RestDatabaseLoaded),
        new BenchmarkScenarioDefinition(
            "quick.counter.rest.unsupported",
            "The existing minimal-counter endpoint is a legacy heuristic, not critical-counter-v1.",
            BenchmarkProfiles.QuickKey,
            OperationKeys.CounterCriticalSet,
            StressGraphSeedIds.Balanced1K,
            JsonSerializer.SerializeToElement(new
            {
                targetNodeId = "n-00015",
                requestedStrategy = OperationStrategyNames.Greedy,
                thresholdLogOdds = -1m,
                autoCandidateCutoff = (int?)null,
                candidateLimit = 8
            }),
            OperationStrategyNames.Greedy,
            requiresIsolation: false,
            new BenchmarkSkipReason(
                "rest-operation-semantic-mismatch",
                "The legacy minimal-counter route cannot be labeled as critical-counter-v1."),
            BenchmarkScenarioExecutionTarget.RestDatabaseLoaded),
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
            requiresIsolation: true,
            measureQualityComparison: true),
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
            new BenchmarkSkipReason("hazardous-scenario-requires-isolation", "Recursive deep robustness must execute in the Phase 1 worker.")),
        .. CreateStandardScenarios(),
        .. CreateColdScenarios()
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
        bool requiresIsolation = false,
        BenchmarkScenarioExecutionTarget executionTarget = BenchmarkScenarioExecutionTarget.InMemory,
        bool measureQualityComparison = false) => new(
            key,
            description,
            BenchmarkProfiles.QuickKey,
            operationKey,
            datasetId,
            JsonSerializer.SerializeToElement(parameters),
            requestedStrategy,
            requiresIsolation,
            executionTarget: executionTarget,
            measureQualityComparison: measureQualityComparison);

    private static BenchmarkScenarioDefinition BrowserScenario<T>(
        string key,
        string description,
        string operationKey,
        string datasetId,
        T parameters,
        BrowserJourneyDefinition browserJourney) => new(
            key,
            description,
            BenchmarkProfiles.QuickKey,
            operationKey,
            datasetId,
            JsonSerializer.SerializeToElement(parameters),
            null,
            requiresIsolation: false,
            executionTarget: BenchmarkScenarioExecutionTarget.Browser,
            browserJourney: browserJourney);

    private static IEnumerable<BenchmarkScenarioDefinition> CreateStandardScenarios()
    {
        yield return ProfileScenario(
            BenchmarkProfiles.StandardKey,
            "standard.graph-catalog.rest",
            "Aggregate catalog retrieval for all twelve canonical stress datasets through PostgreSQL and the real REST API.",
            OperationKeys.GraphCatalog,
            StressGraphSeedIds.Balanced1K,
            new
            {
                stressGraphIds = StressGraphSeedCatalog.All.Select(specification => specification.Id).ToArray()
            },
            executionTarget: BenchmarkScenarioExecutionTarget.RestDatabaseLoaded);

        foreach (var specification in StressGraphSeedCatalog.All)
        {
            var suffix = DatasetSuffix(specification);
            yield return ProfileScenario(
                BenchmarkProfiles.StandardKey,
                $"standard.graph-fetch.{suffix}.rest",
                $"Complete {suffix} graph fetch through the PostgreSQL-backed REST API.",
                OperationKeys.GraphFetch,
                specification.Id,
                new { },
                executionTarget: BenchmarkScenarioExecutionTarget.RestDatabaseLoaded);
        }

        foreach (var specification in StressGraphSeedCatalog.All)
        {
            var suffix = DatasetSuffix(specification);
            yield return ProfileBrowserScenario(
                BenchmarkProfiles.StandardKey,
                $"standard.browser.collapsed.{suffix}",
                $"Initial collapsed GraphMap presentation for {suffix} after a real REST fetch.",
                OperationKeys.GraphFetch,
                specification.Id,
                new { },
                new BrowserJourneyDefinition(BrowserJourneyActions.Collapsed));
        }

        foreach (var specification in StressGraphSeedCatalog.All)
        {
            var suffix = DatasetSuffix(specification);
            var isDesignatedExpansion = string.Equals(
                specification.Id,
                StressGraphSeedIds.Balanced1K,
                StringComparison.Ordinal);
            yield return ProfileBrowserScenario(
                BenchmarkProfiles.StandardKey,
                isDesignatedExpansion
                    ? $"standard.browser.full-expansion.{suffix}"
                    : $"standard.browser.full-expansion.{suffix}.skipped",
                isDesignatedExpansion
                    ? "Complete GraphMap expansion for the single designated bounded balanced-1k dataset."
                    : $"Complete GraphMap expansion for {suffix} remains registered but is outside the designated safe expansion case.",
                OperationKeys.GraphFetch,
                specification.Id,
                new { },
                new BrowserJourneyDefinition(BrowserJourneyActions.FullExpansion),
                isDesignatedExpansion
                    ? null
                    : new BenchmarkSkipReason(
                        "browser-full-expansion-designated-small-only",
                        "Only balanced-1k is designated for complete GraphMap expansion; every other expansion is explicitly skipped."));
        }

        foreach (var specification in StressGraphSeedCatalog.All)
        {
            var suffix = DatasetSuffix(specification);
            yield return ProfileBrowserScenario(
                BenchmarkProfiles.StandardKey,
                $"standard.browser.search.no-hit.{suffix}",
                $"No-hit GraphMap search for {suffix} with observable zero-result metadata.",
                OperationKeys.GraphSearch,
                specification.Id,
                new { query = "__logiclikely_no_such_node__" },
                new BrowserJourneyDefinition(
                    BrowserJourneyActions.Search,
                    "__logiclikely_no_such_node__"));
        }

        foreach (var specification in StressGraphSeedCatalog.All)
        {
            var suffix = DatasetSuffix(specification);
            yield return ProfileBrowserScenario(
                BenchmarkProfiles.StandardKey,
                $"standard.browser.search.compact.{suffix}",
                $"Shallow compact-result GraphMap search for {suffix}.",
                OperationKeys.GraphSearch,
                specification.Id,
                new { query = "n-00015" },
                new BrowserJourneyDefinition(BrowserJourneyActions.Search, "n-00015"));
        }

        foreach (var specification in StressGraphSeedCatalog.All.Where(value =>
                     string.Equals(value.Shape, "deep", StringComparison.Ordinal) &&
                     value.NodeCount >= 10_000))
        {
            var suffix = DatasetSuffix(specification);
            var deepestNodeId = DeterministicStressGraphFixtureFactory.NodeId(
                specification.NodeCount - 1);
            yield return ProfileBrowserScenario(
                BenchmarkProfiles.StandardKey,
                $"standard.browser.search.materializing.{suffix}.skipped",
                $"A deepest-node search for {suffix} would materialize most of the deep chain and remains explicitly skipped.",
                OperationKeys.GraphSearch,
                specification.Id,
                new { query = deepestNodeId },
                new BrowserJourneyDefinition(
                    BrowserJourneyActions.Search,
                    deepestNodeId,
                    MayMaterializeMostGraph: true),
                new BenchmarkSkipReason(
                    "browser-deep-search-materialization-unsafe",
                    "Large deep-chain GraphMap search is skipped because its required ancestor union would materialize most of the graph."));
        }

        foreach (var specification in StressGraphSeedCatalog.All)
        {
            var suffix = DatasetSuffix(specification);
            var isolated = RequiresDeepIsolation(specification);
            yield return ProfileScenario(
                BenchmarkProfiles.StandardKey,
                $"standard.strongest.{suffix}",
                isolated
                    ? $"Deep-recursive strongest downstream paths for {suffix}, contained in a fresh worker."
                    : $"Warm in-process strongest downstream paths for {suffix}.",
                OperationKeys.PathStrongest,
                specification.Id,
                new { startNodeId = "n-00000", direction = "down" },
                requiresIsolation: isolated);
        }

        foreach (var specification in StressGraphSeedCatalog.All)
        {
            var suffix = DatasetSuffix(specification);
            var isolated = RequiresDeepIsolation(specification);
            yield return ProfileScenario(
                BenchmarkProfiles.StandardKey,
                $"standard.evidence.{suffix}",
                isolated
                    ? $"Deep-recursive evidence-impact ranking for {suffix}, contained in a fresh worker."
                    : $"Warm in-process evidence-impact ranking for {suffix}.",
                OperationKeys.EvidenceImpactRanking,
                specification.Id,
                new { targetNodeId = "n-00000" },
                requiresIsolation: isolated);
        }

        foreach (var specification in StressGraphSeedCatalog.All)
        {
            var suffix = DatasetSuffix(specification);
            var recordQuality = specification.NodeCount == 1_000 &&
                specification.Shape is "balanced" or "shared-diamond";
            yield return ProfileScenario(
                BenchmarkProfiles.StandardKey,
                $"standard.counter.exact.{suffix}",
                $"Worker-isolated candidate-limited exact critical-counter execution for {suffix}.",
                OperationKeys.CounterCriticalSet,
                specification.Id,
                CriticalCounterParameters(
                    specification,
                    OperationStrategyNames.Exact,
                    autoCandidateCutoff: null),
                OperationStrategyNames.Exact,
                requiresIsolation: true,
                measureQualityComparison: recordQuality);
        }

        foreach (var specification in StressGraphSeedCatalog.All)
        {
            var suffix = DatasetSuffix(specification);
            var isolated = RequiresDeepIsolation(specification);
            yield return ProfileScenario(
                BenchmarkProfiles.StandardKey,
                $"standard.counter.greedy.{suffix}",
                isolated
                    ? $"Deep bounded greedy critical-counter execution for {suffix}, contained in a fresh worker."
                    : $"Warm in-process bounded greedy critical-counter execution for {suffix}.",
                OperationKeys.CounterCriticalSet,
                specification.Id,
                CriticalCounterParameters(
                    specification,
                    OperationStrategyNames.Greedy,
                    autoCandidateCutoff: null),
                OperationStrategyNames.Greedy,
                requiresIsolation: isolated);
        }

        foreach (var specification in StressGraphSeedCatalog.All)
        {
            var suffix = DatasetSuffix(specification);
            yield return ProfileScenario(
                BenchmarkProfiles.StandardKey,
                $"standard.counter.auto-exact.{suffix}",
                $"Worker-isolated auto critical-counter execution resolving to exact for {suffix}.",
                OperationKeys.CounterCriticalSet,
                specification.Id,
                CriticalCounterParameters(
                    specification,
                    OperationStrategyNames.Auto,
                    autoCandidateCutoff: 8),
                OperationStrategyNames.Auto,
                requiresIsolation: true);
        }

        foreach (var specification in StressGraphSeedCatalog.All)
        {
            var suffix = DatasetSuffix(specification);
            var wide = string.Equals(specification.Shape, "wide", StringComparison.Ordinal);
            yield return ProfileScenario(
                BenchmarkProfiles.StandardKey,
                wide
                    ? $"standard.counter.auto-greedy.{suffix}.skipped"
                    : $"standard.counter.auto-greedy.{suffix}",
                wide
                    ? $"A bounded wide-graph target for {suffix} has zero eligible descendants, so auto cannot honestly resolve to greedy."
                    : $"Worker-isolated auto critical-counter execution resolving to greedy for {suffix}.",
                OperationKeys.CounterCriticalSet,
                specification.Id,
                CriticalCounterParameters(
                    specification,
                    OperationStrategyNames.Auto,
                    autoCandidateCutoff: 1),
                OperationStrategyNames.Auto,
                requiresIsolation: true,
                skipReason: wide
                    ? new BenchmarkSkipReason(
                        "auto-greedy-bounded-wide-target-unavailable",
                        "Wide graphs have only a root and leaves: a leaf target has zero candidates, while the root exceeds the tractable candidate limit, so no bounded target can resolve auto to greedy.")
                    : null);
        }

        foreach (var specification in StressGraphSeedCatalog.All)
        {
            var suffix = DatasetSuffix(specification);
            yield return ProfileScenario(
                BenchmarkProfiles.StandardKey,
                $"standard.robustness.{suffix}",
                $"Worker-isolated node-robustness ranking for {suffix}.",
                OperationKeys.NodeRobustness,
                specification.Id,
                new { },
                requiresIsolation: true);
        }

        foreach (var specification in StressGraphSeedCatalog.All)
        {
            var suffix = DatasetSuffix(specification);
            var isolated = RequiresDeepIsolation(specification);
            yield return ProfileScenario(
                BenchmarkProfiles.StandardKey,
                $"standard.likelihood.{suffix}",
                isolated
                    ? $"Deep-recursive likelihood recalculation for {suffix}, contained in a fresh worker."
                    : $"Warm in-process likelihood recalculation from the deepest node of {suffix}.",
                OperationKeys.LikelihoodRecalculate,
                specification.Id,
                new
                {
                    changedNodeId = DeterministicStressGraphFixtureFactory.NodeId(
                        specification.NodeCount - 1)
                },
                requiresIsolation: isolated);
        }
    }

    private static IEnumerable<BenchmarkScenarioDefinition> CreateColdScenarios()
    {
        yield return ProfileScenario(
            BenchmarkProfiles.ColdKey,
            "cold.strongest.balanced-1k.isolated-worker",
            "A fresh isolated worker process executes the bounded strongest-path case; runner, OS, and filesystem caches are not reset.",
            OperationKeys.PathStrongest,
            StressGraphSeedIds.Balanced1K,
            new { startNodeId = "n-00000", direction = "down" },
            requiresIsolation: true);

        yield return ProfileBrowserScenario(
            BenchmarkProfiles.ColdKey,
            "cold.browser.result-render.strongest.balanced-1k",
            "A newly launched production Chromium process renders a bounded strongest-path result without API or PostgreSQL access.",
            OperationKeys.PathStrongest,
            StressGraphSeedIds.Balanced1K,
            new { startNodeId = "n-00000", direction = "down" },
            new BrowserJourneyDefinition(BrowserJourneyActions.ResultRender));
        yield return ProfileBrowserScenario(
            BenchmarkProfiles.ColdKey,
            "cold.browser.result-render.counter-exact.balanced-1k",
            "A newly launched production Chromium process renders a bounded candidate-limited critical-counter result without API or PostgreSQL access.",
            OperationKeys.CounterCriticalSet,
            StressGraphSeedIds.Balanced1K,
            CriticalCounterParameters(
                StressGraphSeedCatalog.Resolve([StressGraphSeedIds.Balanced1K]).Single(),
                OperationStrategyNames.Exact,
                autoCandidateCutoff: null),
            new BrowserJourneyDefinition(BrowserJourneyActions.ResultRender),
            requestedStrategy: OperationStrategyNames.Exact);
        yield return ProfileBrowserScenario(
            BenchmarkProfiles.ColdKey,
            "cold.browser.result-render.evidence.wide-1k",
            "A newly launched production Chromium process renders a bounded evidence-impact result without API or PostgreSQL access.",
            OperationKeys.EvidenceImpactRanking,
            StressGraphSeedIds.Wide1K,
            new { targetNodeId = "n-00000" },
            new BrowserJourneyDefinition(BrowserJourneyActions.ResultRender));
        yield return ProfileBrowserScenario(
            BenchmarkProfiles.ColdKey,
            "cold.browser.result-render.robustness.balanced-1k",
            "A newly launched production Chromium process renders a bounded robustness result without API or PostgreSQL access.",
            OperationKeys.NodeRobustness,
            StressGraphSeedIds.Balanced1K,
            new { },
            new BrowserJourneyDefinition(BrowserJourneyActions.ResultRender));

        var databaseColdSkip = new BenchmarkSkipReason(
            "cold-api-postgresql-state-not-reset",
            "The API process, connection-pool state, PostgreSQL caches, and OS caches are not reset, so this journey cannot be labeled cold.");
        yield return ProfileScenario(
            BenchmarkProfiles.ColdKey,
            "cold.graph-catalog.rest.skipped",
            "Cold aggregate catalog retrieval remains explicit but cannot run without resetting API and PostgreSQL state.",
            OperationKeys.GraphCatalog,
            StressGraphSeedIds.Balanced1K,
            new
            {
                stressGraphIds = StressGraphSeedCatalog.All.Select(specification => specification.Id).ToArray()
            },
            skipReason: databaseColdSkip,
            executionTarget: BenchmarkScenarioExecutionTarget.RestDatabaseLoaded);
        yield return ProfileScenario(
            BenchmarkProfiles.ColdKey,
            "cold.graph-fetch.balanced-1k.rest.skipped",
            "Cold full REST fetch remains explicit but cannot run without resetting API and PostgreSQL state.",
            OperationKeys.GraphFetch,
            StressGraphSeedIds.Balanced1K,
            new { },
            skipReason: databaseColdSkip,
            executionTarget: BenchmarkScenarioExecutionTarget.RestDatabaseLoaded);

        var browserGraphColdSkip = new BenchmarkSkipReason(
            "cold-browser-api-postgresql-state-not-reset",
            "Although Chromium would be newly launched, the API process, PostgreSQL state, and OS caches are not reset, so the graph journey cannot be labeled cold.");
        yield return ProfileBrowserScenario(
            BenchmarkProfiles.ColdKey,
            "cold.browser.collapsed.balanced-1k.skipped",
            "Cold collapsed graph presentation remains explicit but its API and PostgreSQL inputs are not reset.",
            OperationKeys.GraphFetch,
            StressGraphSeedIds.Balanced1K,
            new { },
            new BrowserJourneyDefinition(BrowserJourneyActions.Collapsed),
            browserGraphColdSkip);
        yield return ProfileBrowserScenario(
            BenchmarkProfiles.ColdKey,
            "cold.browser.search.compact.balanced-1k.skipped",
            "Cold compact graph search remains explicit but its API and PostgreSQL inputs are not reset.",
            OperationKeys.GraphSearch,
            StressGraphSeedIds.Balanced1K,
            new { query = "n-00015" },
            new BrowserJourneyDefinition(BrowserJourneyActions.Search, "n-00015"),
            browserGraphColdSkip);
        yield return ProfileBrowserScenario(
            BenchmarkProfiles.ColdKey,
            "cold.browser.full-expansion.balanced-1k.skipped",
            "Cold full graph expansion remains explicit but its API and PostgreSQL inputs are not reset.",
            OperationKeys.GraphFetch,
            StressGraphSeedIds.Balanced1K,
            new { },
            new BrowserJourneyDefinition(BrowserJourneyActions.FullExpansion),
            browserGraphColdSkip);
    }

    private static BenchmarkScenarioDefinition ProfileScenario<T>(
        string profileKey,
        string key,
        string description,
        string operationKey,
        string datasetId,
        T parameters,
        string? requestedStrategy = null,
        bool requiresIsolation = false,
        BenchmarkSkipReason? skipReason = null,
        BenchmarkScenarioExecutionTarget executionTarget = BenchmarkScenarioExecutionTarget.InMemory,
        bool measureQualityComparison = false) => new(
            key,
            description,
            profileKey,
            operationKey,
            datasetId,
            JsonSerializer.SerializeToElement(parameters),
            requestedStrategy,
            requiresIsolation,
            skipReason,
            executionTarget,
            measureQualityComparison: measureQualityComparison);

    private static BenchmarkScenarioDefinition ProfileBrowserScenario<T>(
        string profileKey,
        string key,
        string description,
        string operationKey,
        string datasetId,
        T parameters,
        BrowserJourneyDefinition browserJourney,
        BenchmarkSkipReason? skipReason = null,
        string? requestedStrategy = null) => new(
            key,
            description,
            profileKey,
            operationKey,
            datasetId,
            JsonSerializer.SerializeToElement(parameters),
            requestedStrategy,
            requiresIsolation: false,
            skipReason,
            BenchmarkScenarioExecutionTarget.Browser,
            browserJourney);

    private static object CriticalCounterParameters(
        StressGraphSeedSpec specification,
        string requestedStrategy,
        int? autoCandidateCutoff) => new
        {
            targetNodeId = CriticalCounterTargetNodeId(specification),
            requestedStrategy,
            thresholdLogOdds = -1m,
            autoCandidateCutoff,
            candidateLimit = 8
        };

    private static string CriticalCounterTargetNodeId(StressGraphSeedSpec specification)
    {
        var index = specification.Shape switch
        {
            "balanced" => BalancedCriticalCounterTargetIndex(specification.NodeCount),
            "shared-diamond" => (specification.NodeCount / 16) - 1,
            "deep" => specification.NodeCount - 19,
            // A wide graph has no bounded target with a non-zero descendant
            // candidate set. A leaf is still a valid bounded exact/greedy
            // case; auto-greedy is registered separately as a structured skip.
            "wide" => 15,
            _ => throw new ArgumentOutOfRangeException(
                nameof(specification),
                specification.Shape,
                "Unknown stress graph shape.")
        };
        return DeterministicStressGraphFixtureFactory.NodeId(index);
    }

    private static int BalancedCriticalCounterTargetIndex(int nodeCount)
    {
        var index = (nodeCount / 16) - 3;
        // Indices ending in 2 are objection candidates and cannot themselves
        // be a critical-counter target. The adjacent parent-level node retains
        // the same bounded 2-3 candidate descendant set in the frozen corpus.
        return index % 10 == 2 ? index - 1 : index;
    }

    private static bool RequiresDeepIsolation(StressGraphSeedSpec specification) =>
        string.Equals(specification.Shape, "deep", StringComparison.Ordinal);

    private static string DatasetSuffix(StressGraphSeedSpec specification) =>
        specification.Id.StartsWith("stress-", StringComparison.Ordinal)
            ? specification.Id["stress-".Length..]
            : throw new InvalidOperationException(
                $"Canonical stress dataset ID '{specification.Id}' does not use the expected prefix.");
}
