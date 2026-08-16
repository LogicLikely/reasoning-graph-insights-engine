using Backend.Calculation;
using Backend.Models.Domain;

namespace backend.Tests.Calculation;

[TestClass]
public class GraphBayesFactorCalculatorTests
{
    private readonly GraphBayesFactorCalculator _calculator = new();

    [TestMethod]
    public void Calculate_PreservesDecimalPrecisionForLeafBayesFactor()
    {
        const decimal leafBayesFactor = 0.1234567890123456789012345678m;
        var graph = GraphWith([Node("H")], []);

        decimal result = _calculator.Calculate(
            graph,
            "H",
            new Dictionary<string, decimal> { ["H"] = leafBayesFactor });

        Assert.AreEqual(leafBayesFactor, result);
    }

    [TestMethod]
    public void Calculate_ReturnsSuppliedBayesFactorWhenHypothesisIsALeaf()
    {
        var graph = GraphWith([Node("H")], []);

        decimal result = _calculator.Calculate(
            graph,
            "H",
            new Dictionary<string, decimal> { ["H"] = 2.75m });

        Assert.AreEqual(2.75m, result);
    }

    [TestMethod]
    public void Calculate_TransformsLeafBayesFactorThroughOneEdge()
    {
        var graph = GraphWith(
            [Node("H"), Node("E")],
            [Edge("E-E-H", "E", "H", 0.8m, 0.2m)]);

        decimal result = _calculator.Calculate(
            graph,
            "H",
            new Dictionary<string, decimal> { ["E"] = 4m });

        // (4 * .8 + (1 - .8)) / (4 * .2 + (1 - .2)) = 3.4 / 1.6
        AssertDecimalEqual(2.125m, result);
    }

    [TestMethod]
    public void Calculate_MultipliesIndependentChildContributions()
    {
        var graph = GraphWith(
            [Node("H"), Node("E1"), Node("E2")],
            [
                Edge("E-E1-H", "E1", "H", 1m, 0m),
                Edge("E-E2-H", "E2", "H", 0m, 1m)
            ]);

        decimal result = _calculator.Calculate(
            graph,
            "H",
            new Dictionary<string, decimal>
            {
                ["E1"] = 2m,
                ["E2"] = 4m
            });

        // E1 contributes 2; E2 contributes 1 / 4.
        AssertDecimalEqual(0.5m, result);
    }

    [TestMethod]
    public void Calculate_AppliesTheRecurrenceAtEveryLevelOfANestedGraph()
    {
        var graph = GraphWith(
            [Node("H"), Node("M"), Node("E")],
            [
                Edge("E-E-M", "E", "M", 0.75m, 0.25m),
                Edge("E-M-H", "M", "H", 0.8m, 0.2m)
            ]);

        decimal result = _calculator.Calculate(
            graph,
            "H",
            new Dictionary<string, decimal> { ["E"] = 4m });

        decimal expectedAtM = Transform(4m, 0.75m, 0.25m);
        decimal expectedAtH = Transform(expectedAtM, 0.8m, 0.2m);
        AssertDecimalEqual(expectedAtH, result);
    }

    [TestMethod]
    public void Calculate_IgnoresAndDoesNotChangeNodeOdds()
    {
        var hypothesis = Node("H", priorOdds: 123.45m, posteriorOdds: -987.65m);
        var evidence = Node("E", priorOdds: -222.2m, posteriorOdds: 333.3m);
        var graph = GraphWith(
            [hypothesis, evidence],
            [Edge("E-E-H", "E", "H", 0.9m, 0.1m)]);

        decimal result = _calculator.Calculate(
            graph,
            "H",
            new Dictionary<string, decimal> { ["E"] = 3m });

        AssertDecimalEqual(Transform(3m, 0.9m, 0.1m), result);
        Assert.AreEqual(123.45m, hypothesis.PriorOdds);
        Assert.AreEqual(-987.65m, hypothesis.PosteriorOdds);
        Assert.AreEqual(-222.2m, evidence.PriorOdds);
        Assert.AreEqual(333.3m, evidence.PosteriorOdds);
    }

    [TestMethod]
    public void Calculate_ThrowsWhenRelevantLeafBayesFactorIsMissing()
    {
        var graph = GraphWith([Node("H")], []);

        var exception = Assert.ThrowsException<InvalidOperationException>(() =>
            _calculator.Calculate(graph, "H", new Dictionary<string, decimal>()));

        StringAssert.Contains(exception.Message, "No Bayes factor was supplied for leaf node 'H'");
    }

    [TestMethod]
    public void Calculate_ThrowsWhenLeafBayesFactorIsNotPositive()
    {
        foreach (decimal invalidBayesFactor in new[] { 0m, -0.25m })
        {
            var graph = GraphWith([Node("H")], []);

            var exception = Assert.ThrowsException<InvalidOperationException>(() =>
                _calculator.Calculate(
                    graph,
                    "H",
                    new Dictionary<string, decimal> { ["H"] = invalidBayesFactor }));

            StringAssert.Contains(exception.Message, "Bayes factors must be greater than zero");
        }
    }

