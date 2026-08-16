using Backend.Calculation;
using Backend.Insights.Analysis;
using Backend.Insights.Contracts;
using Backend.Models.Domain;

namespace Backend.Tests.Insights.Analysis;

[TestClass]
public sealed class StrongestPathV1AnalysisTests
{
    private readonly StrongestPathV1Analysis _analysis = new();

    [TestMethod]
    public void Analyze_TraversesBothDirectionsAndReconstructsSharedDagPaths()
    {
        var graph = GraphWith(
            [Node("root"), Node("left"), Node("right"), Node("leaf")],
            [
                Edge("edge-leaf-left", "leaf", "left", 2m),
                Edge("edge-leaf-right", "leaf", "right", 0.5m),
                Edge("edge-left-root", "left", "root", 3m),
                Edge("edge-right-root", "right", "root", 3m)
            ]);

        var downstream = _analysis.Analyze(graph, "root", PathDirection.Down);
        var upstream = _analysis.Analyze(graph, "leaf", PathDirection.Up);

        Assert.AreEqual(4, downstream.TotalResultCardinality);
        Assert.AreEqual(4, upstream.TotalResultCardinality);
        AssertPath(
            downstream.Items.Single(item => item.EndNodeId == "leaf"),
            ["root", "left", "leaf"],
            ["edge-left-root", "edge-leaf-left"],
            Math.Log(6d));
        AssertPath(
            upstream.Items.Single(item => item.EndNodeId == "root"),
            ["leaf", "left", "root"],
            ["edge-leaf-left", "edge-left-root"],
            Math.Log(6d));
        Assert.AreEqual("down", downstream.Summary.Direction);
        Assert.AreEqual("up", upstream.Summary.Direction);
    }

    [TestMethod]
    public void Analyze_RetainsStartAndAppliesSignedNodeAndEdgeTieRules()
    {
        var graph = GraphWith(
            [
                Node("root"),
                Node("a-branch"),
                Node("b-branch"),
                Node("shared-leaf"),
                Node("support-leaf"),
                Node("counter-leaf"),
                Node("parallel-leaf")
            ],
            [
                Edge("edge-a-root", "a-branch", "root", 1m),
                Edge("edge-shared-a", "shared-leaf", "a-branch", 2m),
                Edge("edge-b-root", "b-branch", "root", 1m),
                Edge("edge-shared-b", "shared-leaf", "b-branch", 2m),
                Edge("edge-support", "support-leaf", "root", 2m),
                Edge("edge-counter", "counter-leaf", "root", 0.5m),
                Edge("z-parallel", "parallel-leaf", "root", 2m),
                Edge("a-parallel", "parallel-leaf", "root", 2m)
            ]);

        var result = _analysis.Analyze(graph, "root", PathDirection.Down);

        var start = result.Items.Single(item => item.EndNodeId == "root");
        AssertPath(start, ["root"], [], 0d);
        AssertPath(
            result.Items.Single(item => item.EndNodeId == "shared-leaf"),
            ["root", "a-branch", "shared-leaf"],
            ["edge-a-root", "edge-shared-a"],
            Math.Log(2d));
        AssertPath(
            result.Items.Single(item => item.EndNodeId == "parallel-leaf"),
            ["root", "parallel-leaf"],
            ["a-parallel"],
            Math.Log(2d));

        var supportRank = result.Items.Single(item => item.EndNodeId == "support-leaf").Rank;
        var counterRank = result.Items.Single(item => item.EndNodeId == "counter-leaf").Rank;
        Assert.IsTrue(supportRank < counterRank,
            "An equal-magnitude positive path must rank ahead of a counter path.");
        Assert.AreEqual(1, start.NodeIds.Count);
        Assert.AreEqual(0, start.EdgeIds.Count);
    }

    [TestMethod]
    public void Analyze_IsIndependentOfInputPermutationAndFreezesDigestMaterial()
    {
        var nodes = new[] { Node("root"), Node("z"), Node("a"), Node("leaf") };
        var edges = new[]
        {
            Edge("edge-z-root", "z", "root", 2m),
            Edge("edge-leaf-z", "leaf", "z", 3m),
            Edge("edge-a-root", "a", "root", 2m),
            Edge("edge-leaf-a", "leaf", "a", 3m)
        };

        var normal = _analysis.Analyze(
            GraphWith(nodes, edges),
            "root",
            PathDirection.Down);
        var reversed = _analysis.Analyze(
            GraphWith(nodes.Reverse(), edges.Reverse()),
            "root",
            PathDirection.Down);

        Assert.AreEqual(normal.ResultDigest, reversed.ResultDigest);
        Assert.AreEqual(
            CanonicalJson.Canonicalize(normal.Items),
            CanonicalJson.Canonicalize(reversed.Items));
        Assert.AreEqual(CanonicalJson.ComputeSha256(normal.Items), normal.ResultDigest);
        Assert.AreEqual(
            "sha256:c3841aa33fd15bc6ede2e4e5838a667bf8a613b57353aab676d7bb732f9f4f28",
            normal.ResultDigest);
        CollectionAssert.AreEqual(
            new[] { "root", "a", "leaf" },
            normal.Items.Single(item => item.EndNodeId == "leaf").NodeIds.ToArray());
    }

