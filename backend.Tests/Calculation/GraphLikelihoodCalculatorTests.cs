using Backend.Calculation;
using Backend.Models.Domain;

namespace backend.Tests.Calculation;

[TestClass]
public class GraphLikelihoodCalculatorTests
{
    private readonly GraphLikelihoodCalculator _calculator = new();

    [TestMethod]
    public void RecalculateAncestors_AddsIndependentEvidenceLogLrs()
    {
        var context = GraphCalculationContext.From(
            [
                Node("A"),
                Node("B", kind: "evidence"),
                Node("C", kind: "evidence")
            ],
            [
                Edge("E-B-A", "B", "A", "support", 4),
                Edge("E-C-A", "C", "A", "support", 8)
            ]);

        var result = _calculator.RecalculateAncestors(context, "B");

        var expected = (decimal)Math.Log(4d) + (decimal)Math.Log(8d);
        AssertDecimalEqual(expected, result["A"]);
        AssertDecimalEqual(expected, context.NodesById["A"].PosteriorOdds);
    }

    [TestMethod]
    public void RecalculateAncestors_UsesLrBelowOneForCounterImpact()
    {
        var context = GraphCalculationContext.From(
            [Node("A"), Node("B", kind: "objection")],
            [Edge("E-B-A", "B", "A", "rebut", 0.1m)]);

        var result = _calculator.RecalculateAncestors(context, "B");

        var expected = (decimal)Math.Log(0.1d);
        AssertDecimalEqual(expected, result["A"]);
        AssertDecimalEqual(expected, context.NodesById["A"].PosteriorOdds);
    }

    [TestMethod]
    public void RecalculateAncestors_UsesAllSiblingsWhenRecalculatingParent()
    {
        var context = GraphCalculationContext.From(
            [
                Node("B"),
                Node("E", kind: "evidence"),
                Node("F", kind: "evidence")
            ],
            [
                Edge("E-E-B", "E", "B", "support", 4),
                Edge("E-F-B", "F", "B", "support", 0.5m)
            ]);

        var result = _calculator.RecalculateAncestors(context, "F");

        var expected = (decimal)Math.Log(4d) + (decimal)Math.Log(0.5d);
        AssertDecimalEqual(expected, result["B"]);
        AssertDecimalEqual(expected, context.NodesById["B"].PosteriorOdds);
    }

    [TestMethod]
    public void RecalculateAncestors_PropagatesToAncestors()
    {
        var context = GraphCalculationContext.From(
            [
                Node("A"),
                Node("B"),
                Node("F", kind: "evidence")
            ],
            [
                Edge("E-F-B", "F", "B", "support", 2),
                Edge("E-B-A", "B", "A", "support", 3)
            ]);

        var result = _calculator.RecalculateAncestors(context, "F");

        Assert.AreEqual(2, result.Count);
        AssertDecimalEqual((decimal)Math.Log(2d), result["B"]);
        AssertDecimalEqual((decimal)Math.Log(6d), result["A"]);
        AssertDecimalEqual((decimal)Math.Log(2d), context.NodesById["B"].PosteriorOdds);
        AssertDecimalEqual((decimal)Math.Log(6d), context.NodesById["A"].PosteriorOdds);
    }

    [TestMethod]
    public void RecalculateAncestors_UsesStrongestPathForSharedEvidence()
    {
        var context = GraphCalculationContext.From(
            [
                Node("A"),
                Node("B"),
                Node("C"),
                Node("F", kind: "evidence")
            ],
            [
                Edge("E-F-B", "F", "B", "support", 2),
                Edge("E-F-C", "F", "C", "support", 3),
                Edge("E-B-A", "B", "A", "support", 2),
                Edge("E-C-A", "C", "A", "support", 3)
            ]);

        var result = _calculator.RecalculateAncestors(context, "F");

        Assert.AreEqual(3, result.Count);
        AssertDecimalEqual((decimal)Math.Log(2d), result["B"]);
        AssertDecimalEqual((decimal)Math.Log(3d), result["C"]);
        AssertDecimalEqual((decimal)Math.Log(9d), result["A"]);
        AssertDecimalEqual((decimal)Math.Log(9d), context.NodesById["A"].PosteriorOdds);
    }

