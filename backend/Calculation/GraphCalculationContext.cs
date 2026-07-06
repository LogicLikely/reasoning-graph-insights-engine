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

    public static GraphCalculationContext From(IEnumerable<GraphNode> nodes, IEnumerable<GraphEdge> edges)
    {
        var nodesById = nodes.ToDictionary(
            node => node.Id,
            node => new GraphNodeCalcState
            {
                Id = node.Id,
                Kind = node.Kind,
                LogOdds = node.LogOdds
            });

        var edgeStates = edges
            .Select(edge =>
            {
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

        var parentEdgesByChildId = edgeStates
            .GroupBy(edge => edge.FromNodeId)
            .ToDictionary(group => group.Key, group => group.ToList());

        var childEdgesByParentId = edgeStates
            .GroupBy(edge => edge.ToNodeId)
            .ToDictionary(group => group.Key, group => group.ToList());

        return new GraphCalculationContext(nodesById, parentEdgesByChildId, childEdgesByParentId);
    }
}
