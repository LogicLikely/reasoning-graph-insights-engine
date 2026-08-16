using Backend.Insights.Analysis;
using Backend.Insights.Contracts;
using Backend.Models.Domain;

namespace Backend.Tests.Insights.Analysis;

[TestClass]
public sealed class CriticalCounterV1AnalysisTests
{
    private readonly CriticalCounterV1Analyzer _analyzer = new();

    [TestMethod]
    public void Exact_ProvesOptimalCardinalityOnASynergisticNestedCandidateSet()
    {
        var graph = GraphWith(
            [
                Node("target", priorLogOdds: 1m),
                Node("counter-a", kind: "objection"),
                Node("counter-b", kind: "objection")
            ],
            [
                Edge("edge-a-b", "counter-a", "counter-b", 0.01m),
                Edge("edge-b-target", "counter-b", "target", 0.5m)
            ]);

        var result = Analyze(graph, OperationStrategyNames.Exact);

        Assert.IsTrue(result.ThresholdAttained);
        CollectionAssert.AreEqual(
            new[] { "counter-a", "counter-b" },
            result.SelectedNodeIds.ToArray());
        Assert.IsTrue(result.OptimalCardinalityProven);
        Assert.IsFalse(result.ProvedUnattainable);
        Assert.IsTrue(result.SearchExhausted,
            "Finding the winner at the final cardinality also exhausts this two-candidate search.");
        Assert.AreEqual(4L, result.EvaluationCount);
        Assert.IsTrue(result.ResultingLogOdds <= CriticalCounterV1Contract.DefaultThresholdLogOdds);
        Assert.AreEqual(
            "sha256:25faf12463764574c1a3d7d17ba73f822e3b10789d38dc19030ad57c01214439",
            result.ResultDigest);
    }

    [TestMethod]
    public void Exact_EvaluatesAFullCardinalityBeforeChoosingTheGreaterMargin()
    {
        var graph = GraphWith(
            [
                Node("target"),
                Node("a-narrow", kind: "objection"),
                Node("z-wide", kind: "objection")
            ],
            [
                Edge("edge-a", "a-narrow", "target", 0.2m),
                Edge("edge-z", "z-wide", "target", 0.1m)
            ]);

        var result = Analyze(graph, OperationStrategyNames.Exact);

        CollectionAssert.AreEqual(new[] { "z-wide" }, result.SelectedNodeIds.ToArray());
        Assert.AreEqual(3L, result.EvaluationCount,
            "Baseline and both singleton subsets must be evaluated before choosing the wider margin.");
        Assert.IsTrue(result.OptimalCardinalityProven);
    }

    [TestMethod]
    public void Exact_EqualMarginTieUsesOrdinalIdsAndIgnoresInputInsertionOrder()
    {
        var ordered = GraphWith(
            [Node("target"), Node("a", kind: "objection"), Node("z", kind: "objection")],
            [Edge("edge-a", "a", "target", 0.2m), Edge("edge-z", "z", "target", 0.2m)]);
        var permuted = GraphWith(
            [Node("z", kind: "objection"), Node("target"), Node("a", kind: "objection")],
            [Edge("edge-z", "z", "target", 0.2m), Edge("edge-a", "a", "target", 0.2m)]);

        var orderedResult = Analyze(ordered, OperationStrategyNames.Exact);
        var permutedResult = Analyze(permuted, OperationStrategyNames.Exact);

        CollectionAssert.AreEqual(new[] { "a" }, orderedResult.SelectedNodeIds.ToArray());
        CollectionAssert.AreEqual(
            orderedResult.SelectedNodeIds.ToArray(),
            permutedResult.SelectedNodeIds.ToArray());
        Assert.AreEqual(orderedResult.ResultDigest, permutedResult.ResultDigest);
        Assert.AreEqual(
            CanonicalJson.Canonicalize(orderedResult.DeterministicTopItem),
            CanonicalJson.Canonicalize(permutedResult.DeterministicTopItem));
    }