    [TestMethod]
    public void RecalculateAncestors_UsesMaximumDistanceForDuplicateReachability()
    {
        var context = GraphCalculationContext.From(
            [
                Node("A"),
                Node("B"),
                Node("C"),
                Node("D"),
                Node("F", kind: "evidence")
            ],
            [
                Edge("E-F-B", "F", "B", "support", 2),
                Edge("E-B-A", "B", "A", "support", 2),
                Edge("E-F-C", "F", "C", "support", 3),
                Edge("E-C-D", "C", "D", "support", 3),
                Edge("E-D-A", "D", "A", "support", 3)
            ]);

        var result = _calculator.RecalculateAncestors(context, "F");

        Assert.AreEqual(4, result.Count);
        AssertDecimalEqual((decimal)Math.Log(2d), result["B"]);
        AssertDecimalEqual((decimal)Math.Log(3d), result["C"]);
        AssertDecimalEqual((decimal)Math.Log(9d), result["D"]);
        AssertDecimalEqual((decimal)Math.Log(27d), result["A"]);
        AssertDecimalEqual((decimal)Math.Log(27d), context.NodesById["A"].PosteriorOdds);
    }

    [TestMethod]
    public void RecalculateAncestors_ReturnsEmptyDictionaryWhenChangedNodeHasNoParents()
    {
        var context = GraphCalculationContext.From([Node("A", logPriorOdds: 1.0m, logPosteriorOdds: 1.0m)], []);

        var result = _calculator.RecalculateAncestors(context, "A");

        Assert.AreEqual(0, result.Count);
    }

    [TestMethod]
    public void RecalculateNodesAndAncestors_RecalculatesStartingNodeAndAncestors()
    {
        var context = GraphCalculationContext.From(
            [
                Node("A", logPriorOdds: 1m, logPosteriorOdds: 1m),
                Node("B", logPriorOdds: 1m, logPosteriorOdds: 1m, kind: "evidence")
            ],
            [Edge("E-B-A", "B", "A", "support", 10)]);

        var result = _calculator.RecalculateNodesAndAncestors(context, ["B"]);

        Assert.AreEqual(2, result.Count);
        Assert.AreEqual(1m, result["B"]);
        AssertDecimalEqual(1m + (decimal)Math.Log(10d), result["A"]);
        Assert.AreEqual(1m, context.NodesById["B"].PosteriorOdds);
        AssertDecimalEqual(1m + (decimal)Math.Log(10d), context.NodesById["A"].PosteriorOdds);
    }

    [TestMethod]
    public void RecalculateAncestors_ClampsHighLogOdds()
    {
        var context = GraphCalculationContext.From(
            [Node("A"), Node("B", kind: "evidence"), Node("C", kind: "evidence")],
            [
                Edge("E-B-A", "B", "A", "support", 10000000000000000000000000000m),
                Edge("E-C-A", "C", "A", "support", 10000000000000000000000000000m)
            ]);

        var result = _calculator.RecalculateAncestors(context, "B");

        Assert.AreEqual(100m, result["A"]);
        Assert.AreEqual(100m, context.NodesById["A"].PosteriorOdds);
    }

    [TestMethod]
    public void RecalculateAncestors_ClampsLowLogOdds()
    {
        var context = GraphCalculationContext.From(
            [Node("A"), Node("B", kind: "evidence"), Node("C", kind: "evidence")],
            [
                Edge("E-B-A", "B", "A", "rebut", 0.0000000000000000000000000001m),
                Edge("E-C-A", "C", "A", "rebut", 0.0000000000000000000000000001m)
            ]);

        var result = _calculator.RecalculateAncestors(context, "B");

        Assert.AreEqual(-100m, result["A"]);
        Assert.AreEqual(-100m, context.NodesById["A"].PosteriorOdds);
    }

