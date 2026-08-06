using Backend.Calculation;
using Backend.Models.Domain;

namespace backend.Tests.Calculation;

[TestClass]
public class GraphLikelihoodCalculatorTests
{
    private readonly GraphLikelihoodCalculator _calculator = new();

    [TestMethod]
    public void RecalculateAncestors_CalculatesTwoChildSupportCase()
    {
        var context = GraphCalculationContext.From(
            [
                Node("A"),
                Node("B", -1.0986122887m),
                Node("C", 1.0986122887m)
            ],
            [
                Edge("E-B-A", "B", "A", "support", 4),
                Edge("E-C-A", "C", "A", "support", 8)
            ]);

        var result = _calculator.RecalculateAncestors(context, "B");

        AssertDecimalEqual(0.43944491548m, result["A"]);
        AssertDecimalEqual(0.43944491548m, context.NodesById["A"].LogOdds);
    }

    [TestMethod]
    public void RecalculateAncestors_AppliesRebutDirection()
    {
        var context = GraphCalculationContext.From(
            [Node("A"), Node("B", 1.0m)],
            [Edge("E-B-A", "B", "A", "rebut", 10)]);

        var result = _calculator.RecalculateAncestors(context, "B");

        Assert.AreEqual(-1.0m, result["A"]);
        Assert.AreEqual(-1.0m, context.NodesById["A"].LogOdds);
    }

    [TestMethod]
    public void RecalculateAncestors_UsesAllSiblingsWhenRecalculatingParent()
    {
        var context = GraphCalculationContext.From(
            [
                Node("B"),
                Node("E", 1.0m),
                Node("F", -0.5m)
            ],
            [
                Edge("E-E-B", "E", "B", "support", 10),
                Edge("E-F-B", "F", "B", "support", 10)
            ]);

        var result = _calculator.RecalculateAncestors(context, "F");

        Assert.AreEqual(0.5m, result["B"]);
        Assert.AreEqual(0.5m, context.NodesById["B"].LogOdds);
    }

    [TestMethod]
    public void RecalculateAncestors_PropagatesToAncestors()
    {
        var context = GraphCalculationContext.From(
            [
                Node("A"),
                Node("B"),
                Node("F", 1.0m)
            ],
            [
                Edge("E-F-B", "F", "B", "support", 10),
                Edge("E-B-A", "B", "A", "support", 10)
            ]);

        var result = _calculator.RecalculateAncestors(context, "F");

        Assert.AreEqual(2, result.Count);
        Assert.AreEqual(1.0m, result["B"]);
        Assert.AreEqual(1.0m, result["A"]);
        Assert.AreEqual(1.0m, context.NodesById["B"].LogOdds);
        Assert.AreEqual(1.0m, context.NodesById["A"].LogOdds);
    }

