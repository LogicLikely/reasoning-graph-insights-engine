using Backend.Calculation;
using Backend.Models.Domain;

namespace backend.Tests.Calculation;

[TestClass]
public class GraphBayesFactorPrunerTests
{
    private readonly GraphBayesFactorPruner _pruner = new();

    [TestMethod]
    public void Prune_SelectsEvidencePathFarthestFromNeutral()
    {
        var graph = GraphWith(
            [
                Node("H"),
                Node("E", "evidence"),
                Node("support-path"),
                Node("counter-path")
            ],
            [
                Edge("E-E-support", "E", "support-path", 2m),
                Edge("E-support-H", "support-path", "H", 2m),
                Edge("E-E-counter", "E", "counter-path", 0.1m),
                Edge("E-counter-H", "counter-path", "H", 0.5m)
            ]);

        var result = _pruner.Prune(graph, "H");

        AssertGraphContains(
            result,
            ["E", "counter-path", "H"],
            ["E-E-counter", "E-counter-H"]);
    }

    [TestMethod]
    public void Prune_ChoosesMaximumPathWhenReciprocalPathsHaveEqualStrength()
    {
        var graph = GraphWith(
            [
                Node("H"),
                Node("E", "evidence"),
                Node("support-path"),
                Node("counter-path")
            ],
            [
                Edge("E-E-support", "E", "support-path", 1m),
                Edge("E-support-H", "support-path", "H", 4m),
                Edge("E-E-counter", "E", "counter-path", 1m),
                Edge("E-counter-H", "counter-path", "H", 0.25m)
            ]);

        var result = _pruner.Prune(graph, "H");

        AssertGraphContains(
            result,
            ["E", "support-path", "H"],
            ["E-E-support", "E-support-H"]);
    }

    [TestMethod]
    public void Prune_PreservesConflictingMergePrefixesAndSelectsOneContinuation()
    {
        var graph = GraphWith(
            [
                Node("H"),
                Node("support-evidence", "evidence"),
                Node("counter-evidence", "evidence"),
                Node("merge"),
                Node("support-suffix"),
                Node("counter-suffix")
            ],
            [
                Edge("E-support-merge", "support-evidence", "merge", 10m),
                Edge("E-counter-merge", "counter-evidence", "merge", 0.01m),
                Edge("E-merge-support", "merge", "support-suffix", 8m),
                Edge("E-support-H", "support-suffix", "H", 1m),
                Edge("E-merge-counter", "merge", "counter-suffix", 0.5m),
                Edge("E-counter-H", "counter-suffix", "H", 1m)
            ]);

        var result = _pruner.Prune(graph, "H");

        AssertGraphContains(
            result,
            ["support-evidence", "counter-evidence", "merge", "support-suffix", "H"],
            ["E-support-merge", "E-counter-merge", "E-merge-support", "E-support-H"]);
        Assert.AreEqual(
            1,
            result.Edges.Count(edge => edge.From == "merge"),
            "A merge node must have one compatible continuation toward the hypothesis.");
    }

    [TestMethod]
    public void Prune_ResolvesNestedMergesFromFarthestToNearest()
    {
        var graph = GraphWith(
            [
                Node("H"),
                Node("E1", "evidence"),
                Node("E2", "evidence"),
                Node("E3", "evidence"),
                Node("far-merge"),
                Node("near-merge"),
                Node("far-counter-suffix"),
                Node("near-support-suffix"),
                Node("near-counter-suffix")
            ],
            [
                Edge("E-E1-far", "E1", "far-merge", 10m),
                Edge("E-E2-far", "E2", "far-merge", 0.01m),
                Edge("E-far-near", "far-merge", "near-merge", 8m),
                Edge("E-far-counter", "far-merge", "far-counter-suffix", 0.5m),
                Edge("E-far-counter-H", "far-counter-suffix", "H", 1m),
                Edge("E-E3-near", "E3", "near-merge", 0.01m),
                Edge("E-near-support", "near-merge", "near-support-suffix", 5m),
                Edge("E-near-support-H", "near-support-suffix", "H", 1m),
                Edge("E-near-counter", "near-merge", "near-counter-suffix", 0.5m),
                Edge("E-near-counter-H", "near-counter-suffix", "H", 1m)
            ]);

        var result = _pruner.Prune(graph, "H");

        AssertGraphContains(
            result,
            ["E1", "E2", "E3", "far-merge", "near-merge", "near-support-suffix", "H"],
            [
                "E-E1-far",
                "E-E2-far",
                "E-far-near",
                "E-E3-near",
                "E-near-support",
                "E-near-support-H"
            ]);
        Assert.IsTrue(
            result.Edges
                .GroupBy(edge => edge.From, StringComparer.Ordinal)
                .All(group => group.Count() == 1),
            "Every retained node must have at most one continuation toward the hypothesis.");
    }