    [TestMethod]
    public void RecalculateAncestors_DoesNotUseEdgeKindToDetermineDirection()
    {
        var context = GraphCalculationContext.From(
            [Node("A"), Node("B", kind: "evidence")],
            [Edge("E-B-A", "B", "A", "mystery", 10)]);

        var result = _calculator.RecalculateAncestors(context, "B");

        AssertDecimalEqual((decimal)Math.Log(10d), result["A"]);
    }

    [TestMethod]
    public void RecalculateAncestors_CounterLabelDoesNotInvertPositiveLr()
    {
        var context = GraphCalculationContext.From(
            [Node("A"), Node("B", kind: "evidence")],
            [Edge("E-B-A", "B", "A", "counter", 10)]);

        var result = _calculator.RecalculateAncestors(context, "B");

        AssertDecimalEqual((decimal)Math.Log(10d), result["A"]);
    }

    [TestMethod]
    public void RecalculateAncestors_ThrowsForCycle()
    {
        var context = GraphCalculationContext.From(
            [
                Node("A", logPriorOdds: 1m, logPosteriorOdds: 1m),
                Node("B", logPriorOdds: 1m, logPosteriorOdds: 1m)
            ],
            [
                Edge("E-A-B", "A", "B"),
                Edge("E-B-A", "B", "A")
            ]);

        var exception = Assert.ThrowsException<InvalidOperationException>(() =>
            _calculator.RecalculateAncestors(context, "A"));

        StringAssert.Contains(exception.Message, "Cycle detected");
    }

    [TestMethod]
    public void GetAccumlatedLR_BranchingPaths()
    {
        var context = GraphCalculationContext.From(
            [Node("A"), Node("B1"), Node("B2"), Node("C1", kind: "evidence"), Node("C2", kind: "rebut"), Node("C3")],
            [Edge("E-B1-A", "B1", "A", kind: "support", likelihoodRatio: 1.3m),
            Edge("E-B2-A", "B2", "A", kind: "support", likelihoodRatio: 1.1m),
            Edge("E-C1-B1", "C1", "B1", kind: "support", likelihoodRatio: 1.2m),
            Edge("E-C2-B1", "C2", "B1", kind: "objection", likelihoodRatio: 0.01m),
            Edge("E-C2-B2", "C2", "B2", kind: "objection", likelihoodRatio: 0.1m),
            Edge("E-C3-B2", "C3", "B2", kind: "support", likelihoodRatio: 1.5m)]
        );

        var result = _calculator.GetSingleAccumulatedLR(context, "C2", "A");
        Assert.IsNotNull(result);
        AssertDecimalEqual(0.013m, result.Value);
    }

    [TestMethod]
    public void GetAccumulatedLR_ReturnsTheOnlyPathLikelihoodRatio()
    {
        var context = GraphCalculationContext.From(
            [Node("claim"), Node("evidence", kind: "evidence")],
            [Edge("E-evidence-claim", "evidence", "claim", likelihoodRatio: 1.5m)]);

        var result = _calculator.GetSingleAccumulatedLR(context, "evidence", "claim");

        Assert.IsNotNull(result);
        AssertDecimalEqual(1.5m, result.Value);
    }

    [TestMethod]
    public void GetAccumulatedLR_MultipliesLikelihoodRatiosAlongThePath()
    {
        var context = GraphCalculationContext.From(
            [Node("claim"), Node("premise"), Node("evidence", kind: "evidence")],
            [
                Edge("E-evidence-premise", "evidence", "premise", likelihoodRatio: 0.25m),
                Edge("E-premise-claim", "premise", "claim", likelihoodRatio: 1.8m)
            ]);

        var result = _calculator.GetSingleAccumulatedLR(context, "evidence", "claim");

        Assert.IsNotNull(result);
        AssertDecimalEqual(0.45m, result.Value);
    }

