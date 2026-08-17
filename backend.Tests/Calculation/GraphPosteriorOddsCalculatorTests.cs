using Backend.Calculation;
using Backend.Models.Domain;

namespace backend.Tests.Calculation;

[TestClass]
public class GraphPosteriorOddsCalculatorTests
{
    private readonly GraphPosteriorOddsCalculator _calculator = new();

    [TestMethod]
    public void CalculateNodeLogPosteriorOdds_TransformsSupportingEvidenceThroughConditionalProbabilities()
    {
        decimal evidenceLogBayesFactor = Log(4m);
        var graph = GraphWith(
            [
                Node("H", priorOdds: 0.4m, posteriorOdds: -12m),
                Node(
                    "E",
                    kind: "evidence",
                    priorOdds: -1m,
                    posteriorOdds: -1m + evidenceLogBayesFactor)
            ],
            [Edge("E-E-H", "E", "H", a: 0.8m, b: 0.2m)]);

        decimal result = _calculator.CalculateNodeLogPosteriorOdds(graph, "H");

        decimal expected = 0.4m + Log(Transform(4m, 0.8m, 0.2m));
        AssertDecimalEqual(expected, result);
    }

    [TestMethod]
    public void CalculateNodeLogPosteriorOdds_TransformsCounterEvidenceThroughConditionalProbabilities()
    {
        decimal evidenceLogBayesFactor = Log(4m);
        var graph = GraphWith(
            [
                Node("H", priorOdds: 0.4m),
                Node(
                    "E",
                    kind: "objection",
                    priorOdds: 3m,
                    posteriorOdds: 3m + evidenceLogBayesFactor)
            ],
            [Edge(
                "E-E-H",
                "E",
                "H",
                a: 0.2m,
                b: 0.8m,
                kind: "counter")]);

        decimal result = _calculator.CalculateNodeLogPosteriorOdds(graph, "H");

        decimal expected = 0.4m + Log(Transform(4m, 0.2m, 0.8m));
        AssertDecimalEqual(expected, result);
        Assert.IsTrue(result < 0.4m, "Counter evidence should lower the hypothesis log odds.");
    }

    [TestMethod]
    public void CalculateNodeLogPosteriorOdds_MultipliesTwoIndependentChildContributions()
    {
        var graph = GraphWith(
            [
                Node("H", priorOdds: 0.25m),
                EvidenceWithLogBayesFactor("E1", Log(2m)),
                EvidenceWithLogBayesFactor("E2", Log(4m), kind: "objection")
            ],
            [
                Edge("E-E1-H", "E1", "H", a: 0.999999999m, b: 0.000000001m),
                Edge("E-E2-H", "E2", "H", a: 0.000000001m, b: 0.999999999m, kind: "counter")
            ]);

        decimal result = _calculator.CalculateNodeLogPosteriorOdds(graph, "H");

        decimal expectedBayesFactor =
            Transform(2m, 0.999999999m, 0.000000001m) *
            Transform(4m, 0.000000001m, 0.999999999m);
        AssertDecimalEqual(0.25m + Log(expectedBayesFactor), result);
    }

    [TestMethod]
    public void CalculateNodeLogPosteriorOdds_AppliesNestedRecurrenceWithoutUsingInternalNodeOddsAsEvidence()
    {
        var graph = GraphWith(
            [
                Node("H", priorOdds: 0.4m),
                Node("M", priorOdds: 80m, posteriorOdds: -80m),
                EvidenceWithLogBayesFactor("E", Log(4m))
            ],
            [
                Edge("E-E-M", "E", "M", a: 0.75m, b: 0.25m),
                Edge("E-M-H", "M", "H", a: 0.8m, b: 0.2m)
            ]);

        decimal result = _calculator.CalculateNodeLogPosteriorOdds(graph, "H");

        decimal bayesFactorAtM = Transform(4m, 0.75m, 0.25m);
        decimal bayesFactorAtH = Transform(bayesFactorAtM, 0.8m, 0.2m);
        AssertDecimalEqual(0.4m + Log(bayesFactorAtH), result);
    }

    [TestMethod]
    public void CalculateNodeLogPosteriorOdds_ReturnsPriorForNonEvidenceNodeWithNoDownstreamEvidence()
    {
        var graph = GraphWith(
            [
                Node("H", priorOdds: 1.25m, posteriorOdds: -40m),
                Node("C", priorOdds: 7m, posteriorOdds: -7m)
            ],
            [Edge("E-C-H", "C", "H", a: 0.999999999m, b: 0.000000001m)]);

        decimal result = _calculator.CalculateNodeLogPosteriorOdds(graph, "H");

        Assert.AreEqual(1.25m, result);
    }

