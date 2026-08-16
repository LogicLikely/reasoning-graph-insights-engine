using Backend.Models.Domain;

namespace Backend.Insights.Contracts;

public sealed record AlgorithmGraphContractIssue(string Code, string Message);

public sealed class AlgorithmGraphContractValidationResult
{
    internal AlgorithmGraphContractValidationResult(IEnumerable<AlgorithmGraphContractIssue> issues)
    {
        Issues = Array.AsReadOnly(issues.ToArray());
    }

    public bool IsValid => Issues.Count == 0;

    public IReadOnlyList<AlgorithmGraphContractIssue> Issues { get; }
}

internal static class AlgorithmGraphContractValidation
{
    public static AlgorithmGraphContractValidationResult ValidateDirectedAcyclicGraph(Graph graph)
    {
        ArgumentNullException.ThrowIfNull(graph);

        var issues = new List<AlgorithmGraphContractIssue>();
        if (graph.Nodes is null)
        {
            issues.Add(new(
                "null-node-collection",
                "The graph node collection must not be null."));
        }

        if (graph.Edges is null)
        {
            issues.Add(new(
                "null-edge-collection",
                "The graph edge collection must not be null."));
        }

        var nodes = graph.Nodes ?? [];
        var edges = graph.Edges ?? [];
        var nodeIds = new HashSet<string>(StringComparer.Ordinal);

        foreach (var node in nodes)
        {
            if (node is null)
            {
                issues.Add(new(
                    "null-node",
                    "Graph nodes must not contain null entries."));
                continue;
            }

            if (string.IsNullOrWhiteSpace(node.Id))
            {
                issues.Add(new(
                    "empty-node-id",
                    "Every graph node must have a non-empty ID."));
                continue;
            }

            if (!nodeIds.Add(node.Id))
            {
                issues.Add(new(
                    "duplicate-node-id",
                    $"Node ID '{node.Id}' occurs more than once."));
            }
        }

        var edgeIds = new HashSet<string>(StringComparer.Ordinal);
        var validStructuralEdges = new List<GraphEdge>();
        foreach (var edge in edges)
        {
            if (edge is null)
            {
                issues.Add(new(
                    "null-edge",
                    "Graph edges must not contain null entries."));
                continue;
            }

            if (string.IsNullOrWhiteSpace(edge.Id))
            {
                issues.Add(new(
                    "empty-edge-id",
                    "Every graph edge must have a non-empty ID."));
            }
            else if (!edgeIds.Add(edge.Id))
            {
                issues.Add(new(
                    "duplicate-edge-id",
                    $"Edge ID '{edge.Id}' occurs more than once."));
            }

            var hasFrom = nodeIds.Contains(edge.From);
            var hasTo = nodeIds.Contains(edge.To);
            if (!hasFrom)
            {
                issues.Add(new(
                    "missing-from-node",
                    $"Edge '{edge.Id}' references missing From node '{edge.From}'."));
            }

            if (!hasTo)
            {
                issues.Add(new(
                    "missing-to-node",
                    $"Edge '{edge.Id}' references missing To node '{edge.To}'."));
            }

            if (edge.ImportanceToParent <= 0m)
            {
                issues.Add(new(
                    "non-positive-edge-lr",
                    $"Edge '{edge.Id}' has non-positive likelihood ratio '{edge.ImportanceToParent}'."));
            }

            if (hasFrom && hasTo)
            {
                validStructuralEdges.Add(edge);
            }
        }

        if (nodeIds.Count == nodes.Count && ContainsDirectedCycle(nodeIds, validStructuralEdges))
        {
            issues.Add(new(
                "directed-cycle",
                "Versioned likelihood analysis requires a directed acyclic graph."));
        }

        return new AlgorithmGraphContractValidationResult(issues);
    }

    private static bool ContainsDirectedCycle(
        IReadOnlyCollection<string> nodeIds,
        IEnumerable<GraphEdge> edges)
    {
        var incomingCount = nodeIds.ToDictionary(nodeId => nodeId, _ => 0, StringComparer.Ordinal);
        var targetsBySource = nodeIds.ToDictionary(
            nodeId => nodeId,
            _ => new List<string>(),
            StringComparer.Ordinal);

        foreach (var edge in edges)
        {
            targetsBySource[edge.From].Add(edge.To);
            incomingCount[edge.To]++;
        }

        var ready = new SortedSet<string>(
            incomingCount
                .Where(entry => entry.Value == 0)
                .Select(entry => entry.Key),
            StringComparer.Ordinal);
        var visitedCount = 0;

        while (ready.Count > 0)
        {
            var nodeId = ready.Min!;
            ready.Remove(nodeId);
            visitedCount++;

            foreach (var targetId in targetsBySource[nodeId])
            {
                incomingCount[targetId]--;
                if (incomingCount[targetId] == 0)
                {
                    ready.Add(targetId);
                }
            }
        }

        return visitedCount != nodeIds.Count;
    }
}