    [TestMethod]
    public void Prune_HandlesOverlapIntroducedByResolvingAFarMerge()
    {
        var graph = GraphWith(
            [
                Node("H"),
                Node("E1", "evidence"),
                Node("E2", "evidence"),
                Node("E3", "evidence"),
                Node("far-merge"),
                Node("previous-suffix"),
                Node("introduced-merge")
            ],
            [
                Edge("E-E1-far", "E1", "far-merge", 0.0001m),
                Edge("E-E2-far", "E2", "far-merge", 0.001m),
                Edge("E-far-previous", "far-merge", "previous-suffix", 0.2m),
                Edge("E-previous-H", "previous-suffix", "H", 1m),
                Edge("E-far-introduced", "far-merge", "introduced-merge", 8m),
                Edge("E-E3-introduced", "E3", "introduced-merge", 2m),
                Edge("E-introduced-H", "introduced-merge", "H", 1m)
            ]);

        var result = _pruner.Prune(graph, "H");

        AssertGraphContains(
            result,
            ["E1", "E2", "E3", "far-merge", "introduced-merge", "H"],
            [
                "E-E1-far",
                "E-E2-far",
                "E-far-introduced",
                "E-E3-introduced",
                "E-introduced-H"
            ]);
        Assert.IsFalse(result.Nodes.Any(node => node.Id == "previous-suffix"));
        Assert.IsTrue(
            result.Edges
                .GroupBy(edge => edge.From, StringComparer.Ordinal)
                .All(group => group.Count() == 1));
    }

    [TestMethod]
    public void Prune_RetainsTheSelectedParallelEdgeById()
    {
        var graph = GraphWith(
            [Node("H"), Node("E", "evidence")],
            [
                Edge("weak-edge", "E", "H", 2m),
                Edge("strong-edge", "E", "H", 4m)
            ]);

        var result = _pruner.Prune(graph, "H");

        AssertGraphContains(result, ["E", "H"], ["strong-edge"]);
    }

    [TestMethod]
    public void Prune_BreaksEqualPathTiesByOrdinalEdgeId()
    {
        var graph = GraphWith(
            [Node("H"), Node("E", "evidence")],
            [
                Edge("z-edge", "E", "H", 4m),
                Edge("a-edge", "E", "H", 4m)
            ]);

        var result = _pruner.Prune(graph, "H");

        AssertGraphContains(result, ["E", "H"], ["a-edge"]);
    }

    [TestMethod]
    public void Prune_SelectsTheSameSubgraphRegardlessOfInputOrder()
    {
        var graph = GraphWith(
            [Node("H"), Node("E", "evidence"), Node("A"), Node("B")],
            [
                Edge("z-E-A", "E", "A", 2m),
                Edge("z-A-H", "A", "H", 2m),
                Edge("a-E-B", "E", "B", 2m),
                Edge("a-B-H", "B", "H", 2m)
            ]);
        var reversedGraph = GraphWith(
            graph.Nodes.AsEnumerable().Reverse(),
            graph.Edges.AsEnumerable().Reverse());

        var firstResult = _pruner.Prune(graph, "H");
        var reversedResult = _pruner.Prune(reversedGraph, "H");

        CollectionAssert.AreEquivalent(
            firstResult.Nodes.Select(node => node.Id).ToArray(),
            reversedResult.Nodes.Select(node => node.Id).ToArray());
        CollectionAssert.AreEquivalent(
            firstResult.Edges.Select(edge => edge.Id).ToArray(),
            reversedResult.Edges.Select(edge => edge.Id).ToArray());
        AssertGraphContains(firstResult, ["E", "B", "H"], ["a-E-B", "a-B-H"]);
    }

