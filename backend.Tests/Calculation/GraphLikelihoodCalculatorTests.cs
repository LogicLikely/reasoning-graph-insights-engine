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
        int importanceToParent = 10)
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
