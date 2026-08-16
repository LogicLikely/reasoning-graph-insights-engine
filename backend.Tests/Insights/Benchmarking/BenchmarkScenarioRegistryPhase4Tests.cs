using Backend.Insights.Benchmarking;
using Backend.Insights.Contracts;
using Backend.Seeding;

namespace backend.Tests.Insights.Benchmarking;

[TestClass]
public sealed class BenchmarkScenarioRegistryPhase4Tests
{
    private static readonly string[] DatasetIds =
        StressGraphSeedCatalog.All.Select(specification => specification.Id).ToArray();

    [TestMethod]
    public void StandardMatrix_CoversEveryRequiredDatasetAndOperationInDeterministicGroupOrder()
    {
        var first = BenchmarkScenarioRegistry.ForProfile(BenchmarkProfiles.StandardKey);
        var second = BenchmarkScenarioRegistry.ForProfile(BenchmarkProfiles.StandardKey);

        Assert.AreEqual(159, first.Count);
        CollectionAssert.AreEqual(
            first.Select(scenario => scenario.Key).ToArray(),
            second.Select(scenario => scenario.Key).ToArray());
        Assert.AreEqual(first.Count, first.Select(scenario => scenario.Key).Distinct().Count());
        Assert.AreEqual("standard.graph-catalog.rest", first[0].Key);
        Assert.AreEqual("standard.likelihood.shared-diamond-100k", first[^1].Key);

        AssertDatasetGroup(first, "standard.graph-fetch.", ".rest");
        AssertDatasetGroup(first, "standard.browser.collapsed.");
        AssertDatasetGroup(first, "standard.browser.full-expansion.", allowSkippedSuffix: true);
        AssertDatasetGroup(first, "standard.browser.search.no-hit.");
        AssertDatasetGroup(first, "standard.browser.search.compact.");
        AssertDatasetGroup(first, "standard.strongest.");
        AssertDatasetGroup(first, "standard.evidence.");
        AssertDatasetGroup(first, "standard.counter.exact.");
        AssertDatasetGroup(first, "standard.counter.greedy.");
        AssertDatasetGroup(first, "standard.counter.auto-exact.");
        AssertDatasetGroup(first, "standard.counter.auto-greedy.");
        AssertDatasetGroup(first, "standard.robustness.");
        AssertDatasetGroup(first, "standard.likelihood.");

        var catalog = first.Single(scenario => scenario.Key == "standard.graph-catalog.rest");
        CollectionAssert.AreEqual(
            DatasetIds,
            catalog.Parameters.GetProperty("stressGraphIds")
                .EnumerateArray()
                .Select(value => value.GetString())
                .ToArray());

        var groupStarts = new[]
        {
            "standard.graph-catalog.",
            "standard.graph-fetch.",
            "standard.browser.collapsed.",
            "standard.browser.full-expansion.",
            "standard.browser.search.no-hit.",
            "standard.browser.search.compact.",
            "standard.browser.search.materializing.",
            "standard.strongest.",
            "standard.evidence.",
            "standard.counter.exact.",
            "standard.counter.greedy.",
            "standard.counter.auto-exact.",
            "standard.counter.auto-greedy.",
            "standard.robustness.",
            "standard.likelihood."
        };
        var priorIndex = -1;
        foreach (var prefix in groupStarts)
        {
            var index = first.ToList().FindIndex(scenario => scenario.Key.StartsWith(prefix, StringComparison.Ordinal));
            Assert.IsTrue(index > priorIndex, $"Scenario group '{prefix}' is out of order.");
            priorIndex = index;
        }
    }

    [TestMethod]
    public void StandardBrowserSafety_RegistersEveryExpansionAndUnsafeDeepSearchAsStructuredOutcomes()
    {
        var standard = BenchmarkScenarioRegistry.ForProfile(BenchmarkProfiles.StandardKey);
        var expansions = standard
            .Where(scenario => scenario.BrowserJourney?.Action == BrowserJourneyActions.FullExpansion)
            .ToArray();

        Assert.AreEqual(12, expansions.Length);
        var runnable = expansions.Where(scenario => !scenario.IsSkipped).ToArray();
        Assert.AreEqual(1, runnable.Length);
        Assert.AreEqual(StressGraphSeedIds.Balanced1K, runnable[0].DatasetId);
        Assert.IsTrue(expansions.Where(scenario => scenario.IsSkipped).All(scenario =>
            scenario.SkipReason?.Code == "browser-full-expansion-designated-small-only"));

        var materializing = standard.Where(scenario =>
            scenario.Key.StartsWith("standard.browser.search.materializing.", StringComparison.Ordinal)).ToArray();
        CollectionAssert.AreEqual(
            new[] { StressGraphSeedIds.Deep10K, StressGraphSeedIds.Deep100K },
            materializing.Select(scenario => scenario.DatasetId).ToArray());
        Assert.IsTrue(materializing.All(scenario =>
            scenario.IsSkipped &&
            scenario.SkipReason?.Code == "browser-deep-search-materialization-unsafe" &&
            scenario.BrowserJourney is
            {
                Action: BrowserJourneyActions.Search,
                MayMaterializeMostGraph: true
            }));

        var safeSearches = standard.Where(scenario =>
            scenario.Key.StartsWith("standard.browser.search.no-hit.", StringComparison.Ordinal) ||
            scenario.Key.StartsWith("standard.browser.search.compact.", StringComparison.Ordinal)).ToArray();
        Assert.AreEqual(24, safeSearches.Length);
        Assert.IsTrue(safeSearches.All(scenario => !scenario.IsSkipped));
    }

