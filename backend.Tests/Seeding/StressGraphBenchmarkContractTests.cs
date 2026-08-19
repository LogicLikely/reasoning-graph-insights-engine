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
            BayesianMinimalCounterSetEvaluator.DefaultThresholdLogOdds,
            StressGraphBenchmarkContract.ThresholdLogOdds,
            "The seed workload threshold must track the production evaluator.");
        Assert.IsTrue(
            StressGraphBenchmarkContract.InitialTargetLogOdds >
            StressGraphBenchmarkContract.ThresholdLogOdds);
        Assert.IsTrue(
            StressGraphBenchmarkContract.CounterLeafLogBayesFactor < 0m);
        Assert.IsTrue(
            StressGraphBenchmarkContract.ProbabilityGivenParent >
            StressGraphBenchmarkContract.ProbabilityGivenNotParent);
        Assert.AreEqual(1m,
            StressGraphBenchmarkContract.ProbabilityGivenParent +
            StressGraphBenchmarkContract.ProbabilityGivenNotParent);

        var nominalAfterSeven =
            StressGraphBenchmarkContract.InitialTargetLogOdds +
            (7 * StressGraphBenchmarkContract.CounterLeafLogBayesFactor);
        var nominalAfterEight =
            StressGraphBenchmarkContract.InitialTargetLogOdds +
            (8 * StressGraphBenchmarkContract.CounterLeafLogBayesFactor);
        Assert.AreEqual(-0.92m, nominalAfterSeven);
        Assert.AreEqual(-1.08m, nominalAfterEight);
        Assert.IsTrue(
            nominalAfterSeven >
            StressGraphBenchmarkContract.ThresholdLogOdds);
        Assert.IsTrue(
            nominalAfterEight <=
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
                $"{StressGraphBenchmarkContract.CounterLeafLogBayesFactor:F3}\t" +
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
        AssertCalibratedCandidateLayout(graph, nodeCount: 100);

        var evaluator = new BayesianMinimalCounterSetEvaluator(
            new GraphPosteriorOddsCalculator());
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
        AssertApproximately(-1.08m, greedy.FinalTargetLogOdds!.Value);

        Assert.IsTrue(exhaustive.ThresholdReached);
        Assert.AreEqual(8, exhaustive.CounterNodeIds.Count);
        Assert.AreEqual(MinimalCounterSetProofStatus.Proven, exhaustive.ProofStatus);
        Assert.AreEqual(MinimalCounterSetStopReason.Completed, exhaustive.StopReason);
        Assert.AreEqual(7, exhaustive.LargestCardinalityFullyExhausted);
        Assert.AreEqual(969L, exhaustive.SubsetEvaluations);
        AssertApproximately(-1.08m, exhaustive.FinalTargetLogOdds!.Value);
    }

    [TestMethod]
    [DataRow("balanced", 1_000)]
    [DataRow("wide", 1_000)]
    [DataRow("shared-diamond", 1_000)]
    [DataRow("balanced", 10_000)]
    [DataRow("wide", 10_000)]
    [DataRow("shared-diamond", 10_000)]
    public void CalibratedLargeGraph_HasTheProductionSevenEightBoundary(
        string shape,
        int nodeCount)
    {
        var graph = CreateCalibratedGraph(shape, nodeCount);
        AssertCalibratedCandidateLayout(graph, nodeCount);

        var evaluator = new BayesianMinimalCounterSetEvaluator(
            new GraphPosteriorOddsCalculator());
        var problem = evaluator.CreateProblem(
            graph,
            "n-00000",
            graph.Nodes.Select(node => node.Id));
        var orderedCandidateIds = problem.Candidates
            .Select(candidate => candidate.NodeId)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.AreEqual(nodeCount / 10, orderedCandidateIds.Length);
        Assert.AreEqual(
            StressGraphBenchmarkContract.InitialTargetLogOdds,
            problem.InitialTargetLogOdds);

        var afterSeven = problem.CalculateTargetLogOdds(
            orderedCandidateIds.Take(7).ToArray());
        var afterEight = problem.CalculateTargetLogOdds(
            orderedCandidateIds.Take(8).ToArray());

        Assert.IsTrue(
            afterSeven > problem.ThresholdLogOdds,
            $"Expected seven counters to remain above the threshold, but got {afterSeven}.");
        Assert.IsTrue(
            afterEight <= problem.ThresholdLogOdds,
            $"Expected eight counters to reach the threshold, but got {afterEight}.");
        AssertApproximately(-0.92m, afterSeven);
        AssertApproximately(-1.08m, afterEight);
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
                    Kind = Kind(index, nodeCount),
                    PriorOdds = index == 0
                        ? StressGraphBenchmarkContract.InitialTargetLogOdds
                        : 0m,
                    PosteriorOdds = index switch
                    {
                        0 => StressGraphBenchmarkContract.InitialTargetLogOdds,
                        _ when Kind(index, nodeCount) == "objection" =>
                            StressGraphBenchmarkContract.CounterLeafLogBayesFactor,
                        _ => 0m
                    }
                })
                .ToList(),
            Edges = CreateEdges(shape, nodeCount)
        };

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
        Kind = "support",
        ProbabilityGivenParent =
            StressGraphBenchmarkContract.ProbabilityGivenParent,
        ProbabilityGivenNotParent =
            StressGraphBenchmarkContract.ProbabilityGivenNotParent
    };

    private static string NodeId(int index) => $"n-{index:00000}";

    private static string Kind(int index, int nodeCount) => index switch
    {
        0 => "root",
        _ when index >= nodeCount - (nodeCount / 10) => "objection",
        _ when IsReplacementEvidence(index, nodeCount) => "evidence",
        _ when index % 5 == 0 => "evidence",
        _ => "claim"
    };

    private static bool IsReplacementEvidence(int index, int nodeCount)
    {
        var objectionCount = nodeCount / 10;
        var objectionStart = nodeCount - objectionCount;
        var replacementWindowStart = objectionStart - objectionCount;

        return index >= replacementWindowStart &&
            index < objectionStart &&
            index % 5 == 1;
    }

    private static void AssertCalibratedCandidateLayout(Graph graph, int nodeCount)
    {
        var objectionCount = nodeCount / 10;
        var expectedObjectionIds = Enumerable
            .Range(nodeCount - objectionCount, objectionCount)
            .Select(NodeId)
            .ToArray();
        var actualObjectionIds = graph.Nodes
            .Where(node => node.Kind == "objection")
            .Select(node => node.Id)
            .ToArray();

        CollectionAssert.AreEqual(expectedObjectionIds, actualObjectionIds);
        foreach (var objectionId in actualObjectionIds)
        {
            Assert.IsFalse(
                graph.Edges.Any(edge => edge.To == objectionId),
                $"Counter candidate '{objectionId}' must be a structural leaf.");
        }

        Assert.AreEqual(
            (nodeCount - 1) / 5,
            graph.Nodes.Count(node => node.Kind == "evidence"));
        Assert.AreEqual(
            nodeCount - 1 - ((nodeCount - 1) / 5) - objectionCount,
            graph.Nodes.Count(node => node.Kind == "claim"));
    }

    private static void AssertApproximately(decimal expected, decimal actual)
    {
        Assert.AreEqual(expected, actual, 0.000001m);
    }
}
