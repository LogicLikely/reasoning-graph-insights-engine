using System.Diagnostics;
using System.Text.Json.Nodes;
using Backend.Calculation;
using Backend.Calculation.MinimalCounterSets;
using Backend.Models.Domain;
using Backend.Models.Dto;
using Backend.Repositories;
using Backend.Reporting;
using Backend.Seeding;

namespace Backend.Services;

public class GraphService : IGraphService
{
    private const int MinimalCounterSetPreviewLimit = 20;
    private const int EvidenceImpactPreviewLimit = 5;
    private const int RobustnessRankingPreviewLimit = 10;

    private readonly IGraphRepository _graphRepository;
    private readonly GraphLikelihoodCalculator _calculator;
    private readonly GreedyMinimalCounterSetSolver _greedyMinimalCounterSetSolver;
    private readonly BoundedBruteForceMinimalCounterSetSolver _boundedMinimalCounterSetSolver;
    private readonly IPerformanceRunStore _performanceRunStore;
    private readonly PerformanceBuildInfo _buildInfo;

    public GraphService(
        IGraphRepository graphRepository,
        GraphLikelihoodCalculator graphLikelihoodCalculator)
        : this(
            graphRepository,
            graphLikelihoodCalculator,
            CreateMinimalCounterSetSolvers(graphLikelihoodCalculator),
            NullPerformanceRunStore.Instance)
    {
    }

    private GraphService(
        IGraphRepository graphRepository,
        GraphLikelihoodCalculator graphLikelihoodCalculator,
        (GreedyMinimalCounterSetSolver Greedy, BoundedBruteForceMinimalCounterSetSolver Bounded) solvers,
        IPerformanceRunStore performanceRunStore)
        : this(
            graphRepository,
            graphLikelihoodCalculator,
            solvers.Greedy,
            solvers.Bounded,
            performanceRunStore)
    {
    }

    public GraphService(
        IGraphRepository graphRepository,
        GraphLikelihoodCalculator graphLikelihoodCalculator,
        GreedyMinimalCounterSetSolver greedyMinimalCounterSetSolver,
        BoundedBruteForceMinimalCounterSetSolver boundedMinimalCounterSetSolver,
        IPerformanceRunStore performanceRunStore)
    {
        _graphRepository = graphRepository ?? throw new ArgumentNullException(nameof(graphRepository));
        _calculator = graphLikelihoodCalculator ?? throw new ArgumentNullException(nameof(graphLikelihoodCalculator));
        _greedyMinimalCounterSetSolver = greedyMinimalCounterSetSolver ??
            throw new ArgumentNullException(nameof(greedyMinimalCounterSetSolver));
        _boundedMinimalCounterSetSolver = boundedMinimalCounterSetSolver ??
            throw new ArgumentNullException(nameof(boundedMinimalCounterSetSolver));
        _performanceRunStore = performanceRunStore ??
            throw new ArgumentNullException(nameof(performanceRunStore));
        _buildInfo = PerformanceBuildInfoCapture.Capture(
            Environment.GetEnvironmentVariable("GIT_COMMIT") ??
            Environment.GetEnvironmentVariable("SOURCE_VERSION"),
            gitBranch: Environment.GetEnvironmentVariable("GIT_BRANCH") ??
                Environment.GetEnvironmentVariable("BRANCH_NAME"));
    }

