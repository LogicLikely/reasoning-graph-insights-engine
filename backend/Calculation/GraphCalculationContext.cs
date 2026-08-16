using Backend.Models.Domain;

namespace Backend.Calculation;

public sealed class GraphCalculationContext
{
    private GraphCalculationContext(
        Dictionary<string, GraphNodeCalcState> nodesById,
        Dictionary<string, List<GraphEdgeCalcState>> parentEdgesByChildId,
        Dictionary<string, List<GraphEdgeCalcState>> childEdgesByParentId)
    {
        NodesById = nodesById;
        ParentEdgesByChildId = parentEdgesByChildId;
        ChildEdgesByParentId = childEdgesByParentId;
    }

    public Dictionary<string, GraphNodeCalcState> NodesById { get; }

    public Dictionary<string, List<GraphEdgeCalcState>> ParentEdgesByChildId { get; }

    public Dictionary<string, List<GraphEdgeCalcState>> ChildEdgesByParentId { get; }

    public static GraphCalculationContext From(
        IEnumerable<GraphNode> nodes,
        IEnumerable<GraphEdge> edges,
        CancellationToken cancellationToken = default)
    {
        var nodesById = nodes.ToDictionary(
            node =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                return node.Id;
            },
            node =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                return new GraphNodeCalcState
                {
                    Id = node.Id,
                    Kind = node.Kind,
                    PriorOdds = node.PriorOdds,
                    PosteriorOdds = node.PosteriorOdds
                };
            });

        var edgeStates = edges
            .Select(edge =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!nodesById.ContainsKey(edge.From))
                {
                    throw new InvalidOperationException(
                        $"Edge '{edge.Id}' references missing from node '{edge.From}'.");
                }

                if (!nodesById.ContainsKey(edge.To))
                {
                    throw new InvalidOperationException(
                        $"Edge '{edge.Id}' references missing to node '{edge.To}'.");
                }

                return new GraphEdgeCalcState(
                    edge.Id,
                    edge.From,
                    edge.To,
                    edge.Kind,
                    edge.ImportanceToParent);
            })
            .ToList();

        var parentEdgesByChildId = new Dictionary<string, List<GraphEdgeCalcState>>();
        var childEdgesByParentId = new Dictionary<string, List<GraphEdgeCalcState>>();
        foreach (var edge in edgeStates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            AddEdge(parentEdgesByChildId, edge.FromNodeId, edge);
            AddEdge(childEdgesByParentId, edge.ToNodeId, edge);
        }

        return new GraphCalculationContext(nodesById, parentEdgesByChildId, childEdgesByParentId);
    }

    private static void AddEdge(
        Dictionary<string, List<GraphEdgeCalcState>> edgesByNodeId,
        string nodeId,
        GraphEdgeCalcState edge)
    {
        if (!edgesByNodeId.TryGetValue(nodeId, out var nodeEdges))
        {
            nodeEdges = [];
            edgesByNodeId[nodeId] = nodeEdges;
        }

        nodeEdges.Add(edge);
    }
}