    [TestMethod]
    public void StandardAlgorithms_IsolateOnlyRequiredHazardsAndQualityIsLimitedToNontrivialTractableCases()
    {
        var standard = BenchmarkScenarioRegistry.ForProfile(BenchmarkProfiles.StandardKey);
        var analysisPrefixes = new[]
        {
            "standard.strongest.",
            "standard.evidence.",
            "standard.counter.exact.",
            "standard.counter.greedy.",
            "standard.counter.auto-exact.",
            "standard.counter.auto-greedy.",
            "standard.robustness.",
            "standard.likelihood."
        };
        var analyses = standard.Where(scenario => analysisPrefixes.Any(prefix =>
            scenario.Key.StartsWith(prefix, StringComparison.Ordinal))).ToArray();

        Assert.AreEqual(96, analyses.Length);
        Assert.IsTrue(standard.Where(scenario =>
            scenario.Key.StartsWith("standard.robustness.", StringComparison.Ordinal)).All(scenario =>
            scenario.RequiresIsolation));
        Assert.IsTrue(standard.Where(scenario =>
            scenario.Key.StartsWith("standard.counter.exact.", StringComparison.Ordinal) ||
            scenario.Key.StartsWith("standard.counter.auto-", StringComparison.Ordinal)).All(scenario =>
            scenario.RequiresIsolation));

        foreach (var prefix in new[]
                 {
                     "standard.strongest.",
                     "standard.evidence.",
                     "standard.counter.greedy.",
                     "standard.likelihood."
                 })
        {
            var group = standard.Where(scenario =>
                scenario.Key.StartsWith(prefix, StringComparison.Ordinal)).ToArray();
            Assert.AreEqual(12, group.Length);
            Assert.IsTrue(group.Where(scenario => scenario.DatasetId.Contains("-deep-", StringComparison.Ordinal))
                .All(scenario => scenario.RequiresIsolation));
            Assert.IsTrue(group.Where(scenario => !scenario.DatasetId.Contains("-deep-", StringComparison.Ordinal))
                .All(scenario => !scenario.RequiresIsolation));
        }

        var exact = standard.Where(scenario =>
            scenario.Key.StartsWith("standard.counter.exact.", StringComparison.Ordinal)).ToArray();
        Assert.AreEqual(12, exact.Length);
        Assert.IsTrue(exact.All(scenario =>
            scenario.Parameters.GetProperty("candidateLimit").GetInt32() == 8 &&
            scenario.RequestedStrategy == OperationStrategyNames.Exact));
        CollectionAssert.AreEqual(
            new[]
            {
                StressGraphSeedIds.Balanced1K,
                StressGraphSeedIds.SharedDiamond1K
            },
            exact.Where(scenario => scenario.MeasureQualityComparison)
                .Select(scenario => scenario.DatasetId)
                .ToArray());
        Assert.IsTrue(BenchmarkScenarioRegistry.Get("quick.counter.exact.balanced-1k")
            .MeasureQualityComparison);
    }

