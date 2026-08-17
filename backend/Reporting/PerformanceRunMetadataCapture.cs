using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Backend.Models.Domain;
using Backend.Seeding;

namespace Backend.Reporting;

public static class PerformanceRunMetadataCapture
{
    private static readonly JsonSerializerOptions FingerprintJsonOptions =
        new(JsonSerializerDefaults.Web);

    public static PerformanceGraphInfo CaptureGraph(Graph graph)
    {
        ArgumentNullException.ThrowIfNull(graph);

        var stressSpec = StressGraphSeedCatalog.All.FirstOrDefault(spec =>
            string.Equals(spec.Slug, graph.Slug, StringComparison.Ordinal));

        var nodeKindCounts = graph.Nodes
            .GroupBy(
                node => node.Kind.ToLowerInvariant(),
                StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.Count(),
                StringComparer.Ordinal);

        return new PerformanceGraphInfo
        {
            Slug = graph.Slug,
            Type = stressSpec?.Shape ?? GetNonStressGraphType(graph.Slug),
            NodeCount = graph.Nodes.Count,
            EdgeCount = graph.Edges.Count,
            MaximumDepth = stressSpec?.MaximumDepth,
            NodeKindCounts = nodeKindCounts,
            Fingerprint = CalculateGraphFingerprint(graph)
        };
    }

    public static string CalculateResultDigest<T>(T result)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(
            result,
            FingerprintJsonOptions);
        return $"sha256:{Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant()}";
    }

    public static JsonNode? ToJsonNode<T>(T value)
    {
        return JsonSerializer.SerializeToNode(value, FingerprintJsonOptions);
    }

    public static int CountLeafNodes(Graph graph)
    {
        ArgumentNullException.ThrowIfNull(graph);
        var nodeIdsWithChildren = graph.Edges
            .Select(edge => edge.To)
            .ToHashSet(StringComparer.Ordinal);
        return graph.Nodes.Count(node => !nodeIdsWithChildren.Contains(node.Id));
    }

    public static int CountReachableNodes(
        Graph graph,
        string targetNodeId)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetNodeId);

        var childNodeIdsByParentId = graph.Edges
            .GroupBy(edge => edge.To, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.Select(edge => edge.From).ToArray(),
                StringComparer.Ordinal);
        var reachable = new HashSet<string>(StringComparer.Ordinal);
        var pending = new Stack<string>();
        pending.Push(targetNodeId);

        while (pending.Count > 0)
        {
            var current = pending.Pop();
            if (!reachable.Add(current) ||
                !childNodeIdsByParentId.TryGetValue(current, out var childNodeIds))
            {
                continue;
            }

            foreach (var childNodeId in childNodeIds)
            {
                pending.Push(childNodeId);
            }
        }

        return reachable.Count;
    }

    public static int GetMaximumAncestorDistance(
        Graph graph,
        string changedNodeId)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentException.ThrowIfNullOrWhiteSpace(changedNodeId);

        var parentNodeIdsByChildId = graph.Edges
            .GroupBy(edge => edge.From, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.Select(edge => edge.To).ToArray(),
                StringComparer.Ordinal);

        var reachableNodeIds = new HashSet<string>(StringComparer.Ordinal)
        {
            changedNodeId
        };
        var pending = new Stack<string>();
        pending.Push(changedNodeId);

        while (pending.Count > 0)
        {
            var current = pending.Pop();
            if (!parentNodeIdsByChildId.TryGetValue(current, out var parentNodeIds))
            {
                continue;
            }

            foreach (var parentNodeId in parentNodeIds)
            {
                if (reachableNodeIds.Add(parentNodeId))
                {
                    pending.Push(parentNodeId);
                }
            }
        }

        var incomingEdgeCounts = reachableNodeIds.ToDictionary(
            nodeId => nodeId,
            _ => 0,
            StringComparer.Ordinal);

        foreach (var childNodeId in reachableNodeIds)
        {
            if (!parentNodeIdsByChildId.TryGetValue(childNodeId, out var parentNodeIds))
            {
                continue;
            }

            foreach (var parentNodeId in parentNodeIds)
            {
                incomingEdgeCounts[parentNodeId]++;
            }
        }

        var ready = new Queue<string>(incomingEdgeCounts
            .Where(entry => entry.Value == 0)
            .Select(entry => entry.Key));
        var distances = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            [changedNodeId] = 0
        };
        var maximumDistance = 0;
        var processedNodeCount = 0;

        while (ready.Count > 0)
        {
            var childNodeId = ready.Dequeue();
            processedNodeCount++;
            var childDistance = distances.GetValueOrDefault(childNodeId, int.MinValue);

            if (!parentNodeIdsByChildId.TryGetValue(childNodeId, out var parentNodeIds))
            {
                continue;
            }

            foreach (var parentNodeId in parentNodeIds)
            {
                if (childDistance != int.MinValue)
                {
                    var parentDistance = childDistance + 1;
                    if (!distances.TryGetValue(parentNodeId, out var currentDistance) ||
                        parentDistance > currentDistance)
                    {
                        distances[parentNodeId] = parentDistance;
                        maximumDistance = Math.Max(maximumDistance, parentDistance);
                    }
                }

                incomingEdgeCounts[parentNodeId]--;
                if (incomingEdgeCounts[parentNodeId] == 0)
                {
                    ready.Enqueue(parentNodeId);
                }
            }
        }

        if (processedNodeCount != reachableNodeIds.Count)
        {
            throw new InvalidOperationException(
                $"Cannot calculate ancestor distance from node '{changedNodeId}' " +
                "because its reachable ancestor graph contains a cycle.");
        }

        return maximumDistance;
    }

    private static string CalculateGraphFingerprint(Graph graph)
    {
        var canonicalGraph = new
        {
            graph.Slug,
            Nodes = graph.Nodes.OrderBy(node => node.Id, StringComparer.Ordinal),
            Edges = graph.Edges.OrderBy(edge => edge.Id, StringComparer.Ordinal)
        };
        var bytes = JsonSerializer.SerializeToUtf8Bytes(
            canonicalGraph,
            FingerprintJsonOptions);
        return $"sha256:{Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant()}";
    }

    private static string GetNonStressGraphType(string slug)
    {
        return slug switch
        {
            "sample-medium" => "sample",
            "flat-earth-large" => "large-example",
            _ => "uncatalogued"
        };
    }
}
