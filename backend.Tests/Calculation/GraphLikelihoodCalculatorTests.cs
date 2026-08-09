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
        AssertDecimalEqual(0.43944491548m, result["A"]);
        AssertDecimalEqual(0.43944491548m, context.NodesById["A"].LogOdds);
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
        Assert.AreEqual(-1.0m, result["A"]);
        Assert.AreEqual(-1.0m, context.NodesById["A"].LogOdds);
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
        Assert.AreEqual(1.0m, result["B"]);
        Assert.AreEqual(1.0m, result["A"]);
        Assert.AreEqual(1.0m, context.NodesById["B"].LogOdds);
        Assert.AreEqual(1.0m, context.NodesById["A"].LogOdds);
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
                Node("B", 1m, "evidence")
            ],
            [Edge("E-B-A", "B", "A", "support", 10)]);

        var result = _calculator.RecalculateNodesAndAncestors(context, ["B"]);

        Assert.AreEqual(2, result.Count);
        Assert.AreEqual(1m, result["B"]);
        AssertDecimalEqual(1m + (decimal)Math.Log(10d), result["A"]);
        Assert.AreEqual(1m, context.NodesById["B"].PosteriorOdds);
        AssertDecimalEqual(1m + (decimal)Math.Log(10d), context.NodesById["A"].PosteriorOdds);
        Assert.AreEqual(2m, context.NodesById["A"].PosteriorOdds);
        Assert.AreEqual(0m, result["B"]);
        Assert.AreEqual(0m, result["A"]);
        Assert.AreEqual(0m, context.NodesById["B"].LogOdds);
        Assert.AreEqual(0m, context.NodesById["A"].LogOdds);
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
        Assert.AreEqual(100m, context.NodesById["A"].LogOdds);
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
        Assert.AreEqual(-100m, context.NodesById["A"].LogOdds);
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
