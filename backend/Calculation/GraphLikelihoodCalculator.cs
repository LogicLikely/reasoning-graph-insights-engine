namespace Backend.Calculation;

public sealed class GraphLikelihoodCalculator
{
    private const decimal MinLogOdds = -100m;
    private const decimal MaxLogOdds = 100m;

    public Dictionary<string, decimal> RecalculateAncestors(
        GraphCalculationContext context,
        string changedNodeId)
    {
        if (!context.NodesById.ContainsKey(changedNodeId))
        {
            throw new InvalidOperationException($"Changed node '{changedNodeId}' does not exist in the calculation context.");
        }

        var affectedDistances = CollectAffectedAncestorDistances(context, changedNodeId);
        return RecalculateAffectedNodes(context, affectedDistances);
    }

    public Dictionary<string, decimal> RecalculateNodesAndAncestors(
        GraphCalculationContext context,
        IEnumerable<string> nodeIds)
    {
        //Sorts nodes by distance so can propogate odds up graph level by level
        var affectedDistances = CollectAffectedNodeAndAncestorDistances(context, nodeIds);
        return RecalculateAffectedNodes(context, affectedDistances);
    }

    private static Dictionary<string, decimal> RecalculateAffectedNodes(
        GraphCalculationContext context,
        Dictionary<string, int> affectedDistances)
    {
        var recalculatedValues = new Dictionary<string, decimal>();
        foreach (var nodeId in affectedDistances
                     .OrderBy(affected => affected.Value)
                     .ThenBy(affected => affected.Key, StringComparer.Ordinal)
                     .Select(affected => affected.Key))
        {
            var logOdds = CalculateNodeLogOdds(context, nodeId);
            context.NodesById[nodeId].LogOdds = logOdds;
            recalculatedValues[nodeId] = logOdds;
        }

        return recalculatedValues;
    }

    private static Dictionary<string, int> CollectAffectedNodeAndAncestorDistances(
        GraphCalculationContext context,
        IEnumerable<string> nodeIds)
    {
        var affectedDistances = new Dictionary<string, int>();
        var stack = new Stack<AncestorTraversalState>();

        foreach (var nodeId in nodeIds.Distinct(StringComparer.Ordinal))
        {
            if (!context.NodesById.ContainsKey(nodeId))
            {
                throw new InvalidOperationException($"Node '{nodeId}' does not exist in the calculation context.");
            }

            affectedDistances[nodeId] = 0;
            stack.Push(new AncestorTraversalState(nodeId, 0, [nodeId]));
        }

        CollectAncestorDistances(context, stack, affectedDistances);

        return affectedDistances;
    }

    private static Dictionary<string, int> CollectAffectedAncestorDistances(
        GraphCalculationContext context,
        string changedNodeId)
    {
        var affectedDistances = new Dictionary<string, int>();
        var stack = new Stack<AncestorTraversalState>();
        stack.Push(new AncestorTraversalState(changedNodeId, 0, [changedNodeId]));

        CollectAncestorDistances(context, stack, affectedDistances);

        return affectedDistances;
    }

    private static void CollectAncestorDistances(
        GraphCalculationContext context,
        Stack<AncestorTraversalState> stack,
        Dictionary<string, int> affectedDistances)
    {
        while (stack.Count > 0)
        {
            var current = stack.Pop();
            if (!context.ParentEdgesByChildId.TryGetValue(current.NodeId, out var parentEdges))
            {
                continue;
            }

            foreach (var parentEdge in parentEdges)
            {
                var parentNodeId = parentEdge.ToNodeId;
                if (!context.NodesById.ContainsKey(parentNodeId))
                {
                    throw new InvalidOperationException(
                        $"Edge '{parentEdge.Id}' references missing to node '{parentNodeId}'.");
                }

                if (current.Path.Contains(parentNodeId))
                {
                    throw new InvalidOperationException(
                        $"Cycle detected while recalculating graph likelihood at node '{parentNodeId}'.");
                }

                var nextDistance = current.Distance + 1;
                if (!affectedDistances.TryGetValue(parentNodeId, out var existingDistance) ||
                    nextDistance > existingDistance)
                {
                    affectedDistances[parentNodeId] = nextDistance;
                }

                var nextPath = new HashSet<string>(current.Path) { parentNodeId };
                stack.Push(new AncestorTraversalState(parentNodeId, nextDistance, nextPath));
            }
        }
    }