    [TestMethod]
    public void GetAccumulatedLR_ChoosesThePathFarthestFromNeutral()
    {
        var context = GraphCalculationContext.From(
            [Node("claim"), Node("pathA"), Node("pathB"), Node("evidence", kind: "evidence")],
            [
                Edge("E-evidence-pathA", "evidence", "pathA", likelihoodRatio: 0.1m),
                Edge("E-pathA-claim", "pathA", "claim", likelihoodRatio: 1m),
                Edge("E-evidence-pathB", "evidence", "pathB", likelihoodRatio: 1.8m),
                Edge("E-pathB-claim", "pathB", "claim", likelihoodRatio: 1m)
            ]);

        var result = _calculator.GetSingleAccumulatedLR(context, "evidence", "claim");

        Assert.IsNotNull(result);
        AssertDecimalEqual(0.1m, result.Value);
    }

    [TestMethod]
    public void GetAccumulatedLR_ReturnsNullWhenNoPathReachesTheClaim()
    {
        var context = GraphCalculationContext.From(
            [Node("claim"), Node("otherClaim"), Node("evidence", kind: "evidence")],
            [Edge("E-evidence-other", "evidence", "otherClaim", likelihoodRatio: 1.5m)]);

        var result = _calculator.GetSingleAccumulatedLR(context, "evidence", "claim");

        Assert.IsNull(result);
    }

    [TestMethod]
    public void GetAccumulatedLR_ThrowsWhenAnEdgeLikelihoodRatioIsZero()
    {
        var context = GraphCalculationContext.From(
            [Node("claim"), Node("evidence", kind: "evidence")],
            [Edge("E-evidence-claim", "evidence", "claim", likelihoodRatio: 0m)]);

        var exception = Assert.ThrowsException<InvalidOperationException>(() =>
            _calculator.GetSingleAccumulatedLR(context, "evidence", "claim"));

        StringAssert.Contains(exception.Message, "range (0, 1]");
    }

    [TestMethod]
    public void GetMinLogPath_ReturnsZeroWhenStartIsTargetWithoutEdges()
    {
        var context = GraphCalculationContext.From([Node("claim")], []);

        var result = _calculator.GetMinLogPath(context, "claim", "claim");

        Assert.AreEqual(0m, result);
    }

    [TestMethod]
    public void GetMinLogPath_ReturnsNullWhenNoPathReachesTarget()
    {
        var context = GraphCalculationContext.From(
            [Node("start"), Node("dead-end"), Node("claim")],
            [Edge("E-start-dead", "start", "dead-end", likelihoodRatio: 2m)]);

        var result = _calculator.GetMinLogPath(context, "start", "claim");

        Assert.IsNull(result);
    }

    [TestMethod]
    public void GetMinLogPath_IgnoresDeadEndAndUsesReachableBranch()
    {
        var context = GraphCalculationContext.From(
            [Node("start"), Node("dead-end"), Node("reachable"), Node("claim")],
            [
                Edge("E-start-dead", "start", "dead-end", likelihoodRatio: 0.1m),
                Edge("E-start-reachable", "start", "reachable", likelihoodRatio: 2m),
                Edge("E-reachable-claim", "reachable", "claim", likelihoodRatio: 3m)
            ]);

        var result = _calculator.GetMinLogPath(context, "start", "claim");

        Assert.IsNotNull(result);
        AssertDecimalEqual((decimal)Math.Log(6d), result.Value);
    }

    [TestMethod]
    public void GetMinLogPath_SelectsPathWithSmallestProductOfWeights()
    {
        var context = GraphCalculationContext.From(
            [Node("start"), Node("path-a"), Node("path-b"), Node("claim")],
            [
                Edge("E-start-a", "start", "path-a", likelihoodRatio: 0.5m),
                Edge("E-a-claim", "path-a", "claim", likelihoodRatio: 0.5m),
                Edge("E-start-b", "start", "path-b", likelihoodRatio: 2m),
                Edge("E-b-claim", "path-b", "claim", likelihoodRatio: 2m)
            ]);

        var result = _calculator.GetMinLogPath(context, "start", "claim");

        Assert.IsNotNull(result);
        AssertDecimalEqual((decimal)Math.Log(0.25d), result.Value);
    }

