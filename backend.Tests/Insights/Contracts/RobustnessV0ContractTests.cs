using Backend.Insights.Contracts;
using Backend.Models.Domain;

namespace Backend.Tests.Insights.Contracts;

[TestClass]
public class RobustnessV0ContractTests
{
    [TestMethod]
    public void Contract_DisclosesFrozenV0SemanticsAndRange()
    {
        Assert.AreEqual("robustness-v0", RobustnessV0Contract.SemanticVersion);
        Assert.IsTrue(RobustnessV0Contract.RanksAllNodeKinds);
        Assert.IsTrue(RobustnessV0Contract.AllowsAllStructuralLeafKinds);
        Assert.IsTrue(RobustnessV0Contract.IncludesAllEdgeKinds);
        Assert.IsFalse(RobustnessV0Contract.IncludesLeafEvidenceContribution);
        Assert.IsTrue(RobustnessV0Contract.UsesStoredPosteriorLogOdds);
        Assert.IsTrue(RobustnessV0Contract.RequiresDirectedAcyclicGraph);
        Assert.IsTrue(RobustnessV0Contract.CurrentImplementationUsesRecursion);
        Assert.IsTrue(RobustnessV0Contract.CurrentImplementationHasDeepGraphStackRisk);
        AssertApproximately((decimal)Math.Exp(-1d), RobustnessV0Contract.TheoreticalMinimumScore);
        Assert.AreEqual(1m, RobustnessV0Contract.TheoreticalMaximumScore);
    }

    [TestMethod]
    public void SupportOnlyVector_UsesAccumulatedEdgeWeights()
    {
        var pathLogLr = RobustnessV0Contract.AccumulateEdgeLogLikelihoodRatio(
            [
                Edge("support-1", "leaf", "branch", "support", 2m),
                Edge("support-2", "branch", "target", "support", 3m)
            ]);

        var vector = RobustnessV0Contract.Evaluate(0.4m, pathLogLr);

        AssertApproximately((decimal)Math.Log(6d), vector.AccumulatedPathLogLikelihoodRatio);
        Assert.IsNotNull(vector.AccumulatedPathLikelihoodRatio);
        AssertApproximately(6m, vector.AccumulatedPathLikelihoodRatio.Value);
        AssertVectorFormula(vector);
    }

    [TestMethod]
    public void CounterOnlyVector_IncludesRebutEdgeWithoutInvertingItsWeight()
    {
        var pathLogLr = RobustnessV0Contract.AccumulateEdgeLogLikelihoodRatio(
            [Edge("counter", "leaf", "target", "rebut", 0.25m)]);

        var vector = RobustnessV0Contract.Evaluate(-0.2m, pathLogLr);

        AssertApproximately((decimal)Math.Log(0.25d), vector.AccumulatedPathLogLikelihoodRatio);
        Assert.IsTrue(vector.HypotheticalProbability > vector.OriginalProbability);
        AssertVectorFormula(vector);
    }

    [TestMethod]
    public void MixedPathVector_IncludesEveryEdgeKindAndSelectsMaximumLogPathNotMaximumMagnitude()
    {
        var mixedPath = RobustnessV0Contract.AccumulateEdgeLogLikelihoodRatio(
            [
                Edge("support", "leaf", "branch", "support", 4m),
                Edge("rebut", "branch", "target", "rebut", 0.5m),
                Edge("custom", "target", "root", "custom", 1.5m)
            ]);
        var counterPath = RobustnessV0Contract.AccumulateEdgeLogLikelihoodRatio(
            [Edge("strong-counter", "other", "root", "rebut", 0.01m)]);

        var selectedPath = RobustnessV0Contract.SelectMaximumAccumulatedPathLogLikelihoodRatio(
            [counterPath, mixedPath]);
        var vector = RobustnessV0Contract.Evaluate(0.1m, selectedPath);

        AssertApproximately((decimal)Math.Log(3d), selectedPath);
        Assert.IsNotNull(vector.AccumulatedPathLikelihoodRatio);
        AssertApproximately(3m, vector.AccumulatedPathLikelihoodRatio.Value);
        AssertVectorFormula(vector);
    }

    [TestMethod]
    public void StoredPosteriorVector_DoesNotUsePriorOrRecalculateTheGraph()
    {
        var node = new GraphNode
        {
            Id = "target",
            Kind = "claim",
            PriorOdds = -20m,
            PosteriorOdds = 0.4m
        };
        var pathLogLr = (decimal)Math.Log(2d);

        var fromNode = RobustnessV0Contract.Evaluate(node, pathLogLr);
        var fromStoredPosterior = RobustnessV0Contract.Evaluate(0.4m, pathLogLr);
        var fromPrior = RobustnessV0Contract.Evaluate(-20m, pathLogLr);

        Assert.AreEqual(0.4m, fromNode.StoredPosteriorLogOdds);
        AssertApproximately(fromStoredPosterior.RobustnessScore, fromNode.RobustnessScore);
        Assert.AreNotEqual(fromPrior.RobustnessScore, fromNode.RobustnessScore);
    }

    [TestMethod]
    public void LeafVector_OmitsLeafEvidenceContributionAndReturnsOne()
    {
        var evidenceLeaf = new GraphNode
        {
            Id = "evidence",
            Kind = "evidence",
            PriorOdds = 8m,
            PosteriorOdds = 8m
        };

        var vector = RobustnessV0Contract.Evaluate(evidenceLeaf, 0m);

        Assert.AreEqual(1m, vector.RobustnessScore);
        Assert.AreEqual(0d, vector.AbsoluteProbabilityDelta);
    }