    [TestMethod]
    public void Exact_ExhaustiveNonAttainmentReturnsEmptyBaselineAndDistinctProofFlags()
    {
        var graph = GraphWith(
            [
                Node("target"),
                Node("improves-but-not-enough", kind: "objection"),
                Node("worsens", kind: "objection")
            ],
            [
                Edge("edge-improves", "improves-but-not-enough", "target", 0.8m),
                Edge("edge-worsens", "worsens", "target", 1.2m)
            ]);

        var result = Analyze(graph, OperationStrategyNames.Exact);

        Assert.IsFalse(result.ThresholdAttained);
        Assert.AreEqual(0, result.SelectedNodeIds.Count);
        Assert.IsTrue(result.SearchExhausted);
        Assert.IsTrue(result.ProvedUnattainable);
        Assert.IsFalse(result.OptimalCardinalityProven);
        Assert.AreEqual(4L, result.EvaluationCount);
        Assert.AreEqual(result.BaselineLogOdds, result.ResultingLogOdds);
    }

    [TestMethod]
    public void Exact_AlreadyAttainingBaselineReturnsEmptyOptimalSet()
    {
        var graph = GraphWith(
            [Node("target", priorLogOdds: -1m), Node("counter", kind: "objection")],
            [Edge("edge-counter", "counter", "target", 0.1m)]);

        var result = Analyze(graph, OperationStrategyNames.Exact);

        Assert.IsTrue(result.ThresholdAttained);
        Assert.AreEqual(0, result.SelectedNodeIds.Count);
        Assert.IsTrue(result.OptimalCardinalityProven);
        Assert.IsFalse(result.ProvedUnattainable);
        Assert.IsFalse(result.SearchExhausted);
        Assert.AreEqual(1L, result.EvaluationCount);
    }

    [TestMethod]
    public void Greedy_RecomputesMarginalsAndCanHaveAStableGapFromExact()
    {
        var graph = GreedyGapGraph();

        var exact = Analyze(graph, OperationStrategyNames.Exact);
        var greedy = Analyze(graph, OperationStrategyNames.Greedy);
        var comparison = _analyzer.CompareExactAndGreedy(
            graph,
            "target",
            CriticalCounterV1Contract.DefaultThresholdLogOdds);

        CollectionAssert.AreEqual(
            new[] { "counter-a", "counter-b" },
            exact.SelectedNodeIds.ToArray());
        CollectionAssert.AreEqual(
            new[] { "counter-a", "counter-b", "counter-c" },
            greedy.SelectedNodeIds.ToArray());
        Assert.AreEqual(7L, greedy.EvaluationCount,
            "Greedy must evaluate 3, then 2, then 1 remaining marginals after its baseline.");
        Assert.AreEqual(1, comparison.CardinalityGapFromOptimal);
        Assert.AreEqual(2, comparison.SelectedSetOverlapCount);
        Assert.AreEqual(3, comparison.SelectedSetUnionCount);
        Assert.AreEqual(0.666666666667m, comparison.SelectedSetJaccardSimilarity);
        Assert.AreEqual(exact.ResultDigest, comparison.ExactResultDigest);
        Assert.AreEqual(greedy.ResultDigest, comparison.GreedyResultDigest);
        Assert.AreEqual(exact.EvaluationCount, comparison.ExactEvaluationCount);
        Assert.AreEqual(greedy.EvaluationCount, comparison.GreedyEvaluationCount);
    }

    [TestMethod]
    public void Greedy_StopsAndRetainsOnlyStrictImprovements()
    {
        var graph = GraphWith(
            [Node("target"), Node("worsens", kind: "objection")],
            [Edge("edge-worsens", "worsens", "target", 2m)]);

        var result = Analyze(graph, OperationStrategyNames.Greedy);

        Assert.IsFalse(result.ThresholdAttained);
        Assert.AreEqual(0, result.SelectedNodeIds.Count);
        Assert.AreEqual(result.BaselineLogOdds, result.ResultingLogOdds);
        Assert.AreEqual(2L, result.EvaluationCount);
        Assert.IsFalse(result.SearchExhausted);
        Assert.IsFalse(result.ProvedUnattainable);
        Assert.IsFalse(result.OptimalCardinalityProven);
    }