    [TestMethod]
    public void RecalculateAncestors_RecalculatesSharedAncestorAfterBothBranches()
    {
        var context = GraphCalculationContext.From(
            [
                Node("A"),
                Node("B"),
                Node("C"),
                Node("F", 1.0m)
            ],
            [
                Edge("E-F-B", "F", "B", "support", 10),
                Edge("E-F-C", "F", "C", "support", 10),
                Edge("E-B-A", "B", "A", "support", 10),
                Edge("E-C-A", "C", "A", "support", 10)
            ]);

        var result = _calculator.RecalculateAncestors(context, "F");

        Assert.AreEqual(3, result.Count);
        Assert.AreEqual(1.0m, result["B"]);
        Assert.AreEqual(1.0m, result["C"]);
        Assert.AreEqual(2.0m, result["A"]);
        Assert.AreEqual(2.0m, context.NodesById["A"].LogOdds);
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
                Node("F", 1.0m)
            ],
            [
                Edge("E-F-B", "F", "B", "support", 10),
                Edge("E-B-A", "B", "A", "support", 10),
                Edge("E-F-C", "F", "C", "support", 10),
                Edge("E-C-D", "C", "D", "support", 10),
                Edge("E-D-A", "D", "A", "support", 10)
            ]);

        var result = _calculator.RecalculateAncestors(context, "F");

        Assert.AreEqual(4, result.Count);
        Assert.AreEqual(1.0m, result["B"]);
        Assert.AreEqual(1.0m, result["C"]);
        Assert.AreEqual(1.0m, result["D"]);
        Assert.AreEqual(2.0m, result["A"]);
        Assert.AreEqual(2.0m, context.NodesById["A"].LogOdds);
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
                Node("B", 1m)
            ],
            [Edge("E-B-A", "B", "A", "support", 10)]);

        var result = _calculator.RecalculateNodesAndAncestors(context, ["B"]);

        Assert.AreEqual(2, result.Count);
        Assert.AreEqual(0m, result["B"]);
        Assert.AreEqual(0m, result["A"]);
        Assert.AreEqual(0m, context.NodesById["B"].LogOdds);
        Assert.AreEqual(0m, context.NodesById["A"].LogOdds);
    }

    [TestMethod]
    public void RecalculateAncestors_ClampsHighLogOdds()
    {
        var context = GraphCalculationContext.From(
            [Node("A"), Node("B", 125m)],
            [Edge("E-B-A", "B", "A", "support", 10)]);

        var result = _calculator.RecalculateAncestors(context, "B");

        Assert.AreEqual(100m, result["A"]);
        Assert.AreEqual(100m, context.NodesById["A"].LogOdds);
    }

    [TestMethod]
    public void RecalculateAncestors_ClampsLowLogOdds()
    {
        var context = GraphCalculationContext.From(
            [Node("A"), Node("B", 125m)],
            [Edge("E-B-A", "B", "A", "rebut", 10)]);

        var result = _calculator.RecalculateAncestors(context, "B");

        Assert.AreEqual(-100m, result["A"]);
        Assert.AreEqual(-100m, context.NodesById["A"].LogOdds);
    }

    [TestMethod]
    public void RecalculateAncestors_ThrowsForUnknownEdgeKind()
    {
        var context = GraphCalculationContext.From(
            [Node("A"), Node("B", 1m)],
            [Edge("E-B-A", "B", "A", "mystery", 10)]);

        var exception = Assert.ThrowsException<InvalidOperationException>(() =>
            _calculator.RecalculateAncestors(context, "B"));

        StringAssert.Contains(exception.Message, "Unknown edge kind 'mystery'.");
    }

    [TestMethod]
    public void RecalculateAncestors_ThrowsForLegacyCounterEdgeKind()
    {
        var context = GraphCalculationContext.From(
            [Node("A"), Node("B", 1m)],
            [Edge("E-B-A", "B", "A", "counter", 10)]);

        var exception = Assert.ThrowsException<InvalidOperationException>(() =>
            _calculator.RecalculateAncestors(context, "B"));

        StringAssert.Contains(exception.Message, "Unknown edge kind 'counter'.");
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
    public void GetAccumulatedLR_ReturnsTheOnlyPathLikelihoodRatio()
    {
        var context = GraphCalculationContext.From(
            [Node("claim"), Node("evidence", kind: "evidence")],
            [Edge("E-evidence-claim", "evidence", "claim", importanceToParent: 1.5m)]);

        var result = _calculator.GetAccumulatedLR(context, "evidence", "claim");

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

        var result = _calculator.GetAccumulatedLR(context, "evidence", "claim");

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

        var result = _calculator.GetAccumulatedLR(context, "evidence", "claim");

        Assert.IsNotNull(result);
        AssertDecimalEqual(0.1m, result.Value);
    }

    [TestMethod]
    public void GetAccumulatedLR_ReturnsNullWhenNoPathReachesTheClaim()
    {
        var context = GraphCalculationContext.From(
            [Node("claim"), Node("otherClaim"), Node("evidence", kind: "evidence")],
            [Edge("E-evidence-other", "evidence", "otherClaim", importanceToParent: 1.5m)]);

        var result = _calculator.GetAccumulatedLR(context, "evidence", "claim");

        Assert.IsNull(result);
    }

    [TestMethod]
    public void GetAccumulatedLR_ThrowsWhenAnEdgeLikelihoodRatioIsZero()
    {
        var context = GraphCalculationContext.From(
            [Node("claim"), Node("evidence", kind: "evidence")],
            [Edge("E-evidence-claim", "evidence", "claim", importanceToParent: 0m)]);

        var exception = Assert.ThrowsException<InvalidOperationException>(() =>
            _calculator.GetAccumulatedLR(context, "evidence", "claim"));

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

    private static GraphNode Node(string id, decimal logOdds = 0m, string kind = "claim")
    {
        return new GraphNode
        {
            Id = id,
            Kind = kind,
            LogOdds = logOdds
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
}