    [TestMethod]
    public void CalculateNodeLogPosteriorOdds_DependsOnLeafOddsDifferenceNotAbsoluteValues()
    {
        var firstGraph = GraphWith(
            [
                Node("H", priorOdds: 0.3m),
                Node("E", kind: "evidence", priorOdds: -10m, posteriorOdds: -9m)
            ],
            [Edge("E-E-H", "E", "H", a: 0.8m, b: 0.2m)]);
        var secondGraph = GraphWith(
            [
                Node("H", priorOdds: 0.3m),
                Node("E", kind: "evidence", priorOdds: 50m, posteriorOdds: 51m)
            ],
            [Edge("E-E-H", "E", "H", a: 0.8m, b: 0.2m)]);

        decimal first = _calculator.CalculateNodeLogPosteriorOdds(firstGraph, "H");
        decimal second = _calculator.CalculateNodeLogPosteriorOdds(secondGraph, "H");

        AssertDecimalEqual(first, second);
    }

    [TestMethod]
    public void RecalculateNodesAndAncestors_PreservesLeafEvidencePosteriorAndRecalculatesItsParent()
    {
        decimal evidencePosterior = 2.25m;
        var evidence = Node(
            "E",
            kind: "evidence",
            priorOdds: -7m,
            posteriorOdds: evidencePosterior);
        var hypothesis = Node("H", priorOdds: 0.5m, posteriorOdds: -20m);
        var graph = GraphWith(
            [hypothesis, evidence],
            [Edge("E-E-H", "E", "H", a: 0.8m, b: 0.2m)]);

        decimal calculatedLeaf = _calculator.CalculateNodeLogPosteriorOdds(graph, "E");
        var result = _calculator.RecalculateNodesAndAncestors(graph, ["E"]);

        decimal leafBayesFactor = Exp(evidencePosterior - evidence.PriorOdds);
        decimal expectedHypothesis = 0.5m + Log(Transform(leafBayesFactor, 0.8m, 0.2m));
        Assert.AreEqual(evidencePosterior, calculatedLeaf);
        Assert.AreEqual(evidencePosterior, result["E"]);
        Assert.AreEqual(evidencePosterior, evidence.PosteriorOdds);
        AssertDecimalEqual(expectedHypothesis, result["H"]);
        AssertDecimalEqual(expectedHypothesis, hypothesis.PosteriorOdds);
    }

    [TestMethod]
    public void RecalculateAncestors_IsIdempotentAndDoesNotChangeTheEvidenceLeaf()
    {
        decimal evidencePosterior = 1.5m;
        var evidence = Node("E", kind: "evidence", priorOdds: 0.5m, posteriorOdds: evidencePosterior);
        var middle = Node("M", priorOdds: -0.2m, posteriorOdds: 60m);
        var hypothesis = Node("H", priorOdds: 0.3m, posteriorOdds: -60m);
        var graph = GraphWith(
            [hypothesis, middle, evidence],
            [
                Edge("E-E-M", "E", "M", a: 0.7m, b: 0.3m),
                Edge("E-M-H", "M", "H", a: 0.9m, b: 0.1m)
            ]);

        var first = _calculator.RecalculateAncestors(graph, "E");
        decimal firstMiddle = middle.PosteriorOdds;
        decimal firstHypothesis = hypothesis.PosteriorOdds;
        var second = _calculator.RecalculateAncestors(graph, "E");

        Assert.AreEqual(2, first.Count);
        Assert.AreEqual(2, second.Count);
        Assert.AreEqual(evidencePosterior, evidence.PosteriorOdds);
        AssertDecimalEqual(firstMiddle, second["M"]);
        AssertDecimalEqual(firstHypothesis, second["H"]);
        AssertDecimalEqual(firstMiddle, middle.PosteriorOdds);
        AssertDecimalEqual(firstHypothesis, hypothesis.PosteriorOdds);
    }

    [TestMethod]
    public void CalculateNodeLogPosteriorOdds_PrunesCompetingSuffixAfterEvidencePathsMerge()
    {
        var graph = GraphWith(
            [
                Node("H"),
                Node("A"),
                Node("B"),
                Node("M"),
                EvidenceWithLogBayesFactor("E1", Log(2m)),
                EvidenceWithLogBayesFactor("E2", Log(3m))
            ],
            [
                Edge("E-E1-M", "E1", "M", a: 0.999999999m, b: 0.000000001m),
                Edge("E-E2-M", "E2", "M", a: 0.999999999m, b: 0.000000001m),
                Edge("E-M-A", "M", "A", a: 0.9m, b: 0.1m),
                Edge("E-A-H", "A", "H", a: 0.8m, b: 0.2m),
                Edge("E-M-B", "M", "B", a: 0.51m, b: 0.49m),
                Edge("E-B-H", "B", "H", a: 0.51m, b: 0.49m)
            ]);

        decimal result = _calculator.CalculateNodeLogPosteriorOdds(graph, "H");

        // The derived ratios select M -> A -> H. The two leaf contributions
        // combine at M, then the shared nonlinear suffix is applied once.
        decimal expectedAtM =
            Transform(2m, 0.999999999m, 0.000000001m) *
            Transform(3m, 0.999999999m, 0.000000001m);
        decimal expectedBayesFactor = Transform(
            Transform(expectedAtM, 0.9m, 0.1m),
            0.8m,
            0.2m);
        AssertDecimalEqual(Log(expectedBayesFactor), result);
    }