    [TestMethod]
    public void GetMaxLogPath_SelectsPathWithLargestProductOfWeights()
    {
        var context = GraphCalculationContext.From(
            [Node("start"), Node("path-a"), Node("path-b"), Node("claim")],
            [
                Edge("E-start-a", "start", "path-a", likelihoodRatio: 0.5m),
                Edge("E-a-claim", "path-a", "claim", likelihoodRatio: 0.5m),
                Edge("E-start-b", "start", "path-b", likelihoodRatio: 2m),
                Edge("E-b-claim", "path-b", "claim", likelihoodRatio: 2m)
            ]);

        var result = _calculator.GetMaxLogPath(context, "start", "claim");

        Assert.IsNotNull(result);
        AssertDecimalEqual((decimal)Math.Log(4d), result.Value);
    }

    [TestMethod]
    public void GetLogPath_SupportsExplicitMinimumAndMaximumSelection()
    {
        var context = GraphCalculationContext.From(
            [Node("start"), Node("path-a"), Node("path-b"), Node("claim")],
            [
                Edge("E-start-a", "start", "path-a", likelihoodRatio: 0.5m),
                Edge("E-a-claim", "path-a", "claim", likelihoodRatio: 0.5m),
                Edge("E-start-b", "start", "path-b", likelihoodRatio: 2m),
                Edge("E-b-claim", "path-b", "claim", likelihoodRatio: 2m)
            ]);

        var minimum = _calculator.GetLogPath(context, "start", "claim", LogPathSelection.Minimum);
        var maximum = _calculator.GetLogPath(context, "start", "claim", LogPathSelection.Maximum);

        Assert.IsNotNull(minimum);
        Assert.IsNotNull(maximum);
        AssertDecimalEqual((decimal)Math.Log(0.25d), minimum.Value);
        AssertDecimalEqual((decimal)Math.Log(4d), maximum.Value);
    }

    [TestMethod]
    public void GetMinAndMaxLogPath_MatchExplicitSelections()
    {
        var context = GraphCalculationContext.From(
            [Node("start"), Node("path-a"), Node("path-b"), Node("claim")],
            [
                Edge("E-start-a", "start", "path-a", likelihoodRatio: 0.5m),
                Edge("E-a-claim", "path-a", "claim", likelihoodRatio: 0.5m),
                Edge("E-start-b", "start", "path-b", likelihoodRatio: 2m),
                Edge("E-b-claim", "path-b", "claim", likelihoodRatio: 2m)
            ]);

        var minimum = _calculator.GetMinLogPath(context, "start", "claim");
        var explicitMinimum = _calculator.GetLogPath(
            context, "start", "claim", LogPathSelection.Minimum);
        var maximum = _calculator.GetMaxLogPath(context, "start", "claim");
        var explicitMaximum = _calculator.GetLogPath(
            context, "start", "claim", LogPathSelection.Maximum);

        Assert.AreEqual(explicitMinimum, minimum);
        Assert.AreEqual(explicitMaximum, maximum);
    }

    [TestMethod]
    public void GetMaxLogPath_ReturnsNullWhenNoPathReachesTarget()
    {
        var context = GraphCalculationContext.From(
            [Node("start"), Node("dead-end"), Node("claim")],
            [Edge("E-start-dead", "start", "dead-end", likelihoodRatio: 2m)]);

        var result = _calculator.GetMaxLogPath(context, "start", "claim");

        Assert.IsNull(result);
    }

    [TestMethod]
    public void GetLogPath_ThrowsForUnknownSelection()
    {
        var context = GraphCalculationContext.From([Node("start"), Node("claim")], []);

        var exception = Assert.ThrowsException<ArgumentOutOfRangeException>(() =>
            _calculator.GetLogPath(context, "start", "claim", (LogPathSelection)99));

        Assert.AreEqual("selection", exception.ParamName);
    }

    [TestMethod]
    public void GetLogPath_ThrowsWhenStartNodeDoesNotExist()
    {
        var context = GraphCalculationContext.From([Node("claim")], []);

        var exception = Assert.ThrowsException<InvalidOperationException>(() =>
            _calculator.GetLogPath(context, "missing", "claim", LogPathSelection.Minimum));

        StringAssert.Contains(exception.Message, "Start node 'missing'");
    }