    //Runs dijkstra to find specific path from point targetNode to targetClaim with the "strongest" likelihood ratio
    public decimal getSingleShortestPath(GraphCalculationContext context, string startNodeId, string targetClaimId)
    {
        if (!context.NodesById.TryGetValue(startNodeId, out var targetNode))
        {
            throw new InvalidOperationException($"Node '{startNodeId}' does not exist in the calculation context.");
        }

        //Make priority queue
        var compare = Comparer<decimal>.Create((a, b) =>
        Math.Abs(Math.Log((double)b))
            .CompareTo(Math.Abs(Math.Log((double)a))));
        var queue = new PriorityQueue<Tuple<string, decimal>, decimal>(compare);

        Dictionary<string, decimal> dist = new Dictionary<string, decimal>();

        if (!context.ChildEdgesByParentId.TryGetValue(startNodeId, out var targetChildrenEdges))
        {
            return 1m;
        }
        queue.Enqueue(Tuple.Create(startNodeId, 0m), 0m);

        while (queue.Count > 0)
        {
            var item = queue.Dequeue();
            string nodeId = item.Item1;
            decimal d = item.Item2;

            if (dist.ContainsKey(nodeId)) continue;
            if (!context.ChildEdgesByParentId.TryGetValue(nodeId, out var childEdges)) continue;

            foreach (GraphEdgeCalcState edge in childEdges)
            {
                decimal currentDist = edge.ImportanceToParent + d;
                string neighborId = edge.ToNodeId;
                if (!dist.ContainsKey(neighborId)) dist.Add(neighborId, decimal.MinValue);
                if (currentDist > dist[neighborId])
                {
                    dist[neighborId] = currentDist;
                    queue.Enqueue(Tuple.Create(neighborId, currentDist), edge.ImportanceToParent);
                    if (neighborId.Equals(targetClaimId)) return currentDist;
                }
            }
        }
        return -1m;
    }

    //Search through parent nodes to calculate total likelihood (importance) value
    public decimal getAccumulatedLR(GraphCalculationContext context, string targetNodeId, string targetClaimId)
    {
        return getSingleShortestPath(context, targetNodeId, targetClaimId);
    }

    private static decimal CalculateNodeLogOdds(GraphCalculationContext context, string nodeId)
    {
        if (!context.NodesById.ContainsKey(nodeId))
        {
            throw new InvalidOperationException($"Node '{nodeId}' does not exist in the calculation context.");
        }

        if (!context.ChildEdgesByParentId.TryGetValue(nodeId, out var childEdges))
        {
            return 0m;
        }

        var logOdds = childEdges.Sum(edge =>
        {
            if (!context.NodesById.TryGetValue(edge.FromNodeId, out var childNode))
            {
                throw new InvalidOperationException(
                    $"Edge '{edge.Id}' references missing from node '{edge.FromNodeId}'.");
            }

            return childNode.LogOdds * GetDirection(edge.Kind) * (edge.ImportanceToParent / 10m);
        });

        return ClampLogOdds(logOdds);
    }

    private static decimal GetDirection(string edgeKind)
    {
        return edgeKind.ToLowerInvariant() switch
        {
            "support" => 1m,
            "rebut" => -1m,
            _ => throw new InvalidOperationException($"Unknown edge kind '{edgeKind}'.")
        };
    }

    private static decimal ClampLogOdds(decimal value)
    {
        return Math.Clamp(value, MinLogOdds, MaxLogOdds);
    }

    private sealed record AncestorTraversalState(string NodeId, int Distance, HashSet<string> Path);
}