    [TestMethod]
    public void CalculateNodeLogPosteriorOdds_ClampsResultsToSupportedLogOddsRange()
    {
        var highGraph = GraphWith(
            [Node("H", priorOdds: 99m), EvidenceWithLogBayesFactor("E", Log(4m))],
            [Edge("E-E-H", "E", "H", a: 0.999999999m, b: 0.000000001m)]);
        var lowGraph = GraphWith(
            [Node("H", priorOdds: -99m), EvidenceWithLogBayesFactor("E", Log(4m), kind: "objection")],
            [Edge("E-E-H", "E", "H", a: 0.000000001m, b: 0.999999999m, kind: "counter")]);

        decimal high = _calculator.CalculateNodeLogPosteriorOdds(highGraph, "H");
        decimal low = _calculator.CalculateNodeLogPosteriorOdds(lowGraph, "H");

        Assert.AreEqual(100m, high);
        Assert.AreEqual(-100m, low);
    }

    [TestMethod]
    public void CalculateNodeLogPosteriorOdds_HandlesExtremeLeafLogBayesFactorWithoutExponentiatingIt()
    {
        var graph = GraphWith(
            [
                Node("H"),
                Node(
                    "E",
                    kind: "evidence",
                    priorOdds: -100m,
                    posteriorOdds: 100m)
            ],
            [Edge("E-E-H", "E", "H", a: 0.999999999m, b: 0.000000001m)]);

        decimal result = _calculator.CalculateNodeLogPosteriorOdds(graph, "H");

        AssertDecimalEqual(Log(0.999999999m / 0.000000001m), result);
    }

    [TestMethod]
    public void CalculateNodeLogPosteriorOdds_ThrowsForCycleThatCanReachHypothesis()
    {
        var graph = GraphWith(
            [Node("H"), Node("A"), Node("B"), EvidenceWithLogBayesFactor("E", 1m)],
            [
                Edge("E-E-A", "E", "A"),
                Edge("E-A-B", "A", "B"),
                Edge("E-B-A", "B", "A"),
                Edge("E-B-H", "B", "H")
            ]);

        var exception = Assert.ThrowsException<InvalidOperationException>(() =>
            _calculator.CalculateNodeLogPosteriorOdds(graph, "H"));

        StringAssert.Contains(exception.Message, "Cycle detected");
    }

    [TestMethod]
    public void CalculateNodeLogPosteriorOdds_ThrowsWhenTargetDoesNotExist()
    {
        var graph = GraphWith([Node("H")], []);

        var exception = Assert.ThrowsException<InvalidOperationException>(() =>
            _calculator.CalculateNodeLogPosteriorOdds(graph, "missing"));

        StringAssert.Contains(exception.Message, "missing");
    }

    [TestMethod]
    public void RecalculateAncestors_ThrowsWhenChangedNodeDoesNotExist()
    {
        var graph = GraphWith([Node("H")], []);

        var exception = Assert.ThrowsException<InvalidOperationException>(() =>
            _calculator.RecalculateAncestors(graph, "missing"));

        StringAssert.Contains(exception.Message, "missing");
    }

    [TestMethod]
    public void CalculateNodeLogPosteriorOdds_ThrowsWhenCancellationWasRequested()
    {
        var graph = GraphWith([Node("H")], []);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.ThrowsException<OperationCanceledException>(() =>
            _calculator.CalculateNodeLogPosteriorOdds(graph, "H", cancellation.Token));
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
        string kind = "claim",
        decimal priorOdds = 0m,
        decimal posteriorOdds = 0m)
    {
        return new GraphNode
        {
            Id = id,
            Kind = kind,
            PriorOdds = priorOdds,
            PosteriorOdds = posteriorOdds
        };
    }

    private static GraphNode EvidenceWithLogBayesFactor(
        string id,
        decimal logBayesFactor,
        string kind = "evidence")
    {
        const decimal priorOdds = -0.75m;
        return Node(
            id,
            kind,
            priorOdds,
            priorOdds + logBayesFactor);
    }

    private static GraphEdge Edge(
        string id,
        string from,
        string to,
        decimal a = 0.5m,
        decimal b = 0.5m,
        string kind = "support")
    {
        return new GraphEdge
        {
            Id = id,
            From = from,
            To = to,
            Kind = kind,
            ProbabilityGivenParent = a,
            ProbabilityGivenNotParent = b
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

    private static decimal Exp(decimal value) =>
        (decimal)Math.Exp((double)value);

    private static decimal Log(decimal value) =>
        (decimal)Math.Log((double)value);

    private static void AssertDecimalEqual(
        decimal expected,
        decimal actual,
        decimal tolerance = 0.000000001m)
    {
        Assert.IsTrue(
            Math.Abs(expected - actual) <= tolerance,
            $"Expected {actual} to be within {tolerance} of {expected}.");
    }
}
