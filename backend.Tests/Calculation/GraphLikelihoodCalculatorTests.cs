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
        var context = GraphCalculationContext.From([Node("A", 1.0m)], []);

        var result = _calculator.RecalculateAncestors(context, "A");

        Assert.AreEqual(0, result.Count);
    }

    [TestMethod]
    public void RecalculateNodesAndAncestors_RecalculatesStartingNodeAndAncestors()
    {
        var context = GraphCalculationContext.From(
            [
                Node("A", 1m),
                Node("B", 1m, "evidence")
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
            [Node("A", 1m), Node("B", 1m)],
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
            [Edge("E-B1-A", "B1", "A", kind: "support", importanceToParent: 1.3m),
            Edge("E-B2-A", "B2", "A", kind: "support", importanceToParent: 1.1m),
            Edge("E-C1-B1", "C1", "B1", kind: "support", importanceToParent: 1.2m),
            Edge("E-C2-B1", "C2", "B1", kind: "objection", importanceToParent: 0.01m),
            Edge("E-C2-B2", "C2", "B2", kind: "objection", importanceToParent: 0.1m),
            Edge("E-C3-B2", "C3", "B2", kind: "support", importanceToParent: 1.5m)]
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
            [Edge("E-evidence-claim", "evidence", "claim", importanceToParent: 1.5m)]);

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
                Edge("E-evidence-premise", "evidence", "premise", importanceToParent: 0.25m),
                Edge("E-premise-claim", "premise", "claim", importanceToParent: 1.8m)
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
                Edge("E-evidence-pathA", "evidence", "pathA", importanceToParent: 0.1m),
                Edge("E-pathA-claim", "pathA", "claim", importanceToParent: 1m),
                Edge("E-evidence-pathB", "evidence", "pathB", importanceToParent: 1.8m),
                Edge("E-pathB-claim", "pathB", "claim", importanceToParent: 1m)
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
            [Edge("E-evidence-other", "evidence", "otherClaim", importanceToParent: 1.5m)]);

        var result = _calculator.GetSingleAccumulatedLR(context, "evidence", "claim");

        Assert.IsNull(result);
    }

    [TestMethod]
    public void GetAccumulatedLR_ThrowsWhenAnEdgeLikelihoodRatioIsZero()
    {
        var context = GraphCalculationContext.From(
            [Node("claim"), Node("evidence", kind: "evidence")],
            [Edge("E-evidence-claim", "evidence", "claim", importanceToParent: 0m)]);

        var exception = Assert.ThrowsException<InvalidOperationException>(() =>
            _calculator.GetSingleAccumulatedLR(context, "evidence", "claim"));

        StringAssert.Contains(exception.Message, "must be greater than zero");
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
            [Edge("E-start-dead", "start", "dead-end", importanceToParent: 2m)]);

        var result = _calculator.GetMinLogPath(context, "start", "claim");

        Assert.IsNull(result);
    }

    [TestMethod]
    public void GetMinLogPath_IgnoresDeadEndAndUsesReachableBranch()
    {
        var context = GraphCalculationContext.From(
            [Node("start"), Node("dead-end"), Node("reachable"), Node("claim")],
            [
                Edge("E-start-dead", "start", "dead-end", importanceToParent: 0.1m),
                Edge("E-start-reachable", "start", "reachable", importanceToParent: 2m),
                Edge("E-reachable-claim", "reachable", "claim", importanceToParent: 3m)
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
                Edge("E-start-a", "start", "path-a", importanceToParent: 0.5m),
                Edge("E-a-claim", "path-a", "claim", importanceToParent: 0.5m),
                Edge("E-start-b", "start", "path-b", importanceToParent: 2m),
                Edge("E-b-claim", "path-b", "claim", importanceToParent: 2m)
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
                Edge("E-start-a", "start", "path-a", importanceToParent: 0.5m),
                Edge("E-a-claim", "path-a", "claim", importanceToParent: 0.5m),
                Edge("E-start-b", "start", "path-b", importanceToParent: 2m),
                Edge("E-b-claim", "path-b", "claim", importanceToParent: 2m)
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
                Edge("E-start-a", "start", "path-a", importanceToParent: 0.5m),
                Edge("E-a-claim", "path-a", "claim", importanceToParent: 0.5m),
                Edge("E-start-b", "start", "path-b", importanceToParent: 2m),
                Edge("E-b-claim", "path-b", "claim", importanceToParent: 2m)
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
                Edge("E-start-a", "start", "path-a", importanceToParent: 0.5m),
                Edge("E-a-claim", "path-a", "claim", importanceToParent: 0.5m),
                Edge("E-start-b", "start", "path-b", importanceToParent: 2m),
                Edge("E-b-claim", "path-b", "claim", importanceToParent: 2m)
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
            [Edge("E-start-dead", "start", "dead-end", importanceToParent: 2m)]);

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
            [Edge("E-start-claim", "start", "claim", importanceToParent: 0m)]);

        var exception = Assert.ThrowsException<InvalidOperationException>(() =>
            _calculator.GetMinLogPath(context, "start", "claim"));

        StringAssert.Contains(exception.Message, "must be greater than zero");
    }

    [TestMethod]
    public void GetStrongestPaths_TraversesUpFromChildToAncestors()
    {
        var context = GraphCalculationContext.From(
            [Node("claim"), Node("premise"), Node("evidence"), Node("unrelated")],
            [
                Edge("E-evidence-premise", "evidence", "premise", importanceToParent: 2m),
                Edge("E-premise-claim", "premise", "claim", importanceToParent: 3m)
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
                Edge("E-evidence-premise", "evidence", "premise", importanceToParent: 2m),
                Edge("E-premise-claim", "premise", "claim", importanceToParent: 3m)
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
                Edge("E-evidence-a", "evidence", "path-a", importanceToParent: 0.1m),
                Edge("E-a-claim", "path-a", "claim", importanceToParent: 0.5m),
                Edge("E-evidence-b", "evidence", "path-b", importanceToParent: 2m),
                Edge("E-b-claim", "path-b", "claim", importanceToParent: 2m)
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
            [Edge("E-B1-A", "B1", "A", kind: "support", importanceToParent: 1.3m),
            Edge("E-B2-A", "B2", "A", kind: "support", importanceToParent: 1.1m),
            Edge("E-C1-B1", "C1", "B1", kind: "support", importanceToParent: 1.2m),
            Edge("E-C2-B1", "C2", "B1", kind: "objection", importanceToParent: 0.01m),
            Edge("E-C2-B2", "C2", "B2", kind: "objection", importanceToParent: 0.1m),
            Edge("E-C3-B2", "C3", "B2", kind: "support", importanceToParent: 1.5m)]
        );

        var result = _calculator.GetStrongestPaths(context, "A", PathDirection.Down);
        Assert.IsNotNull(result);
        AssertDecimalEqual(0.2623m, result["B1"], 0.0001m);
        AssertDecimalEqual(0.0953m, result["B2"], 0.0001m);
        AssertDecimalEqual(0.4446m, result["C1"], 0.0001m);
        AssertDecimalEqual(-4.3428m, result["C2"], 0.0001m);
    }

    [TestMethod]
    public void GetNodeFragilities_CalculatesRecurrenceForEveryNode()
    {
        var graph = new Graph
        {
            Nodes =
            [
                Node("root", logOdds: 0.4m),
                Node("branch", logOdds: 1m),
                Node("support-leaf", kind: "evidence"),
                Node("counter-leaf", kind: "objection"),
                Node("direct-counter-leaf", kind: "objection")
            ],
            Edges =
            [
                Edge("E-support-branch", "support-leaf", "branch", importanceToParent: 2m),
                Edge("E-counter-branch", "counter-leaf", "branch", importanceToParent: 0.5m),
                Edge("E-branch-root", "branch", "root", importanceToParent: 3m),
                Edge("E-counter-root", "direct-counter-leaf", "root", importanceToParent: 0.1m)
            ]
        };

        var result = _calculator.GetNodeFragilities(graph);

        Assert.AreEqual(graph.Nodes.Count, result.Count);
        AssertDecimalEqual(0m, result["support-leaf"]);
        AssertDecimalEqual(0m, result["counter-leaf"]);
        AssertDecimalEqual(0m, result["direct-counter-leaf"]);

        decimal expectedBranchFragility = MaximumAbsoluteProbabilityDifference(
            posteriorLogOdds: 1m,
            minimumLogLr: (decimal)Math.Log(0.5d),
            maximumLogLr: (decimal)Math.Log(2d));
        AssertDecimalEqual(expectedBranchFragility, result["branch"]);

        decimal expectedRootFragility = MaximumAbsoluteProbabilityDifference(
            posteriorLogOdds: 0.4m,
            minimumLogLr: (decimal)Math.Log(0.1d),
            maximumLogLr: (decimal)Math.Log(6d));
        AssertDecimalEqual(expectedRootFragility, result["root"]);
        Assert.IsTrue(result["branch"] > 0m);
        Assert.IsTrue(result["root"] > 0m);
    }

    [TestMethod]
    public void GetNodeFragilities_RejectsCycles()
    {
        var graph = new Graph
        {
            Nodes = [Node("A"), Node("B")],
            Edges =
            [
                Edge("E-A-B", "A", "B", importanceToParent: 2m),
                Edge("E-B-A", "B", "A", importanceToParent: 3m)
            ]
        };

        var exception = Assert.ThrowsException<InvalidOperationException>(() =>
            _calculator.GetNodeFragilities(graph));

        StringAssert.Contains(exception.Message, "Cycle detected");
    }

    [TestMethod]
    public void GetNodeFragilities_ThrowsWhenCancelled()
    {
        var graph = new Graph { Nodes = [Node("A")] };
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.ThrowsException<OperationCanceledException>(() =>
            _calculator.GetNodeFragilities(graph, cancellation.Token));
    }



    private static GraphNode Node(string id, decimal logOdds = 0m, string kind = "claim")
    {
        return new GraphNode
        {
            Id = id,
            Kind = kind,
            PriorOdds = logOdds,
            PosteriorOdds = logOdds
        };
    }

    private static GraphEdge Edge(
        string id,
        string from,
        string to,
        string kind = "support",
        decimal importanceToParent = 10m)
    {
        return new GraphEdge
        {
            Id = id,
            From = from,
            To = to,
            Kind = kind,
            ImportanceToParent = importanceToParent
        };
    }

    private static void AssertDecimalEqual(decimal expected, decimal actual, decimal tolerance = 0.000001m)
    {
        Assert.IsTrue(
            Math.Abs(expected - actual) <= tolerance,
            $"Expected {actual} to be within {tolerance} of {expected}.");
    }

    private static decimal MaximumAbsoluteProbabilityDifference(
        decimal posteriorLogOdds,
        decimal minimumLogLr,
        decimal maximumLogLr)
    {
        static double ToProbability(decimal logOdds) => 1d / (1d + Math.Exp(-(double)logOdds));

        double probabilityWithAllEvidence = ToProbability(posteriorLogOdds);
        double minimumPathDifference = Math.Abs(
            probabilityWithAllEvidence - ToProbability(posteriorLogOdds - minimumLogLr));
        double maximumPathDifference = Math.Abs(
            probabilityWithAllEvidence - ToProbability(posteriorLogOdds - maximumLogLr));

        return (decimal)Math.Max(minimumPathDifference, maximumPathDifference);
    }
}
