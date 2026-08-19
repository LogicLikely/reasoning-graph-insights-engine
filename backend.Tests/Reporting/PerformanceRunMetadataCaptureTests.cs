using Backend.Models.Domain;
using Backend.Reporting;
using Backend.Seeding;

namespace backend.Tests.Reporting;

[TestClass]
public class PerformanceRunMetadataCaptureTests
{
    [TestMethod]
    public void CaptureGraph_ClassifiesCataloguedStressGraph()
    {
        var graph = GraphWith(StressGraphSeedIds.SharedDiamond1K);

        var metadata = PerformanceRunMetadataCapture.CaptureGraph(graph);

        Assert.AreEqual("shared-diamond", metadata.Type);
        Assert.AreEqual(5, metadata.MaximumDepth);
    }

    [DataTestMethod]
    [DataRow("sample-medium", "sample")]
    [DataRow("flat-earth-large", "large-example")]
    [DataRow("custom-graph", "uncatalogued")]
    public void CaptureGraph_ClassifiesNonStressGraphs(string slug, string expectedType)
    {
        var metadata = PerformanceRunMetadataCapture.CaptureGraph(GraphWith(slug));

        Assert.AreEqual(expectedType, metadata.Type);
        Assert.IsNull(metadata.MaximumDepth);
    }

    [TestMethod]
    public void CaptureGraph_RecordsGraphAndNormalizedNodeKindCounts()
    {
        var graph = GraphWith(
            "custom-graph",
            [
                Node("R", "Root"),
                Node("A", "claim"),
                Node("B", "Evidence"),
                Node("C", "objection"),
                Node("D", "CLAIM")
            ],
            [
                Edge("A-R", "A", "R"),
                Edge("B-A", "B", "A"),
                Edge("C-R", "C", "R")
            ]);

        var metadata = PerformanceRunMetadataCapture.CaptureGraph(graph);

        Assert.AreEqual(5, metadata.NodeCount);
        Assert.AreEqual(3, metadata.EdgeCount);
        CollectionAssert.AreEquivalent(
            new[] { "claim", "evidence", "objection", "root" },
            metadata.NodeKindCounts.Keys.ToArray());
        Assert.AreEqual(2, metadata.NodeKindCounts["claim"]);
        Assert.AreEqual(1, metadata.NodeKindCounts["evidence"]);
        Assert.AreEqual(1, metadata.NodeKindCounts["objection"]);
        Assert.AreEqual(1, metadata.NodeKindCounts["root"]);
        Assert.AreEqual(3, PerformanceRunMetadataCapture.CountLeafNodes(graph));
        Assert.AreEqual(4, PerformanceRunMetadataCapture.CountReachableNodes(graph, "R"));
    }

    [TestMethod]
    public void CaptureGraph_FingerprintIsStableAcrossNodeAndEdgeOrdering()
    {
        var first = GraphWith(
            "same-graph",
            [Node("R", "root"), Node("A", "claim"), Node("B", "evidence")],
            [Edge("A-R", "A", "R"), Edge("B-A", "B", "A")]);
        var reordered = GraphWith(
            "same-graph",
            [Node("B", "evidence"), Node("R", "root"), Node("A", "claim")],
            [Edge("B-A", "B", "A"), Edge("A-R", "A", "R")]);

        var firstFingerprint = PerformanceRunMetadataCapture.CaptureGraph(first).Fingerprint;
        var reorderedFingerprint = PerformanceRunMetadataCapture.CaptureGraph(reordered).Fingerprint;

        Assert.IsNotNull(firstFingerprint);
        StringAssert.StartsWith(firstFingerprint, "sha256:");
        Assert.AreEqual(firstFingerprint, reorderedFingerprint);
    }

    [TestMethod]
    public void CaptureGraph_FingerprintChangesWhenGraphContentChanges()
    {
        var first = GraphWith(
            "same-graph",
            [Node("R", "root"), Node("A", "claim")],
            [Edge("A-R", "A", "R")]);
        var changed = GraphWith(
            "same-graph",
            [Node("R", "root"), Node("A", "objection")],
            [Edge("A-R", "A", "R")]);

        Assert.AreNotEqual(
            PerformanceRunMetadataCapture.CaptureGraph(first).Fingerprint,
            PerformanceRunMetadataCapture.CaptureGraph(changed).Fingerprint);
    }

    [TestMethod]
    public void GetMaximumAncestorDistance_ReturnsLongestSharedDagPath()
    {
        var graph = GraphWith(
            "shared-paths",
            [],
            [
                Edge("changed-root", "changed", "root"),
                Edge("changed-a", "changed", "a"),
                Edge("changed-b", "changed", "b"),
                Edge("a-shared", "a", "shared"),
                Edge("b-shared", "b", "shared"),
                Edge("shared-root", "shared", "root")
            ]);

        var distance = PerformanceRunMetadataCapture.GetMaximumAncestorDistance(
            graph,
            "changed");

        Assert.AreEqual(3, distance);
    }

    [TestMethod]
    public void GetMaximumAncestorDistance_ReturnsZeroWhenNodeHasNoAncestors()
    {
        Assert.AreEqual(
            0,
            PerformanceRunMetadataCapture.GetMaximumAncestorDistance(
                GraphWith("isolated"),
                "changed"));
    }

    [TestMethod]
    public void GetMaximumAncestorDistance_RejectsReachableCycle()
    {
        var graph = GraphWith(
            "cyclic",
            [],
            [
                Edge("changed-a", "changed", "a"),
                Edge("a-b", "a", "b"),
                Edge("b-a", "b", "a")
            ]);

        var exception = Assert.ThrowsException<InvalidOperationException>(() =>
            PerformanceRunMetadataCapture.GetMaximumAncestorDistance(graph, "changed"));

        StringAssert.Contains(exception.Message, "contains a cycle");
    }

    [TestMethod]
    [Timeout(10_000)]
    public void GetMaximumAncestorDistance_HandlesOneHundredThousandNodeChain()
    {
        const int nodeCount = 100_000;
        var edges = new List<GraphEdge>(nodeCount - 1);
        for (var index = 0; index < nodeCount - 1; index++)
        {
            edges.Add(Edge(
                $"edge-{index}",
                $"node-{index}",
                $"node-{index + 1}"));
        }

        var distance = PerformanceRunMetadataCapture.GetMaximumAncestorDistance(
            GraphWith("deep", [], edges),
            "node-0");

        Assert.AreEqual(nodeCount - 1, distance);
    }

    private static Graph GraphWith(
        string slug,
        IEnumerable<GraphNode>? nodes = null,
        IEnumerable<GraphEdge>? edges = null)
    {
        return new Graph
        {
            Slug = slug,
            Nodes = nodes?.ToList() ?? [],
            Edges = edges?.ToList() ?? []
        };
    }

    private static GraphNode Node(string id, string kind)
    {
        return new GraphNode
        {
            Id = id,
            Kind = kind,
            Title = id
        };
    }

    private static GraphEdge Edge(string id, string from, string to)
    {
        return new GraphEdge
        {
            Id = id,
            From = from,
            To = to,
            Kind = "support"
        };
    }
}