    [TestMethod]
    public void Prune_ReturnsOnlyTheHypothesisWhenNoEvidenceReachesIt()
    {
        var graph = GraphWith(
            [Node("H"), Node("claim"), Node("unrelated-evidence", "evidence")],
            [
                Edge("E-claim-H", "claim", "H", 2m),
                Edge("E-unrelated-claim", "unrelated-evidence", "other", 2m)
            ]);
        graph.Nodes.Add(Node("other"));

        var result = _pruner.Prune(graph, "H");

        AssertGraphContains(result, ["H"], []);
    }

    [TestMethod]
    public void Prune_ExcludesDisconnectedEvidenceAndNodesAboveTheHypothesis()
    {
        var graph = GraphWith(
            [
                Node("H"),
                Node("reachable-evidence", "evidence"),
                Node("premise"),
                Node("above-H"),
                Node("disconnected-evidence", "evidence"),
                Node("other-root")
            ],
            [
                Edge("E-evidence-premise", "reachable-evidence", "premise", 2m),
                Edge("E-premise-H", "premise", "H", 3m),
                Edge("E-H-above", "H", "above-H", 100m),
                Edge("E-disconnected-other", "disconnected-evidence", "other-root", 100m)
            ]);

        var result = _pruner.Prune(graph, "H");

        AssertGraphContains(
            result,
            ["reachable-evidence", "premise", "H"],
            ["E-evidence-premise", "E-premise-H"]);
    }

    [TestMethod]
    public void Prune_IncludesObjectionNodesCaseInsensitively()
    {
        var graph = GraphWith(
            [Node("H"), Node("O", "ObJeCtIoN")],
            [Edge("E-O-H", "O", "H", 0.2m)]);

        var result = _pruner.Prune(graph, "H");

        AssertGraphContains(result, ["O", "H"], ["E-O-H"]);
    }

    [TestMethod]
    public void Prune_DeepClonesTheResultAndDoesNotMutateTheInput()
    {
        var hypothesis = Node("H");
        hypothesis.Title = "Hypothesis";
        hypothesis.BodyText = "Original hypothesis body";
        hypothesis.Category = "original-category";
        hypothesis.Tags = ["hypothesis-tag"];
        hypothesis.PriorOdds = -0.3m;
        hypothesis.PosteriorOdds = 0.8m;

        var evidence = Node("E", "evidence");
        evidence.Title = "Evidence";
        evidence.BodyText = "Original evidence body";
        evidence.Tags = ["source", "primary"];
        evidence.PriorOdds = -1.2m;
        evidence.PosteriorOdds = 1.7m;
        evidence.Evidence = new GraphEvidenceDetails
        {
            Type = "observational",
            Score = 91m,
            Rationale = "Original rationale"
        };

        var sourceEdge = Edge("E-E-H", "E", "H", 3m, "support");
        sourceEdge.ProbabilityGivenParent = 0.76m;
        sourceEdge.ProbabilityGivenNotParent = 0.24m;
        var graph = GraphWith([hypothesis, evidence], [sourceEdge]);
        graph.Id = 42;
        graph.Slug = "source-graph";
        graph.Title = "Source graph";
        graph.Description = "Source description";

        var result = _pruner.Prune(graph, "H");

        Assert.AreNotSame(graph, result);
        Assert.AreNotSame(graph.Nodes, result.Nodes);
        Assert.AreNotSame(graph.Edges, result.Edges);
        Assert.AreEqual(graph.Id, result.Id);
        Assert.AreEqual(graph.Slug, result.Slug);
        Assert.AreEqual(graph.Title, result.Title);
        Assert.AreEqual(graph.Description, result.Description);

        var clonedEvidence = result.Nodes.Single(node => node.Id == "E");
        var clonedEdge = result.Edges.Single();
        Assert.AreNotSame(evidence, clonedEvidence);
        Assert.AreNotSame(evidence.Tags, clonedEvidence.Tags);
        Assert.IsNotNull(clonedEvidence.Evidence);
        Assert.AreNotSame(evidence.Evidence, clonedEvidence.Evidence);
        Assert.AreNotSame(sourceEdge, clonedEdge);
        Assert.AreEqual(evidence.PriorOdds, clonedEvidence.PriorOdds);
        Assert.AreEqual(evidence.PosteriorOdds, clonedEvidence.PosteriorOdds);
        Assert.AreEqual(0.76m, clonedEdge.ProbabilityGivenParent);
        Assert.AreEqual(0.24m, clonedEdge.ProbabilityGivenNotParent);

        clonedEvidence.Title = "Changed title";
        clonedEvidence.Tags.Add("changed-tag");
        clonedEvidence.Evidence!.Rationale = "Changed rationale";
        clonedEdge.ProbabilityGivenParent = 0.1m;
        clonedEdge.ProbabilityGivenNotParent = 0.9m;
        result.Nodes.Clear();
        result.Edges.Clear();

        Assert.AreEqual("Evidence", evidence.Title);
        CollectionAssert.AreEqual(new[] { "source", "primary" }, evidence.Tags);
        Assert.AreEqual("Original rationale", evidence.Evidence!.Rationale);
        Assert.AreEqual(0.76m, sourceEdge.ProbabilityGivenParent);
        Assert.AreEqual(0.24m, sourceEdge.ProbabilityGivenNotParent);
        Assert.AreEqual(2, graph.Nodes.Count);
        Assert.AreEqual(1, graph.Edges.Count);
        Assert.AreEqual(-1.2m, evidence.PriorOdds);
        Assert.AreEqual(1.7m, evidence.PosteriorOdds);
    }