    private static (
        GreedyMinimalCounterSetSolver Greedy,
        BoundedBruteForceMinimalCounterSetSolver Bounded)
        CreateMinimalCounterSetSolvers(GraphLikelihoodCalculator calculator)
    {
        var evaluator = new LegacyMinimalCounterSetEvaluator(calculator);
        return (
            new GreedyMinimalCounterSetSolver(evaluator),
            new BoundedBruteForceMinimalCounterSetSolver(evaluator));
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
                    ImportanceToParent = edge.ImportanceToParent
                })
                .ToList()
        };
    }

    public async Task<List<string>?> GetMinimalCounterSetAsync(
        string slug,
        string targetNodeId,
        CancellationToken cancellationToken = default)
    {
        var operationStartedAtUtc = DateTimeOffset.UtcNow;
        var operationStopwatch = Stopwatch.StartNew();
        var loadStopwatch = Stopwatch.StartNew();
        var graph = await _graphRepository.GetBySlugAsync(slug, cancellationToken);
        loadStopwatch.Stop();
        if (graph is null)
        {
            return null;
        }

        var reported = await ExecuteReportedCalculationAsync(
            graph,
            CreateMinimalCounterSetAlgorithm(PerformanceAlgorithmImplementations.Greedy),
            CreateTargetInvocation("database", targetNodeId),
            operationStartedAtUtc,
            operationStopwatch,
            loadStopwatch.Elapsed.TotalMilliseconds,
            () => _greedyMinimalCounterSetSolver.Solve(
                graph,
                targetNodeId,
                graph.Nodes.Select(node => node.Id),
                cancellationToken),
            CreateMinimalCounterSetDetails,
            result => result.CounterNodeIds.Count,
            _ => PerformanceRunStatuses.Completed,
            cancellationToken);

        return reported.Result.ThresholdReached
            ? reported.Result.CounterNodeIds.ToList()
            : null;
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

        var operationStartedAtUtc = DateTimeOffset.UtcNow;
        var operationStopwatch = Stopwatch.StartNew();
        var graph = ToDomainGraph(graphContext);
        var reported = await ExecuteReportedCalculationAsync(
            graph,
            CreateMinimalCounterSetAlgorithm(PerformanceAlgorithmImplementations.Greedy),
            CreateTargetInvocation("fixture", targetNodeId),
            operationStartedAtUtc,
            operationStopwatch,
            loadElapsedMilliseconds: null,
            () => _greedyMinimalCounterSetSolver.Solve(
                graph,
                targetNodeId,
                graph.Nodes.Select(node => node.Id),
                cancellationToken),
            CreateMinimalCounterSetDetails,
            result => result.CounterNodeIds.Count,
            _ => PerformanceRunStatuses.Completed,
            cancellationToken);

        return reported.Result.ThresholdReached
            ? reported.Result.CounterNodeIds.ToList()
            : null;
    }

    public async Task<BoundedMinimalCounterSetDto?> GetBoundedMinimalCounterSetAsync(
        string slug,
        string targetNodeId,
        CancellationToken cancellationToken = default)
    {
        var operationStartedAtUtc = DateTimeOffset.UtcNow;
        var operationStopwatch = Stopwatch.StartNew();
        var loadStopwatch = Stopwatch.StartNew();
        var graph = await _graphRepository.GetBySlugAsync(slug, cancellationToken);
        loadStopwatch.Stop();
        if (graph is null)
        {
            return null;
        }

        return await GetBoundedMinimalCounterSetAsync(
            graph,
            "database",
            targetNodeId,
            operationStartedAtUtc,
            operationStopwatch,
            loadStopwatch.Elapsed.TotalMilliseconds,
            cancellationToken);
    }

    public async Task<BoundedMinimalCounterSetDto?> GetBoundedMinimalCounterSetAsync(
        string slug,
        string targetNodeId,
        GraphDto graphContext,
        CancellationToken cancellationToken = default)
    {
        if (!string.Equals(slug, graphContext.Slug, StringComparison.Ordinal))
        {
            return null;
        }

        var operationStartedAtUtc = DateTimeOffset.UtcNow;
        var operationStopwatch = Stopwatch.StartNew();
        var graph = ToDomainGraph(graphContext);

        return await GetBoundedMinimalCounterSetAsync(
            graph,
            "fixture",
            targetNodeId,
            operationStartedAtUtc,
            operationStopwatch,
            loadElapsedMilliseconds: null,
            cancellationToken);
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
        var operationStartedAtUtc = DateTimeOffset.UtcNow;
        var operationStopwatch = Stopwatch.StartNew();
        var loadStopwatch = Stopwatch.StartNew();
        var graph = await _graphRepository.GetBySlugAsync(slug, cancellationToken);
        loadStopwatch.Stop();
        if (graph is null)
        {
            return null;
        }

        var reported = await ExecuteReportedCalculationAsync(
            graph,
            CreateCurrentAlgorithm(PerformanceAlgorithmNames.LeastRobustNode),
            CreateGraphInvocation("database"),
            operationStartedAtUtc,
            operationStopwatch,
            loadStopwatch.Elapsed.TotalMilliseconds,
            () => GetLeastRobustNode(graph, cancellationToken),
            result => CreateRobustnessDetails(graph, result),
            result => result is null ? 0 : 1,
            _ => PerformanceRunStatuses.Completed,
            cancellationToken);

        return reported.Result;
    }

    public async Task<NodeRobustnessDto?> GetLeastRobustNodeAsync(
        string slug,
        GraphDto graphContext,
        CancellationToken cancellationToken = default)
    {
        if (!string.Equals(slug, graphContext.Slug, StringComparison.Ordinal))
        {
            return null;
        }

        var operationStartedAtUtc = DateTimeOffset.UtcNow;
        var operationStopwatch = Stopwatch.StartNew();
        var graph = ToDomainGraph(graphContext);
        var reported = await ExecuteReportedCalculationAsync(
            graph,
            CreateCurrentAlgorithm(PerformanceAlgorithmNames.LeastRobustNode),
            CreateGraphInvocation("fixture"),
            operationStartedAtUtc,
            operationStopwatch,
            loadElapsedMilliseconds: null,
            () => GetLeastRobustNode(graph, cancellationToken),
            result => CreateRobustnessDetails(graph, result),
            result => result is null ? 0 : 1,
            _ => PerformanceRunStatuses.Completed,
            cancellationToken);

        return reported.Result;
    }

    public async Task<List<NodeRobustnessDto>?> GetNodeRobustnessRankingAsync(
        string slug,
        CancellationToken cancellationToken = default)
    {
        var operationStartedAtUtc = DateTimeOffset.UtcNow;
        var operationStopwatch = Stopwatch.StartNew();
        var loadStopwatch = Stopwatch.StartNew();
        var graph = await _graphRepository.GetBySlugAsync(slug, cancellationToken);
        loadStopwatch.Stop();
        if (graph is null)
        {
            return null;
        }

        var reported = await ExecuteReportedCalculationAsync(
            graph,
            CreateCurrentAlgorithm(PerformanceAlgorithmNames.RobustnessRanking),
            CreateGraphInvocation("database"),
            operationStartedAtUtc,
            operationStopwatch,
            loadStopwatch.Elapsed.TotalMilliseconds,
            () => GetNodeRobustnessRanking(graph, cancellationToken),
            result => CreateRobustnessRankingDetails(graph, result),
            result => result.Count,
            _ => PerformanceRunStatuses.Completed,
            cancellationToken);

        return reported.Result;
    }

    public async Task<List<NodeRobustnessDto>?> GetNodeRobustnessRankingAsync(
        string slug,
        GraphDto graphContext,
        CancellationToken cancellationToken = default)
    {
        if (!string.Equals(slug, graphContext.Slug, StringComparison.Ordinal))
        {
            return null;
        }

        var operationStartedAtUtc = DateTimeOffset.UtcNow;
        var operationStopwatch = Stopwatch.StartNew();
        var graph = ToDomainGraph(graphContext);
        var reported = await ExecuteReportedCalculationAsync(
            graph,
            CreateCurrentAlgorithm(PerformanceAlgorithmNames.RobustnessRanking),
            CreateGraphInvocation("fixture"),
            operationStartedAtUtc,
            operationStopwatch,
            loadElapsedMilliseconds: null,
            () => GetNodeRobustnessRanking(graph, cancellationToken),
            result => CreateRobustnessRankingDetails(graph, result),
            result => result.Count,
            _ => PerformanceRunStatuses.Completed,
            cancellationToken);

        return reported.Result;
    }

    public async Task<EvidenceImpactRankingDto?> GetEvidenceImpactRankingAsync(
        string slug,
        string targetNodeId,
        CancellationToken cancellationToken = default)
    {
        var operationStartedAtUtc = DateTimeOffset.UtcNow;
        var operationStopwatch = Stopwatch.StartNew();
        var loadStopwatch = Stopwatch.StartNew();
        var graph = await _graphRepository.GetBySlugAsync(slug, cancellationToken);
        loadStopwatch.Stop();
        if (graph is null)
        {
            return null;
        }

        var reported = await ExecuteReportedCalculationAsync(
            graph,
            CreateCurrentAlgorithm(PerformanceAlgorithmNames.EvidenceImpactRanking),
            CreateTargetInvocation("database", targetNodeId),
            operationStartedAtUtc,
            operationStopwatch,
            loadStopwatch.Elapsed.TotalMilliseconds,
            () => _calculator.GetEvidenceImpactRanking(
                graph,
                targetNodeId,
                cancellationToken),
            result => CreateEvidenceImpactDetails(graph, targetNodeId, result),
            result => result.SupportingEvidence.Count + result.CounterEvidence.Count,
            _ => PerformanceRunStatuses.Completed,
            cancellationToken);

        return reported.Result;
    }

    public async Task<EvidenceImpactRankingDto?> GetEvidenceImpactRankingAsync(
        string slug,
        string targetNodeId,
        GraphDto graphContext,
        CancellationToken cancellationToken = default)
    {
        if (!string.Equals(slug, graphContext.Slug, StringComparison.Ordinal))
        {
            return null;
        }

        var operationStartedAtUtc = DateTimeOffset.UtcNow;
        var operationStopwatch = Stopwatch.StartNew();
        var graph = ToDomainGraph(graphContext);
        var reported = await ExecuteReportedCalculationAsync(
            graph,
            CreateCurrentAlgorithm(PerformanceAlgorithmNames.EvidenceImpactRanking),
            CreateTargetInvocation("fixture", targetNodeId),
            operationStartedAtUtc,
            operationStopwatch,
            loadElapsedMilliseconds: null,
            () => _calculator.GetEvidenceImpactRanking(
                graph,
                targetNodeId,
                cancellationToken),
            result => CreateEvidenceImpactDetails(graph, targetNodeId, result),
            result => result.SupportingEvidence.Count + result.CounterEvidence.Count,
            _ => PerformanceRunStatuses.Completed,
            cancellationToken);

        return reported.Result;
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
        if (!node.PriorOdds.HasValue)
        {
            return await _graphRepository.UpdateNodeAsync(
                slug,
                nodeId,
                node,
                cancellationToken);
        }

        var operationStartedAtUtc = DateTimeOffset.UtcNow;
        var operationStopwatch = Stopwatch.StartNew();
        var loadStopwatch = Stopwatch.StartNew();
        var graphBeforeUpdate = await _graphRepository.GetBySlugAsync(slug, cancellationToken);
        loadStopwatch.Stop();
        var totalLoadElapsedMilliseconds =
            loadStopwatch.Elapsed.TotalMilliseconds;
        var nodeBeforeUpdate = graphBeforeUpdate?.Nodes.FirstOrDefault(candidate =>
            string.Equals(candidate.Id, nodeId, StringComparison.Ordinal));
        var updateInvocation = CreateNodeUpdateInvocation(
            nodeBeforeUpdate,
            nodeId,
            node);

        var updated = await _graphRepository.UpdateNodeAsync(slug, nodeId, node, cancellationToken);
        if (!updated)
        {
            return false;
        }

        if (graphBeforeUpdate is not null)
        {
            ApplyNodeUpdate(graphBeforeUpdate, nodeId, node);
        }

        Graph? reportingGraph = graphBeforeUpdate;
        IReadOnlyDictionary<string, decimal> recalculatedLogOdds =
            new Dictionary<string, decimal>();
        PerformanceMeasurementResult computeMeasurement;
        double? persistElapsedMilliseconds = null;

        if (node.PriorOdds.HasValue)
        {
            var graphForCalculation = graphBeforeUpdate;
            if (graphForCalculation is null)
            {
                var fallbackLoadStopwatch = Stopwatch.StartNew();
                graphForCalculation = await _graphRepository.GetBySlugAsync(
                    slug,
                    cancellationToken);
                fallbackLoadStopwatch.Stop();
                totalLoadElapsedMilliseconds +=
                    fallbackLoadStopwatch.Elapsed.TotalMilliseconds;
                if (graphForCalculation is null)
                {
                    return true;
                }

                reportingGraph = graphForCalculation;
                ApplyNodeUpdate(graphForCalculation, nodeId, node);
            }

            reportingGraph = graphForCalculation;
            var compute = PerformanceMeasurement.Start();
            try
            {
                var context = GraphCalculationContext.From(
                    graphForCalculation.Nodes,
                    graphForCalculation.Edges);
                recalculatedLogOdds = _calculator.RecalculateAncestors(context, nodeId);
            }
            catch (Exception exception)
            {
                computeMeasurement = compute.Stop();
                await TryReportFailedLeafUpdateAsync(
                    reportingGraph,
                    updateInvocation,
                    graphForCalculation.Nodes.FirstOrDefault(candidate =>
                        string.Equals(candidate.Id, nodeId, StringComparison.Ordinal))?.Kind,
                    nodeId,
                    operationStartedAtUtc,
                    operationStopwatch,
                    totalLoadElapsedMilliseconds,
                    computeMeasurement,
                    persistElapsedMilliseconds: null,
                    recalculatedLogOdds,
                    exception);
                throw;
            }

            computeMeasurement = compute.Stop();

            var persistStopwatch = Stopwatch.StartNew();
            try
            {
                if (recalculatedLogOdds.Count > 0)
                {
                    await _graphRepository.UpdateNodePosteriorOddsBatchAsync(
                        graphForCalculation.Id,
                        recalculatedLogOdds,
                        cancellationToken);
                }
            }
            catch (Exception exception)
            {
                persistStopwatch.Stop();
                await TryReportFailedLeafUpdateAsync(
                    reportingGraph,
                    updateInvocation,
                    graphForCalculation.Nodes.FirstOrDefault(candidate =>
                        string.Equals(candidate.Id, nodeId, StringComparison.Ordinal))?.Kind,
                    nodeId,
                    operationStartedAtUtc,
                    operationStopwatch,
                    totalLoadElapsedMilliseconds,
                    computeMeasurement,
                    persistStopwatch.Elapsed.TotalMilliseconds,
                    recalculatedLogOdds,
                    exception);
                throw;
            }

            persistStopwatch.Stop();
            persistElapsedMilliseconds = persistStopwatch.Elapsed.TotalMilliseconds;
            await ReportLeafUpdateAsync(
                reportingGraph,
                updateInvocation,
                graphForCalculation.Nodes.FirstOrDefault(candidate =>
                    string.Equals(candidate.Id, nodeId, StringComparison.Ordinal))?.Kind,
                nodeId,
                operationStartedAtUtc,
                operationStopwatch,
                totalLoadElapsedMilliseconds,
                computeMeasurement,
                persistElapsedMilliseconds,
                recalculatedLogOdds,
                persistedRowCount: recalculatedLogOdds.Count,
                triggered: true);

            return true;
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
            await _graphRepository.UpdateNodePosteriorOddsBatchAsync(graph.Id, recalculatedLogOdds, cancellationToken);
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
            await _graphRepository.UpdateNodePosteriorOddsBatchAsync(graph.Id, recalculatedLogOdds, cancellationToken);
        }

        return recalculatedLogOdds;
    }

    private async Task<BoundedMinimalCounterSetDto> GetBoundedMinimalCounterSetAsync(
        Graph graph,
        string dataSource,
        string targetNodeId,
        DateTimeOffset operationStartedAtUtc,
        Stopwatch operationStopwatch,
        double? loadElapsedMilliseconds,
        CancellationToken cancellationToken)
    {
        var invocation = CreateTargetInvocation(dataSource, targetNodeId);
        invocation.Parameters["candidateLimit"] =
            BoundedBruteForceMinimalCounterSetSolver.CandidateLimit;

        var reported = await ExecuteReportedCalculationAsync(
            graph,
            CreateMinimalCounterSetAlgorithm(
                PerformanceAlgorithmImplementations.BoundedBruteForce),
            invocation,
            operationStartedAtUtc,
            operationStopwatch,
            loadElapsedMilliseconds,
            () => _boundedMinimalCounterSetSolver.Solve(
                graph,
                targetNodeId,
                graph.Nodes.Select(node => node.Id),
                cancellationToken),
            CreateMinimalCounterSetDetails,
            result => result.CounterNodeIds.Count,
            result => result.ProofStatus == MinimalCounterSetProofStatus.NotProven
                ? PerformanceRunStatuses.NotProven
                : PerformanceRunStatuses.Completed,
            cancellationToken);

        return new BoundedMinimalCounterSetDto
        {
            CounterNodeIds = reported.Result.ThresholdReached
                ? reported.Result.CounterNodeIds.ToList()
                : null,
            ProofStatus = ToProofStatusValue(reported.Result.ProofStatus),
            RunNumber = reported.StoredRun.RunNumber
        };
    }

    private async Task<ReportedCalculation<T>> ExecuteReportedCalculationAsync<T>(
        Graph graph,
        PerformanceAlgorithmInfo algorithm,
        PerformanceInvocationInfo invocation,
        DateTimeOffset operationStartedAtUtc,
        Stopwatch operationStopwatch,
        double? loadElapsedMilliseconds,
        Func<T> calculate,
        Func<T, JsonObject> createDetails,
        Func<T, int?> getResultCount,
        Func<T, string> getStatus,
        CancellationToken cancellationToken)
    {
        _ = cancellationToken;
        var measurement = PerformanceMeasurement.Start();
        try
        {
            var result = calculate();
            var measured = measurement.Stop();
            operationStopwatch.Stop();

            var run = new PerformanceRunRecord
            {
                StartedAtUtc = operationStartedAtUtc,
                Algorithm = algorithm,
                Build = _buildInfo,
                Graph = PerformanceRunMetadataCapture.CaptureGraph(graph),
                Invocation = invocation,
                Timing = new PerformanceTimingInfo
                {
                    LoadElapsedMilliseconds = loadElapsedMilliseconds,
                    ComputeElapsedMilliseconds = measured.ElapsedMilliseconds,
                    OperationElapsedMilliseconds =
                        operationStopwatch.Elapsed.TotalMilliseconds
                },
                Resources = measured.Resources,
                Outcome = new PerformanceOutcomeInfo
                {
                    Status = getStatus(result),
                    ResultCount = getResultCount(result),
                    ResultDigest =
                        PerformanceRunMetadataCapture.CalculateResultDigest(result)
                },
                Details = createDetails(result)
            };
            var storedRun = await _performanceRunStore.AppendAsync(
                run,
                CancellationToken.None);

            return new ReportedCalculation<T>(result, storedRun);
        }
        catch (Exception exception)
        {
            var measured = measurement.Stop();
            operationStopwatch.Stop();
            var status = exception is OperationCanceledException
                ? PerformanceRunStatuses.Cancelled
                : PerformanceRunStatuses.Failed;
            var failedRun = new PerformanceRunRecord
            {
                StartedAtUtc = operationStartedAtUtc,
                Algorithm = algorithm,
                Build = _buildInfo,
                Graph = PerformanceRunMetadataCapture.CaptureGraph(graph),
                Invocation = invocation,
                Timing = new PerformanceTimingInfo
                {
                    LoadElapsedMilliseconds = loadElapsedMilliseconds,
                    ComputeElapsedMilliseconds = measured.ElapsedMilliseconds,
                    OperationElapsedMilliseconds =
                        operationStopwatch.Elapsed.TotalMilliseconds
                },
                Resources = measured.Resources,
                Outcome = new PerformanceOutcomeInfo
                {
                    Status = status,
                    ErrorType = exception.GetType().FullName,
                    ErrorMessage = exception.Message
                }
            };

            try
            {
                await _performanceRunStore.AppendAsync(
                    failedRun,
                    CancellationToken.None);
            }
            catch
            {
                // Reporting must not replace the calculation's original failure.
            }

            throw;
        }
    }

    private async Task ReportLeafUpdateAsync(
        Graph graph,
        PerformanceInvocationInfo invocation,
        string? changedNodeKind,
        string changedNodeId,
        DateTimeOffset operationStartedAtUtc,
        Stopwatch operationStopwatch,
        double loadElapsedMilliseconds,
        PerformanceMeasurementResult computeMeasurement,
        double? persistElapsedMilliseconds,
        IReadOnlyDictionary<string, decimal> recalculatedLogOdds,
        int persistedRowCount,
        bool triggered,
        Exception? exception = null)
    {
        operationStopwatch.Stop();
        var isLeaf = !graph.Edges.Any(edge =>
            string.Equals(edge.To, changedNodeId, StringComparison.Ordinal));
        var details = new JsonObject
        {
            ["triggered"] = triggered,
            ["recalculationScope"] = "ancestors-only",
            ["affectedNodeCount"] = recalculatedLogOdds.Count,
            ["maximumAncestorDistance"] =
                TryGetMaximumAncestorDistance(graph, changedNodeId),
            ["persistedRowCount"] = persistedRowCount,
            ["changedNodeKind"] = changedNodeKind,
            ["isLeaf"] = isLeaf
        };
        var run = new PerformanceRunRecord
        {
            StartedAtUtc = operationStartedAtUtc,
            Algorithm = CreateLeafUpdateAlgorithm(),
            Build = _buildInfo,
            Graph = PerformanceRunMetadataCapture.CaptureGraph(graph),
            Invocation = invocation,
            Timing = new PerformanceTimingInfo
            {
                LoadElapsedMilliseconds = loadElapsedMilliseconds,
                ComputeElapsedMilliseconds = computeMeasurement.ElapsedMilliseconds,
                PersistElapsedMilliseconds = persistElapsedMilliseconds,
                OperationElapsedMilliseconds =
                    operationStopwatch.Elapsed.TotalMilliseconds
            },
            Resources = computeMeasurement.Resources,
            Outcome = new PerformanceOutcomeInfo
            {
                Status = exception switch
                {
                    OperationCanceledException => PerformanceRunStatuses.Cancelled,
                    not null => PerformanceRunStatuses.Failed,
                    _ => PerformanceRunStatuses.Completed
                },
                ResultCount = exception is null ? recalculatedLogOdds.Count : null,
                ResultDigest = exception is null
                    ? PerformanceRunMetadataCapture.CalculateResultDigest(
                        recalculatedLogOdds)
                    : null,
                ErrorType = exception?.GetType().FullName,
                ErrorMessage = exception?.Message
            },
            Details = details
        };

        await _performanceRunStore.AppendAsync(run, CancellationToken.None);
    }

    private static int? TryGetMaximumAncestorDistance(
        Graph graph,
        string changedNodeId)
    {
        try
        {
            return PerformanceRunMetadataCapture.GetMaximumAncestorDistance(
                graph,
                changedNodeId);
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    private async Task TryReportFailedLeafUpdateAsync(
        Graph graph,
        PerformanceInvocationInfo invocation,
        string? changedNodeKind,
        string changedNodeId,
        DateTimeOffset operationStartedAtUtc,
        Stopwatch operationStopwatch,
        double loadElapsedMilliseconds,
        PerformanceMeasurementResult computeMeasurement,
        double? persistElapsedMilliseconds,
        IReadOnlyDictionary<string, decimal> recalculatedLogOdds,
        Exception exception)
    {
        try
        {
            await ReportLeafUpdateAsync(
                graph,
                invocation,
                changedNodeKind,
                changedNodeId,
                operationStartedAtUtc,
                operationStopwatch,
                loadElapsedMilliseconds,
                computeMeasurement,
                persistElapsedMilliseconds,
                recalculatedLogOdds,
                persistedRowCount: 0,
                triggered: true,
                exception: exception);
        }
        catch
        {
            // Reporting must not replace the update's original failure.
        }
    }

    private static void ApplyNodeUpdate(
        Graph graph,
        string nodeId,
        GraphNodeUpdateDto update)
    {
        var graphNode = graph.Nodes.FirstOrDefault(candidate =>
            string.Equals(candidate.Id, nodeId, StringComparison.Ordinal));
        if (graphNode is null)
        {
            return;
        }

        graphNode.Title = update.Title ?? graphNode.Title;
        graphNode.BodyText = update.BodyText ?? graphNode.BodyText;
        graphNode.PriorOdds = update.PriorOdds ?? graphNode.PriorOdds;
        graphNode.PosteriorOdds = update.PosteriorOdds ?? graphNode.PosteriorOdds;
    }

    private static PerformanceAlgorithmInfo CreateMinimalCounterSetAlgorithm(
        string implementation)
    {
        return new PerformanceAlgorithmInfo
        {
            Name = PerformanceAlgorithmNames.MinimalCounterSet,
            Implementation = implementation,
            CalculationModel = "graph-likelihood-calculator"
        };
    }

    private static PerformanceAlgorithmInfo CreateCurrentAlgorithm(string name)
    {
        return new PerformanceAlgorithmInfo
        {
            Name = name,
            Implementation = PerformanceAlgorithmImplementations.Current,
            CalculationModel = "graph-likelihood-calculator"
        };
    }

    private static PerformanceAlgorithmInfo CreateLeafUpdateAlgorithm()
    {
        return new PerformanceAlgorithmInfo
        {
            Name = PerformanceAlgorithmNames.LeafUpdate,
            Implementation = PerformanceAlgorithmImplementations.Current,
            CalculationModel = "graph-likelihood-calculator"
        };
    }

    private static PerformanceInvocationInfo CreateGraphInvocation(string dataSource)
    {
        return new PerformanceInvocationInfo
        {
            DataSource = dataSource
        };
    }

    private static PerformanceInvocationInfo CreateTargetInvocation(
        string dataSource,
        string targetNodeId)
    {
        return new PerformanceInvocationInfo
        {
            DataSource = dataSource,
            TargetNodeId = targetNodeId,
            Parameters = new JsonObject
            {
                ["thresholdLogOdds"] =
                    LegacyMinimalCounterSetEvaluator.DefaultThresholdLogOdds
            }
        };
    }

    private static PerformanceInvocationInfo CreateNodeUpdateInvocation(
        GraphNode? existingNode,
        string changedNodeId,
        GraphNodeUpdateDto update)
    {
        var changes = GetNodeUpdateChanges(existingNode, update);
        var oldValues = new JsonObject();
        var newValues = new JsonObject();
        foreach (var change in changes)
        {
            oldValues[change.Field] =
                PerformanceRunMetadataCapture.ToJsonNode(change.OldValue);
            newValues[change.Field] =
                PerformanceRunMetadataCapture.ToJsonNode(change.NewValue);
        }

        return new PerformanceInvocationInfo
        {
            DataSource = "database",
            ChangedNodeId = changedNodeId,
            ChangedField = changes.Count switch
            {
                0 => "none",
                1 => changes[0].Field,
                _ => "multiple"
            },
            OldValue = changes.Count == 1
                ? PerformanceRunMetadataCapture.ToJsonNode(changes[0].OldValue)
                : oldValues,
            NewValue = changes.Count == 1
                ? PerformanceRunMetadataCapture.ToJsonNode(changes[0].NewValue)
                : newValues,
            Parameters = new JsonObject
            {
                ["changedFields"] = new JsonArray(
                    changes
                        .Select(change => JsonValue.Create(change.Field))
                        .ToArray())
            }
        };
    }

    private static List<NodeUpdateChange> GetNodeUpdateChanges(
        GraphNode? existingNode,
        GraphNodeUpdateDto update)
    {
        var changes = new List<NodeUpdateChange>();
        if (update.Kind is not null)
        {
            changes.Add(new NodeUpdateChange("kind", existingNode?.Kind, update.Kind));
        }

        if (update.Title is not null)
        {
            changes.Add(new NodeUpdateChange("title", existingNode?.Title, update.Title));
        }

        if (update.BodyText is not null)
        {
            changes.Add(new NodeUpdateChange(
                "bodyText",
                existingNode?.BodyText,
                update.BodyText));
        }

        if (update.PriorOdds.HasValue)
        {
            changes.Add(new NodeUpdateChange(
                "priorOdds",
                existingNode?.PriorOdds,
                update.PriorOdds.Value));
        }

        if (update.PosteriorOdds.HasValue)
        {
            changes.Add(new NodeUpdateChange(
                "posteriorOdds",
                existingNode?.PosteriorOdds,
                update.PosteriorOdds.Value));
        }

        return changes;
    }

    private static JsonObject CreateMinimalCounterSetDetails(
        MinimalCounterSetResult result)
    {
        return new JsonObject
        {
            ["totalCandidateCount"] = result.TotalCandidateCount,
            ["searchedCandidateCount"] = result.SearchedCandidateCount,
            ["excludedCandidateCount"] = result.ExcludedCandidateCount,
            ["candidatesExamined"] = result.CandidatesExamined,
            ["subsetEvaluations"] = result.SubsetEvaluations,
            ["largestCardinalityFullyExhausted"] =
                result.LargestCardinalityFullyExhausted,
            ["returnedSetSize"] = result.CounterNodeIds.Count,
            ["thresholdLogOdds"] = result.ThresholdLogOdds,
            ["initialTargetLogOdds"] = result.InitialTargetLogOdds,
            ["finalTargetLogOdds"] = result.FinalTargetLogOdds,
            ["thresholdReached"] = result.ThresholdReached,
            ["proofStatus"] = ToProofStatusValue(result.ProofStatus),
            ["stopReason"] = ToStopReasonValue(result.StopReason),
            ["returnedNodeIds"] = new JsonArray(
                result.CounterNodeIds
                    .Take(MinimalCounterSetPreviewLimit)
                    .Select(nodeId => JsonValue.Create(nodeId))
                    .ToArray()),
            ["returnedNodeIdsTruncated"] =
                result.CounterNodeIds.Count > MinimalCounterSetPreviewLimit
        };
    }

    private static JsonObject CreateEvidenceImpactDetails(
        Graph graph,
        string targetNodeId,
        EvidenceImpactRankingDto result)
    {
        var reachableEvidenceCount = CountReachableEvidenceNodes(graph, targetNodeId);
        var returnedEvidenceCount =
            result.SupportingEvidence.Count + result.CounterEvidence.Count;
        return new JsonObject
        {
            ["reachableNodeCount"] =
                PerformanceRunMetadataCapture.CountReachableNodes(graph, targetNodeId),
            ["reachableEvidenceCount"] = reachableEvidenceCount,
            ["supportingResultCount"] = result.SupportingEvidence.Count,
            ["counterResultCount"] = result.CounterEvidence.Count,
            ["neutralEvidenceCount"] =
                Math.Max(0, reachableEvidenceCount - returnedEvidenceCount),
            ["supportingPreview"] = CreateEvidenceImpactPreview(
                result.SupportingEvidence),
            ["counterPreview"] = CreateEvidenceImpactPreview(
                result.CounterEvidence)
        };
    }

    private static JsonObject CreateRobustnessDetails(
        Graph graph,
        NodeRobustnessDto? result)
    {
        return new JsonObject
        {
            ["nodesEvaluated"] = graph.Nodes.Count,
            ["edgesExamined"] = graph.Edges.Count,
            ["leafCount"] = PerformanceRunMetadataCapture.CountLeafNodes(graph),
            ["robustnessResultCount"] = graph.Nodes.Count,
            ["selectedNodeId"] = result?.NodeId,
            ["selectedNodeTitle"] = result?.NodeTitle,
            ["selectedRobustness"] = result?.Robustness
        };
    }

    private static JsonObject CreateRobustnessRankingDetails(
        Graph graph,
        IReadOnlyCollection<NodeRobustnessDto> result)
    {
        return new JsonObject
        {
            ["nodesEvaluated"] = graph.Nodes.Count,
            ["edgesExamined"] = graph.Edges.Count,
            ["leafCount"] = PerformanceRunMetadataCapture.CountLeafNodes(graph),
            ["robustnessResultCount"] = result.Count,
            ["rankedItemCount"] = result.Count,
            ["rankingPreview"] = new JsonArray(
                result
                    .Take(RobustnessRankingPreviewLimit)
                    .Select(item => new JsonObject
                    {
                        ["nodeId"] = item.NodeId,
                        ["nodeTitle"] = item.NodeTitle,
                        ["robustness"] = item.Robustness
                    })
                    .ToArray())
        };
    }

    private static JsonArray CreateEvidenceImpactPreview(
        IEnumerable<EvidenceImpactDto> result)
    {
        return new JsonArray(
            result
                .Take(EvidenceImpactPreviewLimit)
                .Select(item => new JsonObject
                {
                    ["nodeId"] = item.NodeId,
                    ["logLr"] = item.LogLr,
                    ["probabilityDifference"] = item.ProbabilityDifference
                })
                .ToArray());
    }

    private static int CountReachableEvidenceNodes(Graph graph, string targetNodeId)
    {
        var nodesById = graph.Nodes.ToDictionary(node => node.Id, StringComparer.Ordinal);
        var childrenByParent = graph.Edges
            .GroupBy(edge => edge.To, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.Select(edge => edge.From).ToArray(),
                StringComparer.Ordinal);
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var pending = new Stack<string>();
        pending.Push(targetNodeId);
        var evidenceCount = 0;

        while (pending.Count > 0)
        {
            var nodeId = pending.Pop();
            if (!visited.Add(nodeId))
            {
                continue;
            }

            if (nodesById.TryGetValue(nodeId, out var node) &&
                (string.Equals(node.Kind, "evidence", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(node.Kind, "objection", StringComparison.OrdinalIgnoreCase)))
            {
                evidenceCount++;
            }

            if (!childrenByParent.TryGetValue(nodeId, out var childNodeIds))
            {
                continue;
            }

            foreach (var childNodeId in childNodeIds)
            {
                pending.Push(childNodeId);
            }
        }

        return evidenceCount;
    }

    private static string ToProofStatusValue(MinimalCounterSetProofStatus proofStatus)
    {
        return proofStatus switch
        {
            MinimalCounterSetProofStatus.Proven => "proven",
            MinimalCounterSetProofStatus.NotProven => "notProven",
            _ => "notApplicable"
        };
    }

    private static string ToStopReasonValue(MinimalCounterSetStopReason stopReason)
    {
        return stopReason switch
        {
            MinimalCounterSetStopReason.CandidateLimit => "candidateLimit",
            _ => "completed"
        };
    }

    private sealed record ReportedCalculation<T>(
        T Result,
        PerformanceRunRecord StoredRun);

    private sealed record NodeUpdateChange(
        string Field,
        object? OldValue,
        object? NewValue);
}