    [TestMethod]
    public void Calculate_ThrowsWhenAnEdgeProbabilityIsOutsideZeroAndOne()
    {
        var invalidProbabilities = new[]
        {
            (GivenParent: -0.1m, GivenNotParent: 0.5m),
            (GivenParent: 1.1m, GivenNotParent: 0.5m),
            (GivenParent: 0.5m, GivenNotParent: -0.1m),
            (GivenParent: 0.5m, GivenNotParent: 1.1m)
        };

        foreach (var probabilities in invalidProbabilities)
        {
            var graph = GraphWith(
                [Node("H"), Node("E")],
                [Edge(
                    "E-E-H",
                    "E",
                    "H",
                    probabilities.GivenParent,
                    probabilities.GivenNotParent)]);

            var exception = Assert.ThrowsException<InvalidOperationException>(() =>
                _calculator.Calculate(
                    graph,
                    "H",
                    new Dictionary<string, decimal> { ["E"] = 2m }));

            StringAssert.Contains(exception.Message, "Edge 'E-E-H'");
            StringAssert.Contains(exception.Message, "between zero and one");
        }
    }

    [TestMethod]
    public void Calculate_RejectsAnUnprunedNodeWithMultipleContinuations()
    {
        var graph = GraphWith(
            [Node("H"), Node("A"), Node("B"), Node("E")],
            [
                Edge("E-E-A", "E", "A", 0.8m, 0.2m),
                Edge("E-E-B", "E", "B", 0.7m, 0.3m),
                Edge("E-A-H", "A", "H", 0.8m, 0.2m),
                Edge("E-B-H", "B", "H", 0.7m, 0.3m)
            ]);

        var exception = Assert.ThrowsException<InvalidOperationException>(() =>
            _calculator.Calculate(
                graph,
                "H",
                new Dictionary<string, decimal> { ["E"] = 2m }));

        StringAssert.Contains(exception.Message, "Node 'E' has 2 continuations");
        StringAssert.Contains(exception.Message, "Prune the graph");
    }

    [TestMethod]
    public void Calculate_ThrowsForCycleThatCanReachTheHypothesis()
    {
        var graph = GraphWith(
            [Node("H"), Node("A"), Node("B"), Node("E")],
            [
                Edge("E-E-A", "E", "A", 0.8m, 0.2m),
                Edge("E-A-B", "A", "B", 0.8m, 0.2m),
                Edge("E-B-A", "B", "A", 0.8m, 0.2m),
                Edge("E-B-H", "B", "H", 0.8m, 0.2m)
            ]);

        var exception = Assert.ThrowsException<InvalidOperationException>(() =>
            _calculator.Calculate(
                graph,
                "H",
                new Dictionary<string, decimal> { ["E"] = 2m }));

        StringAssert.Contains(exception.Message, "Cycle detected");
    }

    [TestMethod]
    public void Calculate_ThrowsWhenHypothesisDoesNotExist()
    {
        var graph = GraphWith([Node("H")], []);

        var exception = Assert.ThrowsException<InvalidOperationException>(() =>
            _calculator.Calculate(
                graph,
                "missing",
                new Dictionary<string, decimal>()));

        StringAssert.Contains(exception.Message, "Hypothesis node 'missing'");
    }

    [TestMethod]
    public void Calculate_ThrowsWhenCancellationWasRequested()
    {
        var graph = GraphWith([Node("H")], []);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.ThrowsException<OperationCanceledException>(() =>
            _calculator.Calculate(
                graph,
                "H",
                new Dictionary<string, decimal> { ["H"] = 2m },
                cancellation.Token));
    }

    private static Graph GraphWith(
        IEnumerable<GraphNode> nodes,
        IEnumerable<GraphEdge> edges)
    {
        return new Graph
        {
            Nodes = nodes.ToList(),
            Edges = edges.ToList()
        };
    }

    private static GraphNode Node(
        string id,
        decimal priorOdds = 0m,
        decimal posteriorOdds = 0m)
    {
        return new GraphNode
        {
            Id = id,
            Kind = "claim",
            PriorOdds = priorOdds,
            PosteriorOdds = posteriorOdds
        };
    }

    private static GraphEdge Edge(
        string id,
        string from,
        string to,
        decimal probabilityGivenParent,
        decimal probabilityGivenNotParent)
    {
        return new GraphEdge
        {
            Id = id,
            From = from,
            To = to,
            Kind = "support",
            ImportanceToParent = 1m,
            ProbabilityGivenParent = probabilityGivenParent,
            ProbabilityGivenNotParent = probabilityGivenNotParent
        };
    }

    private static decimal Transform(
        decimal childBayesFactor,
        decimal probabilityGivenParent,
        decimal probabilityGivenNotParent)
    {
        return (
            childBayesFactor * probabilityGivenParent +
            (1m - probabilityGivenParent)) /
            (
                childBayesFactor * probabilityGivenNotParent +
                (1m - probabilityGivenNotParent));
    }

    private static void AssertDecimalEqual(
        decimal expected,
        decimal actual,
        decimal tolerance = 0.000000000001m)
    {
        Assert.IsTrue(
            Math.Abs(expected - actual) <= tolerance,
            $"Expected {actual} to be within {tolerance} of {expected}.");
    }
}