    [TestMethod]
    public void Prune_ThrowsWhenHypothesisDoesNotExist()
    {
        var graph = GraphWith([Node("H")], []);

        var exception = Assert.ThrowsException<InvalidOperationException>(() =>
            _pruner.Prune(graph, "missing"));

        StringAssert.Contains(exception.Message, "Hypothesis node 'missing'");
    }

    [TestMethod]
    public void Prune_ThrowsForCycleThatCanReachTheHypothesis()
    {
        var graph = GraphWith(
            [Node("H"), Node("E", "evidence"), Node("A"), Node("B")],
            [
                Edge("E-E-A", "E", "A", 2m),
                Edge("E-A-B", "A", "B", 2m),
                Edge("E-B-A", "B", "A", 2m),
                Edge("E-B-H", "B", "H", 2m)
            ]);

        var exception = Assert.ThrowsException<InvalidOperationException>(() =>
            _pruner.Prune(graph, "H"));

        StringAssert.Contains(exception.Message, "Cycle detected");
    }

    [TestMethod]
    public void Prune_ThrowsForProbabilityThatCannotDefineFiniteLikelihoodRatio()
    {
        foreach (decimal probability in new[] { 0m, -1m })
        {
            var graph = GraphWith(
                [Node("H"), Node("E", "evidence")],
                [new GraphEdge
                {
                    Id = "E-E-H",
                    From = "E",
                    To = "H",
                    Kind = "support",
                    ProbabilityGivenParent = probability,
                    ProbabilityGivenNotParent = 1m
                }]);

            var exception = Assert.ThrowsException<InvalidOperationException>(() =>
                _pruner.Prune(graph, "H"));

            StringAssert.Contains(exception.Message, "range (0, 1]");
        }
    }

    [TestMethod]
    public void Prune_ThrowsWhenCancellationWasRequested()
    {
        var graph = GraphWith([Node("H")], []);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.ThrowsException<OperationCanceledException>(() =>
            _pruner.Prune(graph, "H", cancellation.Token));
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

    private static GraphNode Node(string id, string kind = "claim")
    {
        return new GraphNode
        {
            Id = id,
            Kind = kind
        };
    }

    private static GraphEdge Edge(
        string id,
        string from,
        string to,
        decimal likelihoodRatio,
        string kind = "support")
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

    private static void AssertGraphContains(
        Graph graph,
        string[] expectedNodeIds,
        string[] expectedEdgeIds)
    {
        CollectionAssert.AreEquivalent(
            expectedNodeIds,
            graph.Nodes.Select(node => node.Id).ToArray());
        CollectionAssert.AreEquivalent(
            expectedEdgeIds,
            graph.Edges.Select(edge => edge.Id).ToArray());
    }
}