    [TestMethod]
    [Timeout(180_000)]
    public void StandardCriticalScenarios_AllRunnableRequestsPrepareWithinLimitAndResolveNamedStrategy()
    {
        var critical = BenchmarkScenarioRegistry.ForProfile(BenchmarkProfiles.StandardKey)
            .Where(scenario =>
                scenario.Key.StartsWith("standard.counter.exact.", StringComparison.Ordinal) ||
                scenario.Key.StartsWith("standard.counter.greedy.", StringComparison.Ordinal) ||
                scenario.Key.StartsWith("standard.counter.auto-exact.", StringComparison.Ordinal) ||
                scenario.Key.StartsWith("standard.counter.auto-greedy.", StringComparison.Ordinal))
            .ToArray();

        Assert.AreEqual(48, critical.Length);
        var autoGreedySkips = critical.Where(scenario =>
            scenario.Key.StartsWith("standard.counter.auto-greedy.", StringComparison.Ordinal) &&
            scenario.IsSkipped).ToArray();
        CollectionAssert.AreEqual(
            new[] { StressGraphSeedIds.Wide1K, StressGraphSeedIds.Wide10K, StressGraphSeedIds.Wide100K },
            autoGreedySkips.Select(scenario => scenario.DatasetId).ToArray());
        Assert.IsTrue(autoGreedySkips.All(scenario =>
            scenario.SkipReason?.Code == "auto-greedy-bounded-wide-target-unavailable"));

        foreach (var datasetGroup in critical.Where(scenario => !scenario.IsSkipped)
                     .GroupBy(scenario => scenario.DatasetId, StringComparer.Ordinal))
        {
            var fixture = DeterministicStressGraphFixtureFactory.Create(datasetGroup.Key);
            foreach (var scenario in datasetGroup)
            {
                var operation = BenchmarkOperationRequestFactory.Create(
                    scenario,
                    fixture,
                    scenario.Parameters,
                    Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
                    Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"));
                var expectedUsed = scenario.Key.Contains(".auto-exact.", StringComparison.Ordinal) ||
                                   scenario.Key.Contains(".exact.", StringComparison.Ordinal)
                    ? OperationStrategyNames.Exact
                    : OperationStrategyNames.Greedy;
                Assert.AreEqual(expectedUsed, operation.Strategy.Used, scenario.Key);

                var targetNodeId = scenario.Parameters.GetProperty("targetNodeId").GetString()!;
                var limit = scenario.Parameters.GetProperty("candidateLimit").GetInt32();
                var candidates = CriticalCounterCandidateGuard.RequireAtMost(
                    fixture.CreateGraph(),
                    targetNodeId,
                    limit);
                Assert.IsTrue(candidates.ActualCandidateCount <= 8, scenario.Key);
                if (scenario.MeasureQualityComparison)
                {
                    Assert.IsTrue(candidates.ActualCandidateCount > 0, scenario.Key);
                }
            }
        }
    }

    [TestMethod]
    public void ColdMatrix_RunsOnlyFreshProcessBoundariesAndKeepsUnresetGraphJourneysAsSkips()
    {
        var cold = BenchmarkScenarioRegistry.ForProfile(BenchmarkProfiles.ColdKey);

        Assert.AreEqual(10, cold.Count);
        var worker = cold.Single(scenario =>
            scenario.Key == "cold.strongest.balanced-1k.isolated-worker");
        Assert.IsTrue(worker.RequiresIsolation);
        Assert.AreEqual(BenchmarkScenarioExecutionTarget.InMemory, worker.ExecutionTarget);

        var renderJourneys = cold.Where(scenario =>
            scenario.BrowserJourney?.Action == BrowserJourneyActions.ResultRender).ToArray();
        Assert.AreEqual(4, renderJourneys.Length);
        Assert.IsTrue(renderJourneys.All(scenario =>
            !scenario.IsSkipped &&
            scenario.ExecutionTarget == BenchmarkScenarioExecutionTarget.Browser));

        var skips = cold.Where(scenario => scenario.IsSkipped).ToArray();
        Assert.AreEqual(5, skips.Length);
        Assert.IsTrue(skips.All(scenario => scenario.SkipReason?.Code is
            "cold-api-postgresql-state-not-reset" or
            "cold-browser-api-postgresql-state-not-reset"));
        Assert.IsFalse(cold.Any(scenario =>
            !scenario.IsSkipped &&
            scenario.BrowserJourney?.Action is
                BrowserJourneyActions.Collapsed or
                BrowserJourneyActions.Search or
                BrowserJourneyActions.FullExpansion));
        Assert.IsFalse(cold.Any(scenario =>
            !scenario.IsSkipped &&
            scenario.ExecutionTarget is
                BenchmarkScenarioExecutionTarget.RestDatabaseLoaded or
                BenchmarkScenarioExecutionTarget.RestSuppliedGraph));
    }

    private static void AssertDatasetGroup(
        IReadOnlyList<BenchmarkScenarioDefinition> scenarios,
        string prefix,
        string suffix = "",
        bool allowSkippedSuffix = false)
    {
        var group = scenarios.Where(scenario => scenario.Key.StartsWith(prefix, StringComparison.Ordinal))
            .Where(scenario => allowSkippedSuffix || scenario.Key.EndsWith(suffix, StringComparison.Ordinal))
            .Where(scenario => !allowSkippedSuffix ||
                scenario.BrowserJourney?.Action == BrowserJourneyActions.FullExpansion)
            .ToArray();
        CollectionAssert.AreEqual(DatasetIds, group.Select(scenario => scenario.DatasetId).ToArray());
    }
}