    [TestMethod]
    public void QualityComparison_LeavesGapNullWhenGreedyDoesNotAttainAndBothEmptyOverlapIsOne()
    {
        var graph = GraphWith(
            [Node("target"), Node("worsens", kind: "objection")],
            [Edge("edge-worsens", "worsens", "target", 2m)]);

        var comparison = _analyzer.CompareExactAndGreedy(
            graph,
            "target",
            CriticalCounterV1Contract.DefaultThresholdLogOdds);

        Assert.IsFalse(comparison.ExactThresholdAttained);
        Assert.IsFalse(comparison.GreedyThresholdAttained);
        Assert.IsNull(comparison.CardinalityGapFromOptimal);
        Assert.AreEqual(0, comparison.SelectedSetOverlapCount);
        Assert.AreEqual(0, comparison.SelectedSetUnionCount);
        Assert.AreEqual(1m, comparison.SelectedSetJaccardSimilarity);
    }

    [TestMethod]
    public void Auto_UsesExactAtCutoffAndGreedyAboveCutoffWithStableReasons()
    {
        var graph = GreedyGapGraph();

        var atCutoff = Analyze(
            graph,
            OperationStrategyNames.Auto,
            autoCandidateCutoff: 3);
        var aboveCutoff = Analyze(
            graph,
            OperationStrategyNames.Auto,
            autoCandidateCutoff: 2);

        Assert.AreEqual(OperationStrategyNames.Auto, atCutoff.RequestedStrategy);
        Assert.AreEqual(OperationStrategyNames.Exact, atCutoff.UsedStrategy);
        Assert.AreEqual(3, atCutoff.CandidateCount);
        Assert.AreEqual(3, atCutoff.AutoCandidateCutoff);
        Assert.AreEqual(CriticalCounterV1Analyzer.AutoExactReason, atCutoff.StrategySelectionReason);
        CollectionAssert.AreEqual(
            new[] { "counter-a", "counter-b" },
            atCutoff.SelectedNodeIds.ToArray());

        Assert.AreEqual(OperationStrategyNames.Greedy, aboveCutoff.UsedStrategy);
        Assert.AreEqual(2, aboveCutoff.AutoCandidateCutoff);
        Assert.AreEqual(CriticalCounterV1Analyzer.AutoGreedyReason, aboveCutoff.StrategySelectionReason);
        CollectionAssert.AreEqual(
            new[] { "counter-a", "counter-b", "counter-c" },
            aboveCutoff.SelectedNodeIds.ToArray());
    }

    [TestMethod]
    public void Auto_RequiresExplicitNonnegativeCutoff()
    {
        var graph = GraphWith([Node("target")], []);

        Assert.ThrowsException<ArgumentException>(() => Analyze(
            graph,
            OperationStrategyNames.Auto,
            autoCandidateCutoff: null));
        Assert.ThrowsException<ArgumentOutOfRangeException>(() => Analyze(
            graph,
            OperationStrategyNames.Auto,
            autoCandidateCutoff: -1));
    }

    [TestMethod]
    public void CounterAlias_RemainsIgnoredAsAnEvidenceContributionButCanSupplyStructuralContext()
    {
        var graph = GraphWith(
            [
                Node("target", priorLogOdds: 1m),
                Node("a-objection", kind: "objection"),
                Node("b-counter-alias", kind: "counter")
            ],
            [
                Edge("edge-a-b", "a-objection", "b-counter-alias", 0.01m),
                Edge("edge-b-target", "b-counter-alias", "target", 0.5m)
            ]);

        var result = Analyze(graph, OperationStrategyNames.Exact);

        CollectionAssert.AreEqual(
            new[] { "a-objection", "b-counter-alias" },
            result.SelectedNodeIds.ToArray());
        var objection = result.SelectedCounters.Single(item => item.NodeId == "a-objection");
        var alias = result.SelectedCounters.Single(item => item.NodeId == "b-counter-alias");
        Assert.IsTrue(objection.RecognizedByLikelihoodRecalculationV0);
        Assert.IsNotNull(objection.ResponsiblePath);
        CollectionAssert.AreEqual(
            new[] { "a-objection", "b-counter-alias", "target" },
            objection.ResponsiblePath.NodeIds.ToArray());
        Assert.IsFalse(alias.RecognizedByLikelihoodRecalculationV0);
        Assert.IsNull(alias.ResponsiblePath,
            "The frozen likelihood-recalculate-v0 implementation does not treat kind 'counter' as evidence.");
        Assert.AreEqual(1, result.ResponsibleSelectedPaths.Count);
    }