    [TestMethod]
    public void GetLogPath_ThrowsWhenTargetNodeDoesNotExist()
    {
        var context = GraphCalculationContext.From([Node("start")], []);

        var exception = Assert.ThrowsException<InvalidOperationException>(() =>
            _calculator.GetLogPath(context, "start", "missing", LogPathSelection.Maximum));

        StringAssert.Contains(exception.Message, "Target claim 'missing'");
    }

    [TestMethod]
    public void GetMinLogPath_ThrowsForNonPositiveWeight()
    {
        var context = GraphCalculationContext.From(
            [Node("start"), Node("claim")],
            [Edge("E-start-claim", "start", "claim", likelihoodRatio: 0m)]);

        var exception = Assert.ThrowsException<InvalidOperationException>(() =>
            _calculator.GetMinLogPath(context, "start", "claim"));

        StringAssert.Contains(exception.Message, "range (0, 1]");
    }

    [TestMethod]
    public void GetStrongestPaths_TraversesUpFromChildToAncestors()
    {
        var context = GraphCalculationContext.From(
            [Node("claim"), Node("premise"), Node("evidence"), Node("unrelated")],
            [
                Edge("E-evidence-premise", "evidence", "premise", likelihoodRatio: 2m),
                Edge("E-premise-claim", "premise", "claim", likelihoodRatio: 3m)
            ]);

        var result = _calculator.GetStrongestPaths(context, "evidence", PathDirection.Up);

        CollectionAssert.AreEquivalent(
            new[] { "evidence", "premise", "claim" },
            result.Keys.ToArray());
        AssertDecimalEqual(0m, result["evidence"]);
        AssertDecimalEqual((decimal)Math.Log(2d), result["premise"]);
        AssertDecimalEqual((decimal)Math.Log(6d), result["claim"]);
        Assert.IsFalse(result.ContainsKey("unrelated"));
    }

    [TestMethod]
    public void GetStrongestPaths_TraversesDownFromParentToDescendants()
    {
        var context = GraphCalculationContext.From(
            [Node("claim"), Node("premise"), Node("evidence")],
            [
                Edge("E-evidence-premise", "evidence", "premise", likelihoodRatio: 2m),
                Edge("E-premise-claim", "premise", "claim", likelihoodRatio: 3m)
            ]);

        var result = _calculator.GetStrongestPaths(context, "claim", PathDirection.Down);

        AssertDecimalEqual(0m, result["claim"]);
        AssertDecimalEqual((decimal)Math.Log(3d), result["premise"]);
        AssertDecimalEqual((decimal)Math.Log(6d), result["evidence"]);
    }

    [TestMethod]
    public void GetStrongestPaths_SelectsPathFarthestFromNeutralForEachNode()
    {
        var context = GraphCalculationContext.From(
            [Node("claim"), Node("path-a"), Node("path-b"), Node("evidence")],
            [
                Edge("E-evidence-a", "evidence", "path-a", likelihoodRatio: 0.1m),
                Edge("E-a-claim", "path-a", "claim", likelihoodRatio: 0.5m),
                Edge("E-evidence-b", "evidence", "path-b", likelihoodRatio: 2m),
                Edge("E-b-claim", "path-b", "claim", likelihoodRatio: 2m)
            ]);

        var result = _calculator.GetStrongestPaths(context, "evidence", PathDirection.Up);

        AssertDecimalEqual((decimal)Math.Log(0.1d), result["path-a"]);
        AssertDecimalEqual((decimal)Math.Log(2d), result["path-b"]);
        AssertDecimalEqual((decimal)Math.Log(0.05d), result["claim"]);
    }

    [TestMethod]
    public void GetStrongestPaths_ReturnsOnlyStartNodeWhenItHasNoReachableNeighbors()
    {
        var context = GraphCalculationContext.From(
            [Node("start"), Node("other")],
            []);

        var result = _calculator.GetStrongestPaths(context, "start", PathDirection.Up);

        Assert.AreEqual(1, result.Count);
        AssertDecimalEqual(0m, result["start"]);
    }

