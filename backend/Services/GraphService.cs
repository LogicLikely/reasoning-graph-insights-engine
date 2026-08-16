using Backend.Calculation;
using Backend.Insights.Analysis;
using Backend.Insights.Contracts;
using Backend.Insights.Measurement;
using Backend.Models.Domain;
using Backend.Models.Dto;
using Backend.Repositories;
using Backend.Seeding;

namespace Backend.Services;

public class GraphService : IGraphService
{
    private readonly IGraphRepository _graphRepository;
    private readonly GraphLikelihoodCalculator _calculator;
    private readonly IInsightPhaseTimingCollector _phaseTimings;
    private readonly EvidenceImpactV0Analysis _evidenceImpactAnalysis;
    private readonly RobustnessV0Analyzer _robustnessAnalysis;

    public GraphService(
        IGraphRepository graphRepository,
        GraphLikelihoodCalculator graphLikelihoodCalculator,
        IInsightPhaseTimingCollector? phaseTimings = null,
        EvidenceImpactV0Analysis? evidenceImpactAnalysis = null,
        RobustnessV0Analyzer? robustnessAnalysis = null)
    {
        _graphRepository = graphRepository;
        _calculator = graphLikelihoodCalculator;
        _phaseTimings = phaseTimings ?? new InsightPhaseTimingCollector();
        _evidenceImpactAnalysis = evidenceImpactAnalysis ?? new EvidenceImpactV0Analysis();
        _robustnessAnalysis = robustnessAnalysis ?? new RobustnessV0Analyzer();
    }