    [TestMethod]
    public void ResponsiblePath_UsesDeterministicOrdinalTieBreakingAcrossPermutations()
    {
        var ordered = GraphWith(
            [
                Node("target"),
                Node("a-branch"),
                Node("z-branch"),
                Node("counter", kind: "objection")
            ],
            [
                Edge("edge-counter-a", "counter", "a-branch", 0.5m),
                Edge("edge-a-target", "a-branch", "target", 0.2m),
                Edge("edge-counter-z", "counter", "z-branch", 0.5m),
                Edge("edge-z-target", "z-branch", "target", 0.2m)
            ]);
        var permuted = GraphWith(
            [
                Node("z-branch"),
                Node("counter", kind: "objection"),
                Node("target"),
                Node("a-branch")
            ],
            [
                Edge("edge-z-target", "z-branch", "target", 0.2m),
                Edge("edge-counter-z", "counter", "z-branch", 0.5m),
                Edge("edge-a-target", "a-branch", "target", 0.2m),
                Edge("edge-counter-a", "counter", "a-branch", 0.5m)
            ]);

        var orderedResult = Analyze(ordered, OperationStrategyNames.Exact);
        var permutedResult = Analyze(permuted, OperationStrategyNames.Exact);
        var orderedPath = orderedResult.SelectedCounters.Single().ResponsiblePath;
        var permutedPath = permutedResult.SelectedCounters.Single().ResponsiblePath;

        Assert.IsNotNull(orderedPath);
        Assert.IsNotNull(permutedPath);
        CollectionAssert.AreEqual(
            new[] { "counter", "a-branch", "target" },
            orderedPath.NodeIds.ToArray());
        CollectionAssert.AreEqual(
            orderedPath.NodeIds.ToArray(),
            permutedPath.NodeIds.ToArray());
        CollectionAssert.AreEqual(
            orderedPath.EdgeIds.ToArray(),
            permutedPath.EdgeIds.ToArray());
        Assert.AreEqual(orderedResult.ResultDigest, permutedResult.ResultDigest);
    }

    [TestMethod]
    public void DirectCounterAlias_IsEligibleButCannotImproveTheFrozenLikelihoodResult()
    {
        var graph = GraphWith(
            [Node("target"), Node("alias", kind: "counter")],
            [Edge("edge-alias", "alias", "target", 0.01m)]);

        var exact = Analyze(graph, OperationStrategyNames.Exact);
        var greedy = Analyze(graph, OperationStrategyNames.Greedy);

        Assert.AreEqual(1, exact.CandidateCount);
        Assert.IsTrue(exact.ProvedUnattainable);
        Assert.AreEqual(0, exact.SelectedNodeIds.Count);
        Assert.AreEqual(0, greedy.SelectedNodeIds.Count);
        Assert.AreEqual(exact.BaselineLogOdds, greedy.ResultingLogOdds);
    }

    [TestMethod]
    public void Result_HasOneCanonicalTopItemAndDigestForAnEmptySelection()
    {
        var graph = GraphWith([Node("target")], []);

        var result = Analyze(graph, OperationStrategyNames.Exact);

        Assert.AreEqual(OperationKeys.CounterCriticalSet, result.OperationKey);
        Assert.AreEqual(AlgorithmSemanticIdentities.CriticalCounterV1, result.AlgorithmSemanticIdentity);
        Assert.AreEqual(1L, result.TotalResultCardinality);
        Assert.AreEqual(1, result.Items.Count);
        Assert.AreSame(result.DeterministicTopItem, result.Items[0]);
        Assert.AreEqual(CanonicalJson.ComputeSha256(result.Items), result.ResultDigest);
        Assert.AreEqual(
            CanonicalResultNumber.Normalize(result.BaselineLogOdds),
            result.DeterministicTopItem.BaselineLogOdds);
        Assert.AreEqual(
            CanonicalResultNumber.Normalize(result.ResultingLogOdds),
            result.DeterministicTopItem.ResultingLogOdds);
        Assert.AreEqual(
            CanonicalResultNumber.Normalize(result.BelowThresholdMargin),
            result.DeterministicTopItem.BelowThresholdMargin);
        Assert.AreEqual(0, result.DeterministicTopItem.SelectedNodeIds.Count);
    }

