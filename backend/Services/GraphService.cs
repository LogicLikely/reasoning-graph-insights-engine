using Backend.Calculation;
using Backend.Models.Domain;
using Backend.Models.Dto;
using Backend.Repositories;

namespace Backend.Services;

public class GraphService : IGraphService
{
    private readonly IGraphRepository _graphRepository;
    private readonly GraphLikelihoodCalculator _calculator;

    public GraphService(
        IGraphRepository graphRepository,
        GraphLikelihoodCalculator graphLikelihoodCalculator)
    {
        _graphRepository = graphRepository;
        _calculator = graphLikelihoodCalculator;
    }

    public async Task<GraphDto?> GetBySlugAsync(
        string slug,
        CancellationToken cancellationToken = default)
    {
        var graph = await _graphRepository.GetBySlugAsync(slug, cancellationToken);

        if (graph is null)
        {
            return null;
        }

        return new GraphDto
        {
            Slug = graph.Slug,
            Title = graph.Title,
            Description = graph.Description,
            Nodes = graph.Nodes
                .Select(node => new GraphNodeDto
                {
                    Id = node.Id,
                    Kind = node.Kind,
                    Title = node.Title,
                    BodyText = node.BodyText,
                    Category = node.Category,
                    Tags = node.Tags.ToList(),
                    LogOdds = node.LogOdds,
                    Evidence = node.Evidence == null ? null : new GraphEvidenceDto
                    {
                        Type = node.Evidence.Type,
                        Score = node.Evidence.Score,
                        Rationale = node.Evidence.Rationale
                    }
                })
                .ToList(),
            Edges = graph.Edges
                .Select(edge => new GraphEdgeDto
                {
                    Id = edge.Id,
                    From = edge.From,
                    To = edge.To,
                    Kind = edge.Kind,
                    ImportanceToParent = edge.ImportanceToParent
                })
                .ToList()
        };
    }

    public async Task<bool> DeleteNodeAsync(
        string slug,
        string nodeId,
        CancellationToken cancellationToken = default)
    {
        var graph = await _graphRepository.GetBySlugAsync(slug, cancellationToken);
        if (graph is null) return false;

        // Check if node has incoming edges (IN neighbors)
        if (graph.Edges.Any(e => e.To == nodeId))
        {
            return false;   // Business Rule: Cannot delete a node that currently has 
                            // child support/counter nodes beneath it.
        }

        var parentNodeIds = graph.Edges
            .Where(edge => edge.From == nodeId)
            .Select(edge => edge.To)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        var deleted = await _graphRepository.DeleteNodeAsync(slug, nodeId, cancellationToken);
        if (!deleted)
        {
            return false;
        }

        if (parentNodeIds.Count > 0)
        {
            var updatedGraph = await _graphRepository.GetBySlugAsync(slug, cancellationToken);
            if (updatedGraph is not null)
            {
                await RecalculateAndPersistNodesAndAncestorsAsync(updatedGraph, parentNodeIds, cancellationToken);
            }
        }

        return true;
    }

    public async Task<bool> AddNodeAsync(
        string slug,
        GraphNodeDto node,
        string? parentID = null,
        string edgeKind = "support",
        int importanceToParent = 1,
        CancellationToken cancellationToken = default)
    {
        var added = await _graphRepository.AddNodeAsync(slug, node, parentID, edgeKind, importanceToParent, cancellationToken);
        if (!added)
        {
            return false;
        }

        if (!string.IsNullOrEmpty(parentID))
        {
            await RecalculateAndPersistAncestorsAsync(slug, node.Id, cancellationToken);
        }

        return true;
    }

    public async Task<bool> UpdateNodeAsync(
        string slug,
        string nodeId,
        GraphNodeUpdateDto node,
        CancellationToken cancellationToken = default)
    {
        var updated = await _graphRepository.UpdateNodeAsync(slug, nodeId, node, cancellationToken);
        if (!updated)
        {
            return false;
        }

        if (node.LogOdds.HasValue)
        {
            await RecalculateAndPersistAncestorsAsync(slug, nodeId, cancellationToken);
        }

        return true;
    }

    public async Task<bool> AddEdgeAsync(
        string slug,
        GraphEdgeDto edge,
        CancellationToken cancellationToken = default)
    {
        var added = await _graphRepository.AddEdgeAsync(slug, edge, cancellationToken);
        if (!added)
        {
            return false;
        }

        await RecalculateAndPersistAncestorsAsync(slug, edge.From, cancellationToken);

        return true;
    }

    public async Task<bool> UpdateEdgeAsync(
        string slug,
        string edgeId,
        GraphEdgeUpdateDto edge,
        CancellationToken cancellationToken = default)
    {
        var updated = await _graphRepository.UpdateEdgeAsync(slug, edgeId, edge, cancellationToken);
        if (!updated)
        {
            return false;
        }

        if (edge.ImportanceToParent.HasValue)
        {
            var graph = await _graphRepository.GetBySlugAsync(slug, cancellationToken);
            var updatedEdge = graph?.Edges.FirstOrDefault(candidate => candidate.Id == edgeId);
            if (graph is not null && updatedEdge is not null)
            {
                await RecalculateAndPersistAncestorsAsync(graph, updatedEdge.From, cancellationToken);
            }
        }

        return true;
    }

    public async Task ResetDatabaseAsync(CancellationToken cancellationToken = default)
    {
        await _graphRepository.ResetDatabaseAsync(cancellationToken);
    }

