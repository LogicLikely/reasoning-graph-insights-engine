namespace Backend.Calculation;

public enum LogPathSelection
{
    Minimum,
    Maximum
}
public enum PathDirection
{
    Up,
    Down
}

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

    private Dictionary<string, decimal> RecalculateAffectedNodes(
        GraphCalculationContext context,
        Dictionary<string, int> affectedDistances)
    {
        var recalculatedValues = new Dictionary<string, decimal>();
        foreach (var nodeId in affectedDistances
                     .OrderBy(affected => affected.Value)
                     .ThenBy(affected => affected.Key, StringComparer.Ordinal)
                     .Select(affected => affected.Key))
        {
            var posteriorOdds = CalculateNodeLogPosteriorOdds(context, nodeId);
            context.NodesById[nodeId].PosteriorOdds = posteriorOdds;
            recalculatedValues[nodeId] = posteriorOdds;
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

    // Returns the minimum sum of log edge weights along a path from startNode to targetClaim.
    public decimal? GetMinLogPath(GraphCalculationContext context, string startNodeId, string targetClaimId)
        => GetLogPath(context, startNodeId, targetClaimId, LogPathSelection.Minimum);

    // Returns the maximum sum of log edge weights along a path from startNode to targetClaim.
    public decimal? GetMaxLogPath(GraphCalculationContext context, string startNodeId, string targetClaimId)
        => GetLogPath(context, startNodeId, targetClaimId, LogPathSelection.Maximum);

    public decimal? GetLogPath(
        GraphCalculationContext context,
        string startNodeId,
        string targetClaimId,
        LogPathSelection selection)
    {
        if (!context.NodesById.ContainsKey(startNodeId))
        {
            throw new InvalidOperationException($"Start node '{startNodeId}' does not exist in the calculation context.");
        }

        if (!context.NodesById.ContainsKey(targetClaimId))
        {
            throw new InvalidOperationException($"Target claim '{targetClaimId}' does not exist in the calculation context.");
        }

        if (!Enum.IsDefined(selection))
        {
            throw new ArgumentOutOfRangeException(nameof(selection), selection, "Unknown log path selection.");
        }

        return FindLogPath(context, startNodeId, targetClaimId, selection, [startNodeId]);
    }

    private static decimal? FindLogPath(
        GraphCalculationContext context,
        string currentNodeId,
        string targetClaimId,
        LogPathSelection selection,
        HashSet<string> path)
    {
        if (currentNodeId == targetClaimId)
        {
            return 0m;
        }

        if (!context.ParentEdgesByChildId.TryGetValue(currentNodeId, out var parentEdges))
        {
            return null;
        }

        decimal? bestPath = null;
        foreach (var edge in parentEdges)
        {
            if (edge.ImportanceToParent <= 0m) throw new InvalidOperationException(
                $"Edge '{edge.Id}' has invalid likelihood ratio '{edge.ImportanceToParent}'. Likelihood ratios must be greater than zero.");

            if (!path.Add(edge.ToNodeId)) throw new InvalidOperationException(
                                            $"Cycle detected while calculating minimum log path at node '{edge.ToNodeId}'.");

            var remainingPath = FindLogPath(context, edge.ToNodeId, targetClaimId, selection, path);
            path.Remove(edge.ToNodeId);

            if (!remainingPath.HasValue) continue;

            var currentPath = (decimal)Math.Log((double)edge.ImportanceToParent) + remainingPath.Value;
            if (!bestPath.HasValue || IsBetterLogPath(currentPath, bestPath.Value, selection))
            {
                bestPath = currentPath;
            }
        }

        return bestPath;
    }

    private static bool IsBetterLogPath(decimal candidate, decimal currentBest, LogPathSelection selection)
    {
        return selection switch
        {
            LogPathSelection.Minimum => candidate < currentBest,
            LogPathSelection.Maximum => candidate > currentBest,
            _ => throw new ArgumentOutOfRangeException(nameof(selection), selection, "Unknown log path selection.")
        };
    }

    //Returns the 'strongest' edge weight sum in the log path from startNode to targetClaim
    private decimal? StrongestLogPath(
        GraphCalculationContext context,
        string startNodeId,
        string targetClaimId)
    {
        decimal? minLog = GetMinLogPath(context, startNodeId, targetClaimId);
        decimal? maxLog = GetMaxLogPath(context, startNodeId, targetClaimId);

        if (!minLog.HasValue) return maxLog;
        else if (!maxLog.HasValue) return minLog;
        else if (Math.Abs(minLog.Value) > Math.Abs(maxLog.Value)) return minLog;
        else return maxLog;
    }

    // Returns the likelihood ratio for the path farthest from neutral (1.0).
    public decimal? GetSingleAccumulatedLR(GraphCalculationContext context, string startNodeId, string targetClaimId)
    {
        var strongestLog = StrongestLogPath(context, startNodeId, targetClaimId);
        return strongestLog.HasValue
            ? (decimal)Math.Exp((double)strongestLog.Value)
            : null;
    }

    //Returns dictionary assigning an LR value to every EVIDENCE node downsteam from a starting node
    public Dictionary<string, decimal> GetDownstreamEvidenceLogLRs(GraphCalculationContext context, string nodeId)
    {
        Dictionary<string, decimal> unfilteredPaths = GetStrongestPaths(context, nodeId, PathDirection.Down);

        return unfilteredPaths
            .Where(path => IsEvidenceKind(context.NodesById[path.Key].Kind))
            .ToDictionary(path => path.Key, path => path.Value);
    }

    private static bool IsEvidenceKind(string nodeKind)
    {
        return string.Equals(nodeKind, "evidence", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(nodeKind, "objection", StringComparison.OrdinalIgnoreCase);
    }

    //Returns list of nodes reachable from a start node when traversing either up or down the graph
    private static List<string> GetReachableNodes(GraphCalculationContext context, string startNodeId, PathDirection pathDirection)
    {
        if (!context.NodesById.ContainsKey(startNodeId))
        {
            throw new InvalidOperationException($"Node '{startNodeId}' does not exist in the calculation context.");
        }

        Dictionary<string, List<GraphEdgeCalcState>> connectedEdgesDict = GetConnectEdgesDict(context, pathDirection);
        var reachableNodeIds = new List<string>();
        var visitedNodeIds = new HashSet<string>();
        var nodesToVisit = new Stack<string>();
        nodesToVisit.Push(startNodeId);

        while (nodesToVisit.Count > 0)
        {
            string currentNodeId = nodesToVisit.Pop();
            if (!visitedNodeIds.Add(currentNodeId)) continue;

            reachableNodeIds.Add(currentNodeId);

            if (!connectedEdgesDict.TryGetValue(currentNodeId, out List<GraphEdgeCalcState>? connectedEdges))
            {
                continue;
            }

            for (int i = connectedEdges.Count - 1; i >= 0; i--)
            {
                string neighborId = GetNeighborId(connectedEdges[i], pathDirection);
                if (!context.NodesById.ContainsKey(neighborId))
                {
                    throw new InvalidOperationException($"Node '{neighborId}' does not exist in the calculation context.");
                }

                if (!visitedNodeIds.Contains(neighborId)) nodesToVisit.Push(neighborId);
            }
        }

        return reachableNodeIds;
    }

    //Uses Bellman ford to find all strongest paths upstream or downstream from a node
    public Dictionary<string, decimal> GetStrongestPaths(GraphCalculationContext context, string startNodeId, PathDirection pathDirection)
    {
        List<string> usedNodeIds = GetReachableNodes(context, startNodeId, pathDirection);
        int n = usedNodeIds.Count;
        Dictionary<string, List<GraphEdgeCalcState>> connectedEdgesDict = GetConnectEdgesDict(context, pathDirection);
        var minimumLogPaths = usedNodeIds.ToDictionary(id => id, _ => (decimal?)null);
        var maximumLogPaths = usedNodeIds.ToDictionary(id => id, _ => (decimal?)null);
        minimumLogPaths[startNodeId] = 0m;
        maximumLogPaths[startNodeId] = 0m;

        // A simple path contains at most n - 1 edges.
        for (int k = 0; k < n - 1; k++)
        {
            bool changed = false;

            foreach (string currentNodeId in usedNodeIds)
            {
                //Checks if currentNode is usable (is not usable if doesn't have existing k-1 hop path or has no neighbors)
                if (!minimumLogPaths[currentNodeId].HasValue ||
                    !connectedEdgesDict.TryGetValue(currentNodeId, out List<GraphEdgeCalcState>? connectedEdges))
                {
                    continue;
                }

                //Updates k-hop path for nodes neighboring currentNode 
                foreach (GraphEdgeCalcState edge in connectedEdges)
                {
                    string neighborId = GetNeighborId(edge, pathDirection);
                    if (!minimumLogPaths.ContainsKey(neighborId))
                    {
                        throw new InvalidOperationException($"Node '{neighborId}' is percieved as unreachable from {startNodeId}.");
                    }

                    decimal logWeight = GetLogEdgeWeight(edge);
                    decimal minimumCandidate = minimumLogPaths[currentNodeId]!.Value + logWeight;
                    decimal maximumCandidate = maximumLogPaths[currentNodeId]!.Value + logWeight;

                    if (!minimumLogPaths[neighborId].HasValue || minimumCandidate < minimumLogPaths[neighborId]!.Value)
                    {
                        minimumLogPaths[neighborId] = minimumCandidate;
                        changed = true;
                    }

                    if (!maximumLogPaths[neighborId].HasValue || maximumCandidate > maximumLogPaths[neighborId]!.Value)
                    {
                        maximumLogPaths[neighborId] = maximumCandidate;
                        changed = true;
                    }
                }
            }

            if (!changed) break;
        }

        return usedNodeIds.ToDictionary(
            id => id,
            id => Math.Abs(minimumLogPaths[id]!.Value) > Math.Abs(maximumLogPaths[id]!.Value)
                ? minimumLogPaths[id]!.Value
                : maximumLogPaths[id]!.Value);
    }

    private static decimal GetLogEdgeWeight(GraphEdgeCalcState edge)
    {
        if (edge.ImportanceToParent <= 0m)
        {
            throw new InvalidOperationException(
                $"Edge '{edge.Id}' has invalid likelihood ratio '{edge.ImportanceToParent}'. Likelihood ratios must be greater than zero.");
        }

        return (decimal)Math.Log((double)edge.ImportanceToParent);
    }

    private static Dictionary<string, List<GraphEdgeCalcState>> GetConnectEdgesDict(
        GraphCalculationContext context,
        PathDirection pathDirection)
    {
        if (pathDirection == PathDirection.Up) return context.ParentEdgesByChildId;
        else return context.ChildEdgesByParentId;
    }

    private static string GetNeighborId(GraphEdgeCalcState edge, PathDirection pathDirection)
    {
        if (pathDirection == PathDirection.Up) return edge.ToNodeId;
        else return edge.FromNodeId;
    }

    public decimal CalculateNodeLogPosteriorOdds(GraphCalculationContext context, string nodeId)
    {
        if (!context.NodesById.ContainsKey(nodeId))
        {
            throw new InvalidOperationException($"Node '{nodeId}' does not exist in the calculation context.");
        }

        if (!context.ChildEdgesByParentId.TryGetValue(nodeId, out var childEdges))
        {
            return context.NodesById[nodeId].PriorOdds;
        }

        decimal priorOdds = context.NodesById[nodeId].PriorOdds;
        Dictionary<string, decimal> evidenceLRs = GetDownstreamEvidenceLogLRs(context, nodeId);

        var logPosteriorOdds = priorOdds + evidenceLRs.Values.Sum();

        return ClampLogOdds(logPosteriorOdds);
    }


    private static decimal ClampLogOdds(decimal value)
    {
        return Math.Clamp(value, MinLogOdds, MaxLogOdds);
    }

    private sealed record AncestorTraversalState(string NodeId, int Distance, HashSet<string> Path);
}
