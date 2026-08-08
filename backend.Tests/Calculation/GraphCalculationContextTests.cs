using Backend.Calculation;
using Backend.Models.Domain;

namespace backend.Tests.Calculation;

[TestClass]
public class GraphCalculationContextTests
{
    [TestMethod]
    public void From_PopulatesNodesByIdWithOriginalLogOdds()
    {
        var nodes = new[]
        {
            Node("A", "claim", 0.1m),
            Node("B", "evidence", 0.2m),
            Node("C", "objection", -0.3m),
            Node("D", "claim")
        };

        var context = GraphCalculationContext.From(nodes, []);

        Assert.AreEqual(4, context.NodesById.Count);
        Assert.AreEqual(0.1m, context.NodesById["A"].PriorOdds);
        Assert.AreEqual(0.2m, context.NodesById["B"].PriorOdds);
        Assert.AreEqual(-0.3m, context.NodesById["C"].PriorOdds);
        Assert.AreEqual(0m, context.NodesById["D"].PriorOdds);
    }

    [TestMethod]
    public void From_MapsParentEdgesByChildId()
    {
        var context = GraphCalculationContext.From(
            [Node("A"), Node("B"), Node("C")],
            [
                Edge("E-B-A", "B", "A"),
                Edge("E-C-A", "C", "A")
            ]);

        Assert.AreEqual(1, context.ParentEdgesByChildId["B"].Count);
        Assert.AreEqual("B", context.ParentEdgesByChildId["B"][0].FromNodeId);
        Assert.AreEqual("A", context.ParentEdgesByChildId["B"][0].ToNodeId);
        Assert.AreEqual(1, context.ParentEdgesByChildId["C"].Count);
        Assert.AreEqual("C", context.ParentEdgesByChildId["C"][0].FromNodeId);
        Assert.AreEqual("A", context.ParentEdgesByChildId["C"][0].ToNodeId);
    }

    [TestMethod]
    public void From_MapsChildEdgesByParentId()
    {
        var context = GraphCalculationContext.From(
            [Node("A"), Node("B"), Node("C")],
            [
                Edge("E-B-A", "B", "A"),
                Edge("E-C-A", "C", "A")
            ]);

        var childEdges = context.ChildEdgesByParentId["A"];

        Assert.AreEqual(2, childEdges.Count);
        CollectionAssert.AreEquivalent(
            new[] { "E-B-A", "E-C-A" },
            childEdges.Select(edge => edge.Id).ToArray());
    }

    [TestMethod]
    public void From_HandlesSharedChildDagCase()
    {
        var context = GraphCalculationContext.From(
            [Node("A"), Node("B"), Node("C"), Node("F")],
            [
                Edge("E-F-B", "F", "B"),
                Edge("E-F-C", "F", "C"),
                Edge("E-B-A", "B", "A"),
                Edge("E-C-A", "C", "A")
            ]);

        CollectionAssert.AreEquivalent(
            new[] { "E-F-B", "E-F-C" },
            context.ParentEdgesByChildId["F"].Select(edge => edge.Id).ToArray());
        CollectionAssert.AreEquivalent(
            new[] { "E-B-A", "E-C-A" },
            context.ChildEdgesByParentId["A"].Select(edge => edge.Id).ToArray());
    }

    [TestMethod]
    public void From_PreservesEdgeValues()
    {
        var context = GraphCalculationContext.From(
            [Node("A"), Node("B")],
            [Edge("E-B-A", "B", "A", "rebut", 7)]);

        var edge = context.ParentEdgesByChildId["B"][0];

        Assert.AreEqual("E-B-A", edge.Id);
        Assert.AreEqual("B", edge.FromNodeId);
        Assert.AreEqual("A", edge.ToNodeId);
        Assert.AreEqual("rebut", edge.Kind);
        Assert.AreEqual(7, edge.ImportanceToParent);
    }

    [TestMethod]
    public void From_ThrowsClearExceptionWhenEdgeReferencesMissingNode()
    {
        var exception = Assert.ThrowsException<InvalidOperationException>(() =>
            GraphCalculationContext.From(
                [Node("A")],
                [Edge("E-B-A", "B", "A")]));

        StringAssert.Contains(exception.Message, "Edge 'E-B-A' references missing from node 'B'.");
    }

    private static GraphNode Node(string id, string kind = "claim", decimal logOdds = 0m)
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
        int importanceToParent = 1)
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
}