    [TestMethod]
    public void Analyze_DoesNotMutateTheInputGraphAcrossSubsetEvaluations()
    {
        var graph = GreedyGapGraph();
        var before = CanonicalJson.Canonicalize(graph);

        _ = Analyze(graph, OperationStrategyNames.Exact);
        _ = Analyze(graph, OperationStrategyNames.Greedy);

        Assert.AreEqual(before, CanonicalJson.Canonicalize(graph));
    }

    [TestMethod]
    public void Analyze_ThrowsImmediatelyForAPreCancelledRequest()
    {
        var graph = GreedyGapGraph();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.ThrowsException<OperationCanceledException>(() => _analyzer.Analyze(
            Request(graph, OperationStrategyNames.Exact),
            cancellation.Token));
    }

    [TestMethod]
    [Timeout(5_000)]
    public void Exact_ObservesCancellationDuringACombinatorialSearch()
    {
        var nodes = new List<GraphNode> { Node("target") };
        var edges = new List<GraphEdge>();
        for (var index = 0; index < 24; index++)
        {
            var nodeId = $"counter-{index:D2}";
            nodes.Add(Node(nodeId, kind: "objection"));
            edges.Add(Edge($"edge-{index:D2}", nodeId, "target", 0.99m));
        }

        var graph = GraphWith(nodes, edges);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(20));

        Assert.ThrowsException<OperationCanceledException>(() => _analyzer.Analyze(
            Request(graph, OperationStrategyNames.Exact),
            cancellation.Token));
    }

    [TestMethod]
    [DoNotParallelize]
    public void Analyze_ProducesNoConsoleOutput()
    {
        var graph = GreedyGapGraph();
        var originalOut = Console.Out;
        using var captured = new StringWriter();
        try
        {
            Console.SetOut(captured);
            _ = Analyze(graph, OperationStrategyNames.Greedy);
        }
        finally
        {
            Console.SetOut(originalOut);
        }

        Assert.AreEqual(string.Empty, captured.ToString());
    }

    private CriticalCounterV1AnalysisResult Analyze(
        Graph graph,
        string strategy,
        decimal thresholdLogOdds = CriticalCounterV1Contract.DefaultThresholdLogOdds,
        int? autoCandidateCutoff = null)
    {
        return _analyzer.Analyze(new CriticalCounterV1AnalysisRequest(
            graph,
            "target",
            strategy,
            thresholdLogOdds,
            autoCandidateCutoff));
    }

    private static CriticalCounterV1AnalysisRequest Request(Graph graph, string strategy)
    {
        return new CriticalCounterV1AnalysisRequest(
            graph,
            "target",
            strategy,
            CriticalCounterV1Contract.DefaultThresholdLogOdds,
            null);
    }

    private static Graph GreedyGapGraph()
    {
        return GraphWith(
            [
                Node("target", priorLogOdds: 2m),
                Node("counter-a", kind: "objection"),
                Node("counter-b", kind: "objection"),
                Node("counter-c", kind: "objection")
            ],
            [
                Edge("edge-a-b", "counter-a", "counter-b", 0.1m),
                Edge("edge-b-target", "counter-b", "target", 0.5m),
                Edge("edge-c-target", "counter-c", "target", 0.2m)
            ]);
    }

    private static Graph GraphWith(
        IEnumerable<GraphNode> nodes,
        IEnumerable<GraphEdge> edges)
    {
        return new Graph
        {
            Id = 42,
            Slug = "critical-counter-v1-analysis",
            Title = "Critical counter v1 analysis fixture",
            Nodes = nodes.ToList(),
            Edges = edges.ToList()
        };
    }

    private static GraphNode Node(
        string id,
        string kind = "claim",
        decimal priorLogOdds = 0m)
    {
        return new GraphNode
        {
            Id = id,
            Kind = kind,
            Title = id,
            BodyText = id,
            PriorOdds = priorLogOdds,
            PosteriorOdds = priorLogOdds
        };
    }

    private static GraphEdge Edge(
        string id,
        string from,
        string to,
        decimal likelihoodRatio)
    {
        return new GraphEdge
        {
            Id = id,
            From = from,
            To = to,
            Kind = likelihoodRatio < 1m ? "rebut" : "support",
            ImportanceToParent = likelihoodRatio
        };
    }
}