    [TestMethod]
    public void Analyze_RetainsOnlyTop100SeparatelyWhileDigestCoversAllItems()
    {
        var nodes = new List<GraphNode> { Node("root") };
        var edges = new List<GraphEdge>();
        for (var index = 0; index < 105; index++)
        {
            var nodeId = $"leaf-{index:D3}";
            nodes.Add(Node(nodeId));
            edges.Add(Edge($"edge-{index:D3}", nodeId, "root", 2m));
        }

        var result = _analysis.Analyze(
            GraphWith(nodes, edges),
            "root",
            PathDirection.Down);

        Assert.AreEqual(106, result.Items.Count);
        Assert.AreEqual(106, result.TotalResultCardinality);
        Assert.AreEqual(100, result.TopItems.Count);
        Assert.AreEqual(CanonicalJson.ComputeSha256(result.Items), result.ResultDigest);
        Assert.AreNotEqual(CanonicalJson.ComputeSha256(result.TopItems), result.ResultDigest);
        Assert.AreEqual(106, result.OrderedPaths.Count);
    }

    [TestMethod]
    public void Analyze_RejectsCycleAnywhereInGraphAndUnknownDirection()
    {
        var graph = GraphWith(
            [Node("root"), Node("cycle-a"), Node("cycle-b")],
            [
                Edge("edge-a-b", "cycle-a", "cycle-b", 2m),
                Edge("edge-b-a", "cycle-b", "cycle-a", 2m)
            ]);

        Assert.ThrowsException<ArgumentException>(() =>
            _analysis.Analyze(graph, "root", PathDirection.Down));
        Assert.ThrowsException<ArgumentOutOfRangeException>(() =>
            _analysis.Analyze(
                GraphWith([Node("root")], []),
                "root",
                (PathDirection)999));
    }

    [TestMethod]
    public void Analyze_HonorsCancellationAndEmitsFrozenIdentityWithoutConsoleOutput()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.ThrowsException<OperationCanceledException>(() =>
            _analysis.Analyze(
                GraphWith([Node("root")], []),
                "root",
                PathDirection.Down,
                cancellation.Token));

        var originalOut = Console.Out;
        using var captured = new StringWriter();
        try
        {
            Console.SetOut(captured);
            var result = _analysis.Analyze(
                GraphWith([Node("root")], []),
                "root",
                PathDirection.Down);

            Assert.AreEqual(OperationKeys.PathStrongest, result.OperationKey);
            Assert.AreEqual(AlgorithmSemanticIdentities.StrongestPathV1,
                result.AlgorithmSemanticIdentity);
        }
        finally
        {
            Console.SetOut(originalOut);
        }

        Assert.AreEqual(string.Empty, captured.ToString());
    }

    private static void AssertPath(
        StrongestPathV1Item item,
        string[] expectedNodeIds,
        string[] expectedEdgeIds,
        double expectedScore)
    {
        CollectionAssert.AreEqual(expectedNodeIds, item.NodeIds.ToArray());
        CollectionAssert.AreEqual(expectedEdgeIds, item.EdgeIds.ToArray());
        Assert.AreEqual(
            CanonicalResultNumber.Normalize(expectedScore),
            item.AccumulatedLogLikelihoodRatio);
    }

    private static Graph GraphWith(
        IEnumerable<GraphNode> nodes,
        IEnumerable<GraphEdge> edges) => new()
        {
            Slug = "strongest-path-v1-test",
            Nodes = nodes.ToList(),
            Edges = edges.ToList()
        };

    private static GraphNode Node(string id) => new()
    {
        Id = id,
        Kind = "claim",
        Title = id
    };

    private static GraphEdge Edge(
        string id,
        string from,
        string to,
        decimal likelihoodRatio) => new()
        {
            Id = id,
            From = from,
            To = to,
            Kind = "custom",
            ImportanceToParent = likelihoodRatio
        };
}