    [TestMethod]
    public void Vector_KeepsScoreAndLogLrWhenLikelihoodRatioExceedsDecimalRange()
    {
        var vector = RobustnessV0Contract.Evaluate(0.4m, 100m);

        Assert.IsNull(vector.AccumulatedPathLikelihoodRatio);
        Assert.AreEqual(100m, vector.AccumulatedPathLogLikelihoodRatio);
        AssertVectorFormula(vector);
    }

    [TestMethod]
    public void ReportedPath_UsesMaximumScoreThenOrdinalNodeAndEdgeSequences()
    {
        var nodeTieB = new RobustnessV0PathProjection(["leaf-b", "target"], ["edge-a"], 2m);
        var edgeTieB = new RobustnessV0PathProjection(["leaf-a", "target"], ["edge-b"], 2m);
        var winner = new RobustnessV0PathProjection(["leaf-a", "target"], ["edge-a"], 2m);
        var lowerScore = new RobustnessV0PathProjection(["leaf-0", "target"], ["edge-0"], 1m);

        Assert.AreSame(
            winner,
            RobustnessV0Contract.SelectReportedPath([nodeTieB, edgeTieB, lowerScore, winner]));
    }

    [TestMethod]
    public void AllNodeAndEndpointKindsAreEligible()
    {
        var graph = GraphWith(
            [
                Node("root", "root"),
                Node("claim", "claim"),
                Node("evidence", "evidence"),
                Node("objection", "objection")
            ],
            [
                Edge("e-claim-root", "claim", "root", "support", 2m),
                Edge("e-evidence-claim", "evidence", "claim", "support", 2m)
            ]);

        Assert.IsTrue(graph.Nodes.All(RobustnessV0Contract.IsRankableNode));
        Assert.IsTrue(RobustnessV0Contract.IsStructuralLeaf(graph, "evidence"));
        Assert.IsTrue(RobustnessV0Contract.IsStructuralLeaf(graph, "objection"));
        Assert.IsFalse(RobustnessV0Contract.IsStructuralLeaf(graph, "claim"));
    }

    [TestMethod]
    public void Ranking_IsScoreAscendingThenOrdinalNodeIdAndLeastIsFirst()
    {
        var tiedB = new RobustnessV0RankedNode("b", 0.5m);
        var tiedA = new RobustnessV0RankedNode("a", 0.5m);
        var higher = new RobustnessV0RankedNode("higher", 0.9m);

        var ranking = RobustnessV0Contract.Rank([higher, tiedB, tiedA]);
        var least = RobustnessV0Contract.LeastRobust([higher, tiedB, tiedA]);

        CollectionAssert.AreEqual(
            new[] { tiedA, tiedB, higher },
            ranking.ToArray());
        Assert.AreSame(ranking[0], least);
    }

    [TestMethod]
    public void Validation_AllowsSharedDagAndRejectsDirectedCycles()
    {
        var sharedDag = GraphWith(
            [Node("root"), Node("left"), Node("right"), Node("leaf", "evidence")],
            [
                Edge("e-leaf-left", "leaf", "left", "support", 2m),
                Edge("e-leaf-right", "leaf", "right", "rebut", 0.5m),
                Edge("e-left-root", "left", "root", "support", 2m),
                Edge("e-right-root", "right", "root", "support", 2m)
            ]);
        var cycle = GraphWith(
            [Node("a"), Node("b")],
            [
                Edge("e-a-b", "a", "b", "support", 2m),
                Edge("e-b-a", "b", "a", "support", 2m)
            ]);

        Assert.IsTrue(RobustnessV0Contract.ValidateGraph(sharedDag).IsValid);
        var cycleValidation = RobustnessV0Contract.ValidateGraph(cycle);
        Assert.IsFalse(cycleValidation.IsValid);
        Assert.IsTrue(cycleValidation.Issues.Any(issue => issue.Code == "directed-cycle"));
    }

    private static void AssertVectorFormula(RobustnessV0Vector vector)
    {
        Assert.IsTrue(vector.AbsoluteProbabilityDelta >= 0d);
        AssertApproximately(
            (decimal)Math.Exp(-vector.AbsoluteProbabilityDelta),
            vector.RobustnessScore);
        Assert.IsTrue(vector.RobustnessScore >= RobustnessV0Contract.TheoreticalMinimumScore);
        Assert.IsTrue(vector.RobustnessScore <= RobustnessV0Contract.TheoreticalMaximumScore);
    }

    private static void AssertApproximately(
        decimal expected,
        decimal actual,
        decimal tolerance = 0.000000000001m)
    {
        Assert.IsTrue(
            Math.Abs(expected - actual) <= tolerance,
            $"Expected {expected} +/- {tolerance}, but got {actual}.");
    }

    private static Graph GraphWith(IEnumerable<GraphNode> nodes, IEnumerable<GraphEdge> edges) =>
        new()
        {
            Slug = "robustness-v0-contract",
            Nodes = nodes.ToList(),
            Edges = edges.ToList()
        };

    private static GraphNode Node(string id, string kind = "claim") =>
        new()
        {
            Id = id,
            Kind = kind
        };

    private static GraphEdge Edge(
        string id,
        string from,
        string to,
        string kind,
        decimal weight) =>
        new()
        {
            Id = id,
            From = from,
            To = to,
            Kind = kind,
            ImportanceToParent = weight
        };
}
