using Backend.Calculation;
using Backend.Models.Domain;
using Backend.Models.Dto;
using Backend.Repositories;
using Backend.Seeding;

namespace Backend.Services;

public class GraphService : IGraphService
{
    private readonly IGraphRepository _graphRepository;

    // Legacy likelihood-ratio ranking, robustness, and counter analytics.
    private readonly GraphLikelihoodCalculator _calculator;

    // BF-based pruning, recurrence, and persisted posterior-log-odds updates.
    private readonly GraphPosteriorOddsCalculator _posteriorOddsCalculator;

    public GraphService(
        IGraphRepository graphRepository,
        GraphLikelihoodCalculator graphLikelihoodCalculator)
        : this(
            graphRepository,
            graphLikelihoodCalculator,
            new GraphPosteriorOddsCalculator())
    {
    }

    public GraphService(
        IGraphRepository graphRepository,
        GraphLikelihoodCalculator graphLikelihoodCalculator,
        GraphPosteriorOddsCalculator posteriorOddsCalculator)
    {
        _graphRepository = graphRepository;
        _calculator = graphLikelihoodCalculator;
        _posteriorOddsCalculator = posteriorOddsCalculator;
    }

    public async Task<IReadOnlyList<GraphSummaryDto>> GetSummariesAsync(
        CancellationToken cancellationToken = default)
    {
        var summaries = await _graphRepository.GetSummariesAsync(cancellationToken);

        return summaries
            .Select(summary => new GraphSummaryDto
            {
                Slug = summary.Slug,
                Title = summary.Title,
                Description = summary.Description,
                NodeCount = summary.NodeCount,
                EdgeCount = summary.EdgeCount
            })
            .ToList();
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
                    PriorOdds = node.PriorOdds,
                    PosteriorOdds = node.PosteriorOdds,
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
                    ProbabilityGivenParent = edge.ProbabilityGivenParent,
                    ProbabilityGivenNotParent = edge.ProbabilityGivenNotParent
                })
                .ToList()
        };
    }

    public async Task<List<string>?> GetMinimalCounterSetAsync(
        string slug,
        string targetNodeId,
        CancellationToken cancellationToken = default)
    {
        var graph = await _graphRepository.GetBySlugAsync(slug, cancellationToken);
        if (graph is null)
        {
            return null;
        }

        return GetMinimalCounterSet(
            graph,
            targetNodeId,
            graph.Nodes.Select(node => node.Id),
            cancellationToken);
    }

    public Task<List<string>?> GetMinimalCounterSetAsync(
        string slug,
        string targetNodeId,
        GraphDto graphContext,
        CancellationToken cancellationToken = default)
    {
        if (!string.Equals(slug, graphContext.Slug, StringComparison.Ordinal))
        {
            return Task.FromResult<List<string>?>(null);
        }

        var graph = ToDomainGraph(graphContext);

        return Task.FromResult(GetMinimalCounterSet(
            graph,
            targetNodeId,
            graph.Nodes.Select(node => node.Id),
            cancellationToken));
    }

    public NodeRobustnessDto? GetLeastRobustNode(
        Graph graph,
        CancellationToken cancellationToken = default
    )
    {
        var robustnessValues = _calculator.GetAllNodeRobustness(graph, cancellationToken);
        if (robustnessValues.Count == 0)
        {
            return null;
        }

        var leastRobust = robustnessValues.MinBy(entry => entry.Value);
        var node = graph.Nodes.First(node => node.Id == leastRobust.Key);

        return new NodeRobustnessDto
        {
            NodeId = node.Id,
            NodeTitle = node.Title,
            Robustness = leastRobust.Value
        };
    }

    public List<NodeRobustnessDto> GetNodeRobustnessRanking(
        Graph graph,
        CancellationToken cancellationToken = default)
    {
        var nodesById = graph.Nodes.ToDictionary(node => node.Id, StringComparer.Ordinal);

        return _calculator.GetAllNodeRobustness(graph, cancellationToken)
            .OrderBy(entry => entry.Value)
            .ThenBy(entry => entry.Key, StringComparer.Ordinal)
            .Select(entry => new NodeRobustnessDto
            {
                NodeId = entry.Key,
                NodeTitle = nodesById[entry.Key].Title,
                Robustness = entry.Value
            })
            .ToList();
    }

    public async Task<NodeRobustnessDto?> GetLeastRobustNodeAsync(
        string slug,
        CancellationToken cancellationToken = default)
    {
        var graph = await _graphRepository.GetBySlugAsync(slug, cancellationToken);
        return graph is null ? null : GetLeastRobustNode(graph, cancellationToken);
    }

    public Task<NodeRobustnessDto?> GetLeastRobustNodeAsync(
        string slug,
        GraphDto graphContext,
        CancellationToken cancellationToken = default)
    {
        if (!string.Equals(slug, graphContext.Slug, StringComparison.Ordinal))
        {
            return Task.FromResult<NodeRobustnessDto?>(null);
        }

        return Task.FromResult(GetLeastRobustNode(ToDomainGraph(graphContext), cancellationToken));
    }

    public async Task<List<NodeRobustnessDto>?> GetNodeRobustnessRankingAsync(
        string slug,
        CancellationToken cancellationToken = default)
    {
        var graph = await _graphRepository.GetBySlugAsync(slug, cancellationToken);
        return graph is null ? null : GetNodeRobustnessRanking(graph, cancellationToken);
    }

    public Task<List<NodeRobustnessDto>?> GetNodeRobustnessRankingAsync(
        string slug,
        GraphDto graphContext,
        CancellationToken cancellationToken = default)
    {
        if (!string.Equals(slug, graphContext.Slug, StringComparison.Ordinal))
        {
            return Task.FromResult<List<NodeRobustnessDto>?>(null);
        }

        return Task.FromResult<List<NodeRobustnessDto>?>(
            GetNodeRobustnessRanking(ToDomainGraph(graphContext), cancellationToken));
    }

    public async Task<EvidenceImpactRankingDto?> GetEvidenceImpactRankingAsync(
        string slug,
        string targetNodeId,
        CancellationToken cancellationToken = default)
    {
        var graph = await _graphRepository.GetBySlugAsync(slug, cancellationToken);
        return graph is null
            ? null
            : _calculator.GetEvidenceImpactRanking(graph, targetNodeId, cancellationToken);
    }

    public Task<EvidenceImpactRankingDto?> GetEvidenceImpactRankingAsync(
        string slug,
        string targetNodeId,
        GraphDto graphContext,
        CancellationToken cancellationToken = default)
    {
        if (!string.Equals(slug, graphContext.Slug, StringComparison.Ordinal))
        {
            return Task.FromResult<EvidenceImpactRankingDto?>(null);
        }

        var graph = ToDomainGraph(graphContext);
        return Task.FromResult<EvidenceImpactRankingDto?>(
            _calculator.GetEvidenceImpactRanking(graph, targetNodeId, cancellationToken));
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

    private static Graph ToDomainGraph(GraphDto graphDto)
    {
        return new Graph
        {
            Slug = graphDto.Slug,
            Title = graphDto.Title,
            Description = graphDto.Description,
            Nodes = graphDto.Nodes.Select(node => new GraphNode
            {
                Id = node.Id,
                Kind = node.Kind,
                Title = node.Title,
                BodyText = node.BodyText,
                Category = node.Category,
                Tags = node.Tags.ToList(),
                PriorOdds = node.PriorOdds,
                PosteriorOdds = node.PosteriorOdds,
                Evidence = node.Evidence is null ? null : new GraphEvidenceDetails
                {
                    Type = node.Evidence.Type,
                    Score = node.Evidence.Score,
                    Rationale = node.Evidence.Rationale
                }
            }).ToList(),
            Edges = graphDto.Edges.Select(edge => new GraphEdge
            {
                Id = edge.Id,
                From = edge.From,
                To = edge.To,
                Kind = edge.Kind,
                ProbabilityGivenParent = edge.ProbabilityGivenParent,
                ProbabilityGivenNotParent = edge.ProbabilityGivenNotParent
            }).ToList()
        };
    }

    public async Task<bool> AddNodeAsync(
        string slug,
        GraphNodeDto node,
        string? parentID = null,
        string edgeKind = "support",
        decimal probabilityGivenParent = 0.5m,
        decimal probabilityGivenNotParent = 0.5m,
        CancellationToken cancellationToken = default)
    {
        // Evidence-like nodes treat the authored likelihood as posterior
        // evidence strength relative to neutral prior log odds.
        if (IsEvidenceLikeNodeKind(node.Kind))
        {
            node.PriorOdds = 0m;
        }

        var added = await _graphRepository.AddNodeAsync(
            slug,
            node,
            parentID,
            edgeKind,
            probabilityGivenParent,
            probabilityGivenNotParent,
            cancellationToken);
        if (!added)
        {
            return false;
        }

        await RecalculateAndPersistNodesAndAncestorsAsync(
            slug,
            [node.Id],
            cancellationToken);

        return true;
    }

    private static bool IsEvidenceLikeNodeKind(string kind)
    {
        return string.Equals(kind, "evidence", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(kind, "objection", StringComparison.OrdinalIgnoreCase);
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

        // Kind changes path eligibility and the evidence-leaf base case. Odds
        // changes affect either the target prior or its authored leaf log BF.
        if (node.Kind is not null ||
            node.PriorOdds.HasValue ||
            node.PosteriorOdds.HasValue)
        {
            await RecalculateAndPersistNodesAndAncestorsAsync(
                slug,
                [nodeId],
                cancellationToken);
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

        // Either probability changes both the derived pruning LR and the BF
        // transform on the retained edge.
        if (edge.ProbabilityGivenParent.HasValue ||
            edge.ProbabilityGivenNotParent.HasValue)
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

    public async Task ResetDatabaseAsync(
        IReadOnlyCollection<string> stressGraphIds,
        CancellationToken cancellationToken = default)
    {
        var stressGraphs = StressGraphSeedCatalog.Resolve(stressGraphIds);
        await _graphRepository.ResetDatabaseAsync(stressGraphs, cancellationToken);
    }

    /// <summary>Loads a graph, recalculates ancestors, and persists the result.</summary>
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

    /// <summary>Recalculates and batch-persists ancestors, excluding the changed node.</summary>
    private async Task<IReadOnlyDictionary<string, decimal>> RecalculateAndPersistAncestorsAsync(
        Graph graph,
        string changedNodeId,
        CancellationToken cancellationToken)
    {
        var recalculatedLogOdds = _posteriorOddsCalculator.RecalculateAncestors(
            graph,
            changedNodeId,
            cancellationToken);

        if (recalculatedLogOdds.Count > 0)
        {
            await _graphRepository.UpdateNodePosteriorOddsBatchAsync(graph.Id, recalculatedLogOdds, cancellationToken);
        }

        return recalculatedLogOdds;
    }

    /// <summary>Loads a graph, recalculates supplied nodes and ancestors, and persists them.</summary>
    private async Task<IReadOnlyDictionary<string, decimal>> RecalculateAndPersistNodesAndAncestorsAsync(
        string slug,
        IEnumerable<string> nodeIds,
        CancellationToken cancellationToken)
    {
        var graph = await _graphRepository.GetBySlugAsync(slug, cancellationToken);
        if (graph is null)
        {
            return new Dictionary<string, decimal>();
        }

        return await RecalculateAndPersistNodesAndAncestorsAsync(
            graph,
            nodeIds,
            cancellationToken);
    }

    /// <summary>Recalculates and batch-persists supplied nodes and their ancestors.</summary>
    private async Task<IReadOnlyDictionary<string, decimal>> RecalculateAndPersistNodesAndAncestorsAsync(
        Graph graph,
        IEnumerable<string> nodeIds,
        CancellationToken cancellationToken)
    {
        var recalculatedLogOdds =
            _posteriorOddsCalculator.RecalculateNodesAndAncestors(
                graph,
                nodeIds,
                cancellationToken);

        if (recalculatedLogOdds.Count > 0)
        {
            await _graphRepository.UpdateNodePosteriorOddsBatchAsync(graph.Id, recalculatedLogOdds, cancellationToken);
        }

        return recalculatedLogOdds;
    }

    private List<string>? GetMinimalCounterSet(
        Graph graph,
        string targetClaimId,
        IEnumerable<string> nodeIds,
        CancellationToken cancellationToken
    )
    {
        const decimal targetLogOddsThreshold = -1m;

        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(targetClaimId);
        ArgumentNullException.ThrowIfNull(nodeIds);
        cancellationToken.ThrowIfCancellationRequested();

        var nodesById = graph.Nodes.ToDictionary(
            node => node.Id,
            StringComparer.Ordinal);
        if (!nodesById.ContainsKey(targetClaimId))
        {
            throw new InvalidOperationException(
                $"Target node '{targetClaimId}' does not exist in the graph.");
        }

        var availableNodeIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (string nodeId in nodeIds.Distinct(StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!nodesById.ContainsKey(nodeId))
            {
                throw new InvalidOperationException(
                    $"Node '{nodeId}' does not exist in the graph.");
            }

            availableNodeIds.Add(nodeId);
        }

        availableNodeIds.Add(targetClaimId);

        var counterNodeIds = availableNodeIds
            .Where(nodeId =>
                nodeId != targetClaimId &&
                IsCounterNode(nodesById[nodeId]))
            .ToList();
        var includedNodeIds = availableNodeIds
            .Where(nodeId => !IsCounterNode(nodesById[nodeId]))
            .ToHashSet(StringComparer.Ordinal);
        includedNodeIds.Add(targetClaimId);

        decimal targetClaimLogOdds = CalculateTargetLogOdds(
            graph,
            targetClaimId,
            includedNodeIds,
            cancellationToken);
        if (targetClaimLogOdds <= targetLogOddsThreshold)
        {
            return [];
        }

        // Rank once against the no-counter graph. The BF calculation includes
        // pruning and every nonlinear edge transform, so the lowest resulting
        // target log odds is the strongest counter.
        var rankedCounters = new List<(string NodeId, decimal TargetLogOdds)>(
            counterNodeIds.Count);
        foreach (string counterNodeId in counterNodeIds)
        {
            cancellationToken.ThrowIfCancellationRequested();
            includedNodeIds.Add(counterNodeId);
            decimal candidateTargetLogOdds = CalculateTargetLogOdds(
                graph,
                targetClaimId,
                includedNodeIds,
                cancellationToken);
            includedNodeIds.Remove(counterNodeId);
            rankedCounters.Add((counterNodeId, candidateTargetLogOdds));
        }

        var countersUsed = new List<string>();
        foreach (var counter in rankedCounters
                     .OrderBy(counter => counter.TargetLogOdds)
                     .ThenBy(counter => counter.NodeId, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            includedNodeIds.Add(counter.NodeId);
            countersUsed.Add(counter.NodeId);
            targetClaimLogOdds = CalculateTargetLogOdds(
                graph,
                targetClaimId,
                includedNodeIds,
                cancellationToken);

            if (targetClaimLogOdds <= targetLogOddsThreshold)
            {
                return countersUsed;
            }
        }

        return null;
    }

    private decimal CalculateTargetLogOdds(
        Graph source,
        string targetClaimId,
        HashSet<string> includedNodeIds,
        CancellationToken cancellationToken)
    {
        var graph = new Graph
        {
            Id = source.Id,
            Slug = source.Slug,
            Title = source.Title,
            Description = source.Description,
            Nodes = source.Nodes
                .Where(node => includedNodeIds.Contains(node.Id))
                .ToList(),
            Edges = source.Edges
                .Where(edge =>
                    includedNodeIds.Contains(edge.From) &&
                    includedNodeIds.Contains(edge.To))
                .ToList()
        };

        return _posteriorOddsCalculator.CalculateNodeLogPosteriorOdds(
            graph,
            targetClaimId,
            cancellationToken);
    }

    private static bool IsCounterNode(GraphNode node)
    {
        return string.Equals(node.Kind, "objection", StringComparison.OrdinalIgnoreCase)
            || string.Equals(node.Kind, "counter", StringComparison.OrdinalIgnoreCase);
    }
}
