namespace Backend.Calculation;

public enum LogPathSelection
{
    Minimum,
    Maximum
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
    private decimal? strongestLogPath(
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
    public decimal? GetAccumulatedLR(GraphCalculationContext context, string startNodeId, string targetClaimId)
    {
        var strongestLog = strongestLogPath(context, startNodeId, targetClaimId);
        return strongestLog.HasValue
            ? (decimal)Math.Exp((double)strongestLog.Value)
            : null;
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