    [TestMethod]
    public void GetStrongestPaths_ThrowsWhenStartNodeDoesNotExist()
    {
        var context = GraphCalculationContext.From([Node("claim")], []);

        var exception = Assert.ThrowsException<InvalidOperationException>(() =>
            _calculator.GetStrongestPaths(context, "missing", PathDirection.Up));

        StringAssert.Contains(exception.Message, "Node 'missing'");
    }

    [TestMethod]
    public void GetStrongestPaths_BranchingPaths()
    {
        var context = GraphCalculationContext.From(
            [Node("A"), Node("B1"), Node("B2"), Node("C1", kind: "evidence"), Node("C2", kind: "rebut"), Node("C3")],
            [Edge("E-B1-A", "B1", "A", kind: "support", likelihoodRatio: 1.3m),
            Edge("E-B2-A", "B2", "A", kind: "support", likelihoodRatio: 1.1m),
            Edge("E-C1-B1", "C1", "B1", kind: "support", likelihoodRatio: 1.2m),
            Edge("E-C2-B1", "C2", "B1", kind: "objection", likelihoodRatio: 0.01m),
            Edge("E-C2-B2", "C2", "B2", kind: "objection", likelihoodRatio: 0.1m),
            Edge("E-C3-B2", "C3", "B2", kind: "support", likelihoodRatio: 1.5m)]
        );

        var result = _calculator.GetStrongestPaths(context, "A", PathDirection.Down);
        Assert.IsNotNull(result);
        AssertDecimalEqual(0.2623m, result["B1"], 0.0001m);
        AssertDecimalEqual(0.0953m, result["B2"], 0.0001m);
        AssertDecimalEqual(0.4446m, result["C1"], 0.0001m);
        AssertDecimalEqual(-4.3428m, result["C2"], 0.0001m);
    }

    [TestMethod]
    public void GetAllNodeRobustness_CalculatesRecurrenceForEveryNode()
    {
        var graph = new Graph
        {
            Nodes =
            [
                Node("root", logPriorOdds: 0.4m, logPosteriorOdds: 0.4m),
                Node("branch", logPriorOdds: 1m, logPosteriorOdds: 1m),
                Node("support-leaf", kind: "evidence"),
                Node("counter-leaf", kind: "objection"),
                Node("direct-counter-leaf", kind: "objection")
            ],
            Edges =
            [
                Edge("E-support-branch", "support-leaf", "branch", likelihoodRatio: 2m),
                Edge("E-counter-branch", "counter-leaf", "branch", likelihoodRatio: 0.5m),
                Edge("E-branch-root", "branch", "root", likelihoodRatio: 3m),
                Edge("E-counter-root", "direct-counter-leaf", "root", likelihoodRatio: 0.1m)
            ]
        };

        var result = _calculator.GetAllNodeRobustness(graph);

        Assert.AreEqual(graph.Nodes.Count, result.Count);
        AssertDecimalEqual(1m, result["support-leaf"]);
        AssertDecimalEqual(1m, result["counter-leaf"]);
        AssertDecimalEqual(1m, result["direct-counter-leaf"]);

        decimal expectedBranchRobustness = RobustnessAfterRemovingPath(
            posteriorLogOdds: 1m,
            pathLogLr: (decimal)Math.Log(2d));
        AssertDecimalEqual(expectedBranchRobustness, result["branch"]);

        decimal expectedRootRobustness = RobustnessAfterRemovingPath(
            posteriorLogOdds: 0.4m,
            pathLogLr: (decimal)Math.Log(6d));
        AssertDecimalEqual(expectedRootRobustness, result["root"]);
        Assert.IsTrue(result["branch"] is > 0m and <= 1m);
        Assert.IsTrue(result["root"] is > 0m and <= 1m);
    }

    [TestMethod]
    public void GetAllNodeRobustness_RejectsCycles()
    {
        var graph = new Graph
        {
            Nodes = [Node("A"), Node("B")],
            Edges =
            [
                Edge("E-A-B", "A", "B", likelihoodRatio: 2m),
                Edge("E-B-A", "B", "A", likelihoodRatio: 3m)
            ]
        };

        var exception = Assert.ThrowsException<InvalidOperationException>(() =>
            _calculator.GetAllNodeRobustness(graph));

        StringAssert.Contains(exception.Message, "Cycle detected");
    }

