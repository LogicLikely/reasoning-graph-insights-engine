using Backend.Calculation;
using Backend.Calculation.MinimalCounterSets;
using Backend.Models.Domain;
using Backend.Seeding;

namespace backend.Tests.Seeding;

[TestClass]
public sealed class StressGraphBenchmarkContractTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public void Contract_HasAWellSeparatedMinimumOfEightCounters()
    {
        Assert.AreEqual(
            LegacyMinimalCounterSetEvaluator.DefaultThresholdLogOdds,
            StressGraphBenchmarkContract.ThresholdLogOdds,
            "The seed workload threshold must track the production evaluator.");
        Assert.AreEqual(
            -0.92m,
            StressGraphBenchmarkContract.TargetLogOddsAfterCounters(7));
        Assert.AreEqual(
            -1.08m,
            StressGraphBenchmarkContract.TargetLogOddsAfterCounters(8));
        Assert.IsTrue(
            StressGraphBenchmarkContract.TargetLogOddsAfterCounters(7) >
            StressGraphBenchmarkContract.ThresholdLogOdds);
        Assert.IsTrue(
            StressGraphBenchmarkContract.TargetLogOddsAfterCounters(8) <=
            StressGraphBenchmarkContract.ThresholdLogOdds);
        Assert.AreEqual(
            8,
            StressGraphBenchmarkContract.ExpectedMinimumCounterSetCardinality);
        Assert.AreEqual(
            "969",
            StressGraphBenchmarkContract
                .ExpectedExhaustiveEvaluationsToFirstMinimum(10)
                .ToString());
        Assert.IsTrue(
            StressGraphBenchmarkContract
                .ExpectedExhaustiveEvaluationsToFirstMinimum(100) >
            10_000_000_000L);
    }

    [TestMethod]
    public void Report_AllStandardGraphsShareTheCheckedInWorkloadContract()
    {
        TestContext.WriteLine(
            "graph\tnodes\tcandidates\tinitial\teach counter\tminimum\texhaustive evaluations");

        var standardSpecs = StressGraphSeedCatalog.All
            .Where(spec => !string.Equals(spec.Shape, "deep", StringComparison.Ordinal))
            .ToArray();

        Assert.AreEqual(12, standardSpecs.Length);
        foreach (var spec in standardSpecs)
        {
            TestContext.WriteLine(
                $"{spec.Id}\t{spec.NodeCount}\t{spec.ObjectionCount}\t" +
                $"{StressGraphBenchmarkContract.InitialTargetLogOdds:F3}\t" +
                $"{StressGraphBenchmarkContract.EffectiveCounterContributionLogOdds:F3}\t" +
                $"{StressGraphBenchmarkContract.ExpectedMinimumCounterSetCardinality}\t" +
                StressGraphBenchmarkContract
                    .ExpectedExhaustiveEvaluationsToFirstMinimum(spec.ObjectionCount));

            Assert.IsTrue(
                spec.ObjectionCount >=
                StressGraphBenchmarkContract.ExpectedMinimumCounterSetCardinality);
        }
    }

    [TestMethod]
    [DataRow("balanced")]
    [DataRow("wide")]
    [DataRow("shared-diamond")]
    public void CalibratedHundredNodeGraph_GreedyFindsAndExhaustiveProvesEight(
        string shape)
    {
        var graph = CreateCalibratedGraph(shape, nodeCount: 100);
        var calculator = new GraphLikelihoodCalculator();
        var evaluator = new LegacyMinimalCounterSetEvaluator(calculator);
        var nodeIds = graph.Nodes.Select(node => node.Id).ToArray();

        var greedy = new GreedyMinimalCounterSetSolver(evaluator).Solve(
            graph,
            "n-00000",
            nodeIds);
        var exhaustive = new BoundedBruteForceMinimalCounterSetSolver(evaluator).Solve(
            graph,
            "n-00000",
            nodeIds);

        Assert.AreEqual(10, greedy.TotalCandidateCount);
        Assert.IsTrue(greedy.ThresholdReached);
        Assert.AreEqual(8, greedy.CounterNodeIds.Count);
        Assert.AreEqual(StressGraphBenchmarkContract.InitialTargetLogOdds, greedy.InitialTargetLogOdds);
        Assert.AreEqual(
            StressGraphBenchmarkContract.TargetLogOddsAfterCounters(8),
            greedy.FinalTargetLogOdds);

        Assert.IsTrue(exhaustive.ThresholdReached);
        Assert.AreEqual(8, exhaustive.CounterNodeIds.Count);
        Assert.AreEqual(MinimalCounterSetProofStatus.Proven, exhaustive.ProofStatus);
        Assert.AreEqual(MinimalCounterSetStopReason.Completed, exhaustive.StopReason);
        Assert.AreEqual(7, exhaustive.LargestCardinalityFullyExhausted);
        Assert.AreEqual(969L, exhaustive.SubsetEvaluations);
        Assert.AreEqual(
            StressGraphBenchmarkContract.TargetLogOddsAfterCounters(8),
            exhaustive.FinalTargetLogOdds);
    }

    private static Graph CreateCalibratedGraph(string shape, int nodeCount)
    {
        var graph = new Graph
        {
            Slug = $"stress-{shape}-{nodeCount}",
            Nodes = Enumerable.Range(0, nodeCount)
                .Select(index => new GraphNode
                {
                    Id = NodeId(index),
                    Kind = Kind(index),
                    PriorOdds = EvidencePrior(index),
                    PosteriorOdds = EvidencePrior(index)
                })
                .ToList(),
            Edges = CreateEdges(shape, nodeCount)
        };

        var calculator = new GraphLikelihoodCalculator();
        var uncalibratedContext = GraphCalculationContext.From(graph.Nodes, graph.Edges);
        var root = graph.Nodes[0];
        var rootPathContribution = calculator.CalculateNodeLogPosteriorOdds(
            uncalibratedContext,
            root.Id);
        root.PriorOdds =
            StressGraphBenchmarkContract.InitialTargetLogOdds - rootPathContribution;

        foreach (var objection in graph.Nodes.Where(node => node.Kind == "objection"))
        {
            var downstreamPathContribution = calculator.CalculateNodeLogPosteriorOdds(
                uncalibratedContext,
                objection.Id);
            var accumulatedLikelihoodRatio = calculator.GetSingleAccumulatedLR(
                uncalibratedContext,
                objection.Id,
                root.Id);
            Assert.IsNotNull(accumulatedLikelihoodRatio);
            var pathToRoot = (decimal)Math.Log((double)accumulatedLikelihoodRatio.Value);

            objection.PriorOdds =
                StressGraphBenchmarkContract.EffectiveCounterContributionLogOdds -
                downstreamPathContribution -
                pathToRoot;
        }

        var calibratedContext = GraphCalculationContext.From(graph.Nodes, graph.Edges);
        foreach (var node in graph.Nodes)
        {
            node.PosteriorOdds = calculator.CalculateNodeLogPosteriorOdds(
                calibratedContext,
                node.Id);
        }

        return graph;
    }

    private static List<GraphEdge> CreateEdges(string shape, int nodeCount)
    {
        var edges = new List<GraphEdge>();
        for (var index = 1; index < nodeCount; index++)
        {
            var primaryParent = shape switch
            {
                "balanced" or "shared-diamond" => (index - 1) / 4,
                "wide" => 0,
                _ => throw new ArgumentOutOfRangeException(nameof(shape), shape, null)
            };
            edges.Add(Edge($"e-p-{index:00000}", index, primaryParent));

            if (shape == "shared-diamond" && index >= 5)
            {
                var firstSibling = (4 * ((primaryParent - 1) / 4)) + 1;
                var alternateParent = firstSibling +
                    ((primaryParent - firstSibling + 1) % 4);
                edges.Add(Edge($"e-a-{index:00000}", index, alternateParent));
            }
        }

        return edges;
    }

    private static GraphEdge Edge(string id, int childIndex, int parentIndex) => new()
    {
        Id = id,
        From = NodeId(childIndex),
        To = NodeId(parentIndex),
        Kind = childIndex % 2 == 1 ? "support" : "rebut",
        ImportanceToParent = childIndex % 2 == 1 ? 1.001m : 0.999m
    };

    private static string NodeId(int index) => $"n-{index:00000}";

    private static string Kind(int index) => index switch
    {
        0 => "root",
        _ when index % 5 == 0 => "evidence",
        _ when index % 10 == 2 => "objection",
        _ => "claim"
    };

    private static decimal EvidencePrior(int index)
    {
        if (Kind(index) != "evidence")
        {
            return 0m;
        }

        var score = 35 + (5 * ((index / 5) % 7));
        return (decimal)Math.Log(score / (double)(100 - score));
    }
}