    private async Task<IReadOnlyDictionary<string, decimal>> RecalculateAndPersistAncestorsAsync(
        string slug,
        string changedNodeId,
        CancellationToken cancellationToken)
    {
        var graph = await _graphRepository.GetBySlugAsync(slug, cancellationToken);
        if (graph is null)
        {
            return new Dictionary<string, decimal>();
        }

        return await RecalculateAndPersistAncestorsAsync(graph, changedNodeId, cancellationToken);
    }

    private async Task<IReadOnlyDictionary<string, decimal>> RecalculateAndPersistAncestorsAsync(
        Graph graph,
        string changedNodeId,
        CancellationToken cancellationToken)
    {
        var context = GraphCalculationContext.From(graph.Nodes, graph.Edges);
        var recalculatedLogOdds = _calculator.RecalculateAncestors(context, changedNodeId);

        if (recalculatedLogOdds.Count > 0)
        {
            await _graphRepository.UpdateNodeLogOddsBatchAsync(graph.Id, recalculatedLogOdds, cancellationToken);
        }

        return recalculatedLogOdds;
    }

    private async Task<IReadOnlyDictionary<string, decimal>> RecalculateAndPersistNodesAndAncestorsAsync(
        Graph graph,
        IEnumerable<string> nodeIds,
        CancellationToken cancellationToken)
    {
        var context = GraphCalculationContext.From(graph.Nodes, graph.Edges);
        var recalculatedLogOdds = _calculator.RecalculateNodesAndAncestors(context, nodeIds);

        if (recalculatedLogOdds.Count > 0)
        {
            await _graphRepository.UpdateNodeLogOddsBatchAsync(graph.Id, recalculatedLogOdds, cancellationToken);
        }

        return recalculatedLogOdds;
    }
    private async Task<List<GraphEdgeCalcState>> GetMinimalCounterSet(
        Graph graph,
        string targetNodeId,
        IEnumerable<string> nodeIds,
        CancellationToken cancellationToken
    )
    {
        var context = GraphCalculationContext.From(graph.Nodes, graph.Edges);
        // registerdNodeIds starts by not including any counter evidence, adding counters 1 by 1
        var registerdNodeIds = ExcludeCounterNodes(context, nodeIds);
        var counterQueue = GetCounterQueue(context,targetNodeId, nodeIds);
        //Calculates odds only consider
        var recalculatedLogOdds = _calculator.RecalculateNodesAndAncestors(context, registerdNodeIds);

        throw new NotImplementedException("GetMinimalCounterSet is still in progress.");
    }

    private static PriorityQueue<string, decimal> GetCounterQueue(
        GraphCalculationContext context,
        string targetNodeId,
        IEnumerable<string> nodeIds)
    {
        if (!context.NodesById.ContainsKey(targetNodeId))
        {
            throw new InvalidOperationException($"Target node '{targetNodeId}' does not exist in the calculation context.");
        }

        var counterQueue = new PriorityQueue<string, decimal>(
            Comparer<decimal>.Create((left, right) => right.CompareTo(left)));

        foreach (var nodeId in nodeIds.Distinct(StringComparer.Ordinal))
        {
            if (!context.NodesById.TryGetValue(nodeId, out var node))
            {
                throw new InvalidOperationException($"Node '{nodeId}' does not exist in the calculation context.");
            }

            if (!IsCounterNode(node))
            {
                continue;
            }

            var multiplier = GetAncestorImportanceMultiplier(context, nodeId, targetNodeId);
            if (multiplier is null)
            {
                continue;
            }

            counterQueue.Enqueue(nodeId, node.LogOdds * multiplier.Value);
        }

        return counterQueue;
    }

    private static decimal? GetAncestorImportanceMultiplier(
        GraphCalculationContext context,
        string startNodeId,
        string targetNodeId)
    {
        var stack = new Stack<CounterTraversalState>();
        stack.Push(new CounterTraversalState(startNodeId, 1m, [startNodeId]));

        decimal? bestMultiplier = null;
        while (stack.Count > 0)
        {
            var current = stack.Pop();
            if (current.NodeId == targetNodeId)
            {
                bestMultiplier = bestMultiplier is null
                    ? current.Multiplier
                    : Math.Max(bestMultiplier.Value, current.Multiplier);
                continue;
            }

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
                        $"Cycle detected while finding counter priority at node '{parentNodeId}'.");
                }

                var nextMultiplier = current.Multiplier * (parentEdge.ImportanceToParent / 10m);
                var nextPath = new HashSet<string>(current.Path) { parentNodeId };
                stack.Push(new CounterTraversalState(parentNodeId, nextMultiplier, nextPath));
            }
        }

        return bestMultiplier;
    }

    private static bool IsCounterNode(GraphNodeCalcState node)
    {
        return string.Equals(node.Kind, "objection", StringComparison.OrdinalIgnoreCase)
            || string.Equals(node.Kind, "counter", StringComparison.OrdinalIgnoreCase);
    }

    private static List<string> ExcludeCounterNodes(
        GraphCalculationContext context,
        IEnumerable<string> nodeIds)
    {
        return nodeIds
            .Where(id =>
            {
                if (!context.NodesById.TryGetValue(id, out var node))
                {
                    throw new InvalidOperationException($"Node '{id}' does not exist in the calculation context.");
                }

                return !IsCounterNode(node);
            })
            .ToList();
    }

    private sealed record CounterTraversalState(
        string NodeId,
        decimal Multiplier,
        HashSet<string> Path);
}