    public async Task<IReadOnlyList<GraphSummaryDto>> GetSummariesAsync(
        CancellationToken cancellationToken = default)
    {
        var summaries = await _graphRepository.GetSummariesAsync(cancellationToken);

        using (_phaseTimings.Measure(
                   InsightMeasurementLayers.BackendServiceApi,
                   InsightMeasurementPhases.DtoMapping))
        {
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

        using (_phaseTimings.Measure(
                   InsightMeasurementLayers.BackendServiceApi,
                   InsightMeasurementPhases.DtoMapping))
        {
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
                        ImportanceToParent = edge.ImportanceToParent
                    })
                    .ToList()
            };
        }
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

    public async Task<List<string>?> GetMinimalCounterSetAsync(
        string slug,
        string targetNodeId,
        GraphDto graphContext,
        CancellationToken cancellationToken = default)
    {
        if (!string.Equals(slug, graphContext.Slug, StringComparison.Ordinal))
        {
            return null;
        }

        var graph = ToDomainGraphWithTiming(graphContext);

        return await Task.FromResult(
            GetMinimalCounterSet(
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
        var result = _robustnessAnalysis.Analyze(graph, _phaseTimings, cancellationToken);
        if (result.LeastRobust is null)
        {
            return null;
        }

        using (_phaseTimings.Measure(
                   InsightMeasurementLayers.BackendServiceApi,
                   InsightMeasurementPhases.DtoMapping))
        {
            return new NodeRobustnessDto
            {
                NodeId = result.LeastRobust.NodeId,
                NodeTitle = result.LeastRobust.Title,
                Robustness = result.LeastRobust.RobustnessScore
            };
        }
    }

    public List<NodeRobustnessDto> GetNodeRobustnessRanking(
        Graph graph,
        CancellationToken cancellationToken = default)
    {
        var result = _robustnessAnalysis.Analyze(graph, _phaseTimings, cancellationToken);
        using (_phaseTimings.Measure(
                   InsightMeasurementLayers.BackendServiceApi,
                   InsightMeasurementPhases.DtoMapping))
        {
            return result.Ranking
                .Select(item => new NodeRobustnessDto
                {
                    NodeId = item.NodeId,
                    NodeTitle = item.Title,
                    Robustness = item.RobustnessScore
                })
                .ToList();
        }
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

        return Task.FromResult(
            GetLeastRobustNode(ToDomainGraphWithTiming(graphContext), cancellationToken));
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
            GetNodeRobustnessRanking(ToDomainGraphWithTiming(graphContext), cancellationToken));
    }

    public async Task<EvidenceImpactRankingDto?> GetEvidenceImpactRankingAsync(
        string slug,
        string targetNodeId,
        CancellationToken cancellationToken = default)
    {
        var graph = await _graphRepository.GetBySlugAsync(slug, cancellationToken);
        return graph is null
            ? null
            : GetLegacyEvidenceImpactRanking(graph, targetNodeId, cancellationToken);
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

        var graph = ToDomainGraphWithTiming(graphContext);
        return Task.FromResult<EvidenceImpactRankingDto?>(
            GetLegacyEvidenceImpactRanking(graph, targetNodeId, cancellationToken));
    }

    private EvidenceImpactRankingDto GetLegacyEvidenceImpactRanking(
        Graph graph,
        string targetNodeId,
        CancellationToken cancellationToken)
    {
        bool useRichAnalysis;
        using (_phaseTimings.Measure(
                   InsightMeasurementLayers.BackendServiceApi,
                   InsightMeasurementPhases.Validation))
        {
            cancellationToken.ThrowIfCancellationRequested();
            // Preserve the legacy endpoint's exception contract for a missing target
            // while adapting its compact DTO from the versioned rich result.
            if (!graph.Nodes.Any(node => string.Equals(node.Id, targetNodeId, StringComparison.Ordinal)))
            {
                throw new InvalidOperationException(
                    $"Target node '{targetNodeId}' does not exist in the calculation context.");
            }

            // The rich v0 contract makes positive-LR DAG validation explicit. The
            // pre-Phase3 endpoint did not, so retain its scalar behavior for inputs
            // outside that richer execution domain instead of narrowing the legacy
            // route's accepted input set.
            useRichAnalysis = AlgorithmGraphContractValidation
                .ValidateDirectedAcyclicGraph(graph, cancellationToken)
                .IsValid;
        }

        if (!useRichAnalysis)
        {
            using (_phaseTimings.Measure(
                       InsightMeasurementLayers.BackendServiceApi,
                       InsightMeasurementPhases.Algorithm))
            using (_phaseTimings.Measure(
                       InsightMeasurementLayers.BackendServiceApi,
                       InsightMeasurementPhases.AlgorithmSubphase("legacy-scalar-fallback")))
            {
                return _calculator.GetEvidenceImpactRanking(
                    graph,
                    targetNodeId,
                    cancellationToken);
            }
        }

        var result = _evidenceImpactAnalysis.Analyze(
            graph,
            targetNodeId,
            _phaseTimings,
            cancellationToken);

        using (_phaseTimings.Measure(
                   InsightMeasurementLayers.BackendServiceApi,
                   InsightMeasurementPhases.DtoMapping))
        {
            static EvidenceImpactDto ToLegacyDto(EvidenceImpactV0LegacyItem item) => new()
            {
                NodeId = item.NodeId,
                LogLr = item.AccumulatedPathLogLikelihoodRatio,
                ProbabilityDifference = item.RawProbabilityDelta
            };

            return new EvidenceImpactRankingDto
            {
                SupportingEvidence = result.LegacySupportingEvidence.Select(ToLegacyDto).ToList(),
                CounterEvidence = result.LegacyCounterEvidence.Select(ToLegacyDto).ToList()
            };
        }
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
                ImportanceToParent = edge.ImportanceToParent
            }).ToList()
        };
    }

    private Graph ToDomainGraphWithTiming(GraphDto graphDto)
    {
        using (_phaseTimings.Measure(
                   InsightMeasurementLayers.BackendServiceApi,
                   InsightMeasurementPhases.DtoMapping))
        {
            return ToDomainGraph(graphDto);
        }
    }

    public async Task<bool> AddNodeAsync(
        string slug,
        GraphNodeDto node,
        string? parentID = null,
        string edgeKind = "support",
        decimal importanceToParent = 1m,
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

        if (node.PriorOdds.HasValue)
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

    public async Task ResetDatabaseAsync(
        IReadOnlyCollection<string> stressGraphIds,
        CancellationToken cancellationToken = default)
    {
        var stressGraphs = StressGraphSeedCatalog.Resolve(stressGraphIds);
        await _graphRepository.ResetDatabaseAsync(stressGraphs, cancellationToken);
    }

    public async Task ResetDatabaseAsync(
        IReadOnlyCollection<string> stressGraphIds,
        DatabaseResetTargetExpectation targetExpectation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(targetExpectation);
        var stressGraphs = StressGraphSeedCatalog.Resolve(stressGraphIds);
        await _graphRepository.ResetDatabaseAsync(
            stressGraphs,
            targetExpectation,
            cancellationToken);
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
        var context = GraphCalculationContext.From(
            graph.Nodes,
            graph.Edges,
            cancellationToken);
        var recalculatedLogOdds = _calculator.RecalculateAncestors(
            context,
            changedNodeId,
            cancellationToken);

        if (recalculatedLogOdds.Count > 0)
        {
            await _graphRepository.UpdateNodePosteriorOddsBatchAsync(graph.Id, recalculatedLogOdds, cancellationToken);
        }

        return recalculatedLogOdds;
    }

    private async Task<IReadOnlyDictionary<string, decimal>> RecalculateAndPersistNodesAndAncestorsAsync(
        Graph graph,
        IEnumerable<string> nodeIds,
        CancellationToken cancellationToken)
    {
        var context = GraphCalculationContext.From(
            graph.Nodes,
            graph.Edges,
            cancellationToken);
        var recalculatedLogOdds = _calculator.RecalculateNodesAndAncestors(
            context,
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
        cancellationToken.ThrowIfCancellationRequested();
        GraphCalculationContext context;
        using (_phaseTimings.Measure(
                   InsightMeasurementLayers.BackendServiceApi,
                   InsightMeasurementPhases.CalculationContextConstruction))
        {
            context = GraphCalculationContext.From(
                graph.Nodes,
                graph.Edges,
                cancellationToken);
        }

        List<string> countersUsed;
        decimal targetClaimLogOdds;
        using (_phaseTimings.Measure(
                   InsightMeasurementLayers.BackendServiceApi,
                   InsightMeasurementPhases.Algorithm))
        {
            List<string> registeredNodeIds;
            PriorityQueue<string, decimal> counterQueue;
            using (_phaseTimings.Measure(
                       InsightMeasurementLayers.BackendServiceApi,
                       InsightMeasurementPhases.AlgorithmSubphase("candidate-preparation")))
            {
                // registeredNodeIds starts without counter evidence; counters
                // are added one at a time by the frozen heuristic below.
                registeredNodeIds = ExcludeCounterNodes(context, nodeIds, cancellationToken);
                if (!registeredNodeIds.Contains(targetClaimId, StringComparer.Ordinal))
                {
                    registeredNodeIds.Add(targetClaimId);
                }

                counterQueue = GetCounterQueue(
                    context,
                    targetClaimId,
                    nodeIds,
                    cancellationToken);
            }

            IReadOnlyDictionary<string, decimal> normalLogOdds;
            using (_phaseTimings.Measure(
                       InsightMeasurementLayers.BackendServiceApi,
                       InsightMeasurementPhases.AlgorithmSubphase("likelihood-recalculation")))
            {
                // Dictionary mapping log odds to every node (including counters).
                cancellationToken.ThrowIfCancellationRequested();
                normalLogOdds = _calculator.RecalculateNodesAndAncestors(
                    context,
                    nodeIds,
                    cancellationToken);

                // Calculate odds without considering counters.
                cancellationToken.ThrowIfCancellationRequested();
                var recalculatedLogOdds = _calculator.RecalculateNodesAndAncestors(
                    context,
                    registeredNodeIds,
                    cancellationToken);
                if (!recalculatedLogOdds.TryGetValue(targetClaimId, out targetClaimLogOdds))
                {
                    throw new InvalidOperationException(
                        $"Target node '{targetClaimId}' does not exist in the recalculatedLogOdds dictionary.");
                }
            }

            countersUsed = [];
            using (_phaseTimings.Measure(
                       InsightMeasurementLayers.BackendServiceApi,
                       InsightMeasurementPhases.AlgorithmSubphase("threshold-selection")))
            {
                // Walk through every counter in likelihood order and include it
                // in the frozen heuristic's accumulated target odds.
                while (counterQueue.Count > 0 && targetClaimLogOdds > -1)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    string counterNodeId = counterQueue.Dequeue();
                    registeredNodeIds.Add(counterNodeId);
                    countersUsed.Add(counterNodeId);
                    double? counterLikelihoodRatio = (double?)_calculator.GetSingleAccumulatedLR(
                        context,
                        counterNodeId,
                        targetClaimId,
                        cancellationToken);
                    if (!counterLikelihoodRatio.HasValue) continue;

                    decimal logCounterLikelihoodRatio = (decimal)Math.Log(counterLikelihoodRatio.Value);
                    targetClaimLogOdds += normalLogOdds[counterNodeId] + logCounterLikelihoodRatio;
                }
            }
        }

        using (_phaseTimings.Measure(
                   InsightMeasurementLayers.BackendServiceApi,
                   InsightMeasurementPhases.ResultShaping))
        {
            return targetClaimLogOdds > -1 ? null : countersUsed;
        }
    }

    //Get queue of counters ranked by their likelihood
    private static PriorityQueue<string, decimal> GetCounterQueue(
        GraphCalculationContext context,
        string targetNodeId,
        IEnumerable<string> nodeIds,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!context.NodesById.ContainsKey(targetNodeId))
        {
            throw new InvalidOperationException($"Target node '{targetNodeId}' does not exist in the calculation context.");
        }

        var counterQueue = new PriorityQueue<string, decimal>(
            Comparer<decimal>.Create((left, right) => right.CompareTo(left)));

        foreach (var nodeId in nodeIds.Distinct(StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!context.NodesById.TryGetValue(nodeId, out var node))
            {
                throw new InvalidOperationException($"Node '{nodeId}' does not exist in the calculation context.");
            }

            if (nodeId == targetNodeId || !IsCounterNode(node))
            {
                continue;
            }

            var multiplier = GetAncestorImportanceMultiplier(
                context,
                nodeId,
                targetNodeId,
                cancellationToken);
            if (multiplier is null)
            {
                continue;
            }

            counterQueue.Enqueue(nodeId, node.PosteriorOdds * multiplier.Value);
        }

        return counterQueue;
    }

    private static decimal? GetAncestorImportanceMultiplier(
        GraphCalculationContext context,
        string startNodeId,
        string targetNodeId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var stack = new Stack<CounterTraversalState>();
        stack.Push(new CounterTraversalState(startNodeId, 1m, [startNodeId]));

        decimal? bestMultiplier = null;
        while (stack.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
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
                cancellationToken.ThrowIfCancellationRequested();
                var parentNodeId = parentEdge.ToNodeId;
                if (!context.NodesById.ContainsKey(parentNodeId))
                {
                    throw new InvalidOperationException(
                        $"Edge '{parentEdge.Id}' references missing to node '{parentNodeId}'.");
                }

                //Cycle detection
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
        IEnumerable<string> nodeIds,
        CancellationToken cancellationToken)
    {
        return nodeIds
            .Where(id =>
            {
                cancellationToken.ThrowIfCancellationRequested();
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