    [TestMethod]
    public void GetAllNodeRobustness_ThrowsWhenCancelled()
    {
        var graph = new Graph { Nodes = [Node("A")] };
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.ThrowsException<OperationCanceledException>(() =>
            _calculator.GetAllNodeRobustness(graph, cancellation.Token));
    }

    [TestMethod]
    public void GetAllNodeRobustness_BranchPaths()
    {
        var graph = new Graph
        {
            Nodes =
            [
                Node("A", logPriorOdds: -0.2m, logPosteriorOdds: 0.157674m),
                Node("B1", logPriorOdds: 0.4m, logPosteriorOdds: -4.022849m),
                Node("B2", logPriorOdds: -0.3m, logPosteriorOdds: -2.197120m),
                Node("C1", logPriorOdds: 0.8m, logPosteriorOdds: 0.8m, kind: "evidence"),
                Node("C2", logPriorOdds: -0.6m, logPosteriorOdds: -0.6m, kind: "objection"),
                Node("C3", logPriorOdds: 1.1m, logPosteriorOdds: 1.1m, kind: "evidence")
            ],
            Edges =
            [
                Edge("E-B1-A", "B1", "A", "support", 1.3m),
                Edge("E-B2-A", "B2", "A", "support", 1.1m),
                Edge("E-C1-B1", "C1", "B1", "support", 1.2m),
                Edge("E-C2-B1", "C2", "B1", "objection", 0.01m),
                Edge("E-C2-B2", "C2", "B2", "objection", 0.1m),
                Edge("E-C3-B2", "C3", "B2", "support", 1.5m)
            ]
        };

        var calculator = new GraphLikelihoodCalculator();
        var result = calculator.GetAllNodeRobustness(graph, CancellationToken.None);

        foreach (var node in graph.Nodes)
        {
            Assert.IsTrue(result.ContainsKey(node.Id));
        }
        decimal expectedRobustness = RobustnessAfterRemovingPath(
            posteriorLogOdds: 0.157674m,
            pathLogLr: (decimal)Math.Log(1.5d * 1.1d));
        AssertDecimalEqual(expectedRobustness, result["A"], 0.0001m);
    }




    private static GraphNode Node(
        string id,
        decimal logPriorOdds = 0m,
        decimal logPosteriorOdds = 0m,
        string kind = "claim")
    {
        return new GraphNode
        {
            Id = id,
            Kind = kind,
            PriorOdds = logPriorOdds,
            PosteriorOdds = logPosteriorOdds
        };
    }

    private static GraphEdge Edge(
        string id,
        string from,
        string to,
        string kind = "support",
        decimal likelihoodRatio = 10m)
    {
        return new GraphEdge
        {
            Id = id,
            From = from,
            To = to,
            Kind = kind,
            ProbabilityGivenParent = likelihoodRatio >= 1m ? 1m : likelihoodRatio,
            ProbabilityGivenNotParent = likelihoodRatio >= 1m
                ? 1m / likelihoodRatio
                : 1m
        };
    }

    private static void AssertDecimalEqual(decimal expected, decimal actual, decimal tolerance = 0.000001m)
    {
        Assert.IsTrue(
            Math.Abs(expected - actual) <= tolerance,
            $"Expected {actual} to be within {tolerance} of {expected}.");
    }

    private static decimal RobustnessAfterRemovingPath(
        decimal posteriorLogOdds,
        decimal pathLogLr)
    {
        static double ToProbability(decimal logOdds) => 1d / (1d + Math.Exp(-(double)logOdds));

        double probabilityWithAllEvidence = ToProbability(posteriorLogOdds);
        double probabilityWithoutPath = ToProbability(posteriorLogOdds - pathLogLr);
        double probabilityDifference = Math.Abs(probabilityWithAllEvidence - probabilityWithoutPath);

        return (decimal)Math.Exp(-probabilityDifference);
    }
}
