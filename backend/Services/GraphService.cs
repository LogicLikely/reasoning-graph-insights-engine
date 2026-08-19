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

    // Minimal-counter search implementations share the BF evaluator.
    private readonly GreedyMinimalCounterSetSolver _greedyMinimalCounterSetSolver;
    private readonly BoundedBruteForceMinimalCounterSetSolver _boundedMinimalCounterSetSolver;
    private readonly IPerformanceRunStore _performanceRunStore;
    private readonly PerformanceBuildInfo _buildInfo;

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
        : this(
            graphRepository,
            graphLikelihoodCalculator,
            posteriorOddsCalculator,
            CreateMinimalCounterSetSolvers(posteriorOddsCalculator),
            NullPerformanceRunStore.Instance)
    {
    }

    private GraphService(
        IGraphRepository graphRepository,
        GraphLikelihoodCalculator graphLikelihoodCalculator,
        GraphPosteriorOddsCalculator posteriorOddsCalculator,
        (GreedyMinimalCounterSetSolver Greedy, BoundedBruteForceMinimalCounterSetSolver Bounded) solvers,
        IPerformanceRunStore performanceRunStore)
        : this(
            graphRepository,
            graphLikelihoodCalculator,
            posteriorOddsCalculator,
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
        : this(
            graphRepository,
            graphLikelihoodCalculator,
            new GraphPosteriorOddsCalculator(),
            greedyMinimalCounterSetSolver,
            boundedMinimalCounterSetSolver,
            performanceRunStore)
    {
    }

    public GraphService(
        IGraphRepository graphRepository,
        GraphLikelihoodCalculator graphLikelihoodCalculator,
        GraphPosteriorOddsCalculator posteriorOddsCalculator,
        GreedyMinimalCounterSetSolver greedyMinimalCounterSetSolver,
        BoundedBruteForceMinimalCounterSetSolver boundedMinimalCounterSetSolver,
        IPerformanceRunStore performanceRunStore)
    {
        _graphRepository = graphRepository ?? throw new ArgumentNullException(nameof(graphRepository));
        ArgumentNullException.ThrowIfNull(graphLikelihoodCalculator);
        _posteriorOddsCalculator = posteriorOddsCalculator ??
            throw new ArgumentNullException(nameof(posteriorOddsCalculator));
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
        CreateMinimalCounterSetSolvers(GraphPosteriorOddsCalculator calculator)
    {
        var evaluator = new BayesianMinimalCounterSetEvaluator(calculator);
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
                    ProbabilityGivenParent = edge.ProbabilityGivenParent,
                    ProbabilityGivenNotParent = edge.ProbabilityGivenNotParent
                })
                .ToList()
        };
    }

    public Task<List<string>?> GetMinimalCounterSetAsync(
        string slug,
        string targetNodeId,
        CancellationToken cancellationToken = default)
    {
        return GetMinimalCounterSetAsync(
            slug,
            targetNodeId,
            benchmarkSetId: null,
            cancellationToken);
    }

    public async Task<List<string>?> GetMinimalCounterSetAsync(
        string slug,
        string targetNodeId,
        string? benchmarkSetId,
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
            benchmarkSetId,
            cancellationToken);

        return reported.Result.ThresholdReached
            ? reported.Result.CounterNodeIds.ToList()
            : null;
    }

    public Task<List<string>?> GetMinimalCounterSetAsync(
        string slug,
        string targetNodeId,
        GraphDto graphContext,
        CancellationToken cancellationToken = default)
    {
        return GetMinimalCounterSetAsync(
            slug,
            targetNodeId,
            graphContext,
            benchmarkSetId: null,
            cancellationToken);
    }

    public async Task<List<string>?> GetMinimalCounterSetAsync(
        string slug,
        string targetNodeId,
        GraphDto graphContext,
        string? benchmarkSetId,
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
            benchmarkSetId,
            cancellationToken);

        return reported.Result.ThresholdReached
            ? reported.Result.CounterNodeIds.ToList()
            : null;
    }

    public Task<BoundedMinimalCounterSetDto?> GetBoundedMinimalCounterSetAsync(
        string slug,
        string targetNodeId,
        CancellationToken cancellationToken = default)
    {
        return GetBoundedMinimalCounterSetAsync(
            slug,
            targetNodeId,
            benchmarkSetId: null,
            cancellationToken);
    }

    public async Task<BoundedMinimalCounterSetDto?> GetBoundedMinimalCounterSetAsync(
        string slug,
        string targetNodeId,
        string? benchmarkSetId,
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
            benchmarkSetId,
            cancellationToken);
    }

    public Task<BoundedMinimalCounterSetDto?> GetBoundedMinimalCounterSetAsync(
        string slug,
        string targetNodeId,
        GraphDto graphContext,
        CancellationToken cancellationToken = default)
    {
        return GetBoundedMinimalCounterSetAsync(
            slug,
            targetNodeId,
            graphContext,
            benchmarkSetId: null,
            cancellationToken);
    }

    public async Task<BoundedMinimalCounterSetDto?> GetBoundedMinimalCounterSetAsync(
        string slug,
        string targetNodeId,
        GraphDto graphContext,
        string? benchmarkSetId,
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
            benchmarkSetId,
            cancellationToken);
    }

    public NodeRobustnessDto? GetLeastRobustNode(
        Graph graph,
        CancellationToken cancellationToken = default
    )
    {
        var robustnessValues = GetAllNodeRobustness(graph, cancellationToken);
        if (robustnessValues.Count == 0)
        {
            return null;
        }

        var leastRobust = robustnessValues
            .OrderBy(entry => entry.Value)
            .ThenBy(entry => entry.Key, StringComparer.Ordinal)
            .First();
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

        return GetAllNodeRobustness(graph, cancellationToken)
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

    // Robustness is exp(-d), where d is the largest absolute change in the
    // node's BF-derived probability after removing one downstream evidence or
    // objection node. A leaf or evidence-independent node therefore scores 1.
    private Dictionary<string, decimal> GetAllNodeRobustness(
        Graph graph,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(graph);
        cancellationToken.ThrowIfCancellationRequested();

        var context = GraphCalculationContext.From(graph.Nodes, graph.Edges);
        var includedNodeIds = context.NodesById.Keys.ToHashSet(StringComparer.Ordinal);
        var robustnessByNodeId = new Dictionary<string, decimal>(
            context.NodesById.Count,
            StringComparer.Ordinal);

        foreach (string targetNodeId in context.NodesById.Keys)
        {
            cancellationToken.ThrowIfCancellationRequested();

            decimal targetLogOdds = CalculateTargetLogOdds(
                graph,
                targetNodeId,
                includedNodeIds,
                cancellationToken);
            double targetProbability = LogOddsToProbability(targetLogOdds);
            double largestProbabilityChange = 0d;

            foreach (string evidenceNodeId in CollectEvidenceReachingTarget(
                         context,
                         targetNodeId,
                         cancellationToken))
            {
                cancellationToken.ThrowIfCancellationRequested();
                includedNodeIds.Remove(evidenceNodeId);
                decimal logOddsWithoutEvidence = CalculateTargetLogOdds(
                    graph,
                    targetNodeId,
                    includedNodeIds,
                    cancellationToken);
                includedNodeIds.Add(evidenceNodeId);

                double probabilityWithoutEvidence =
                    LogOddsToProbability(logOddsWithoutEvidence);
                largestProbabilityChange = Math.Max(
                    largestProbabilityChange,
                    Math.Abs(targetProbability - probabilityWithoutEvidence));
            }

            robustnessByNodeId[targetNodeId] =
                (decimal)Math.Exp(-largestProbabilityChange);
        }

        return robustnessByNodeId;
    }

    private static List<string> CollectEvidenceReachingTarget(
        GraphCalculationContext context,
        string targetNodeId,
        CancellationToken cancellationToken)
    {
        var evidenceNodeIds = new List<string>();
        var visitedNodeIds = new HashSet<string>(StringComparer.Ordinal)
        {
            targetNodeId
        };
        var nodesToVisit = new Queue<string>();
        nodesToVisit.Enqueue(targetNodeId);

        while (nodesToVisit.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string parentNodeId = nodesToVisit.Dequeue();
            if (!context.ChildEdgesByParentId.TryGetValue(
                    parentNodeId,
                    out var childEdges))
            {
                continue;
            }

            foreach (var childEdge in childEdges)
            {
                string childNodeId = childEdge.FromNodeId;
                if (!visitedNodeIds.Add(childNodeId))
                {
                    continue;
                }

                if (IsEvidenceLikeNodeKind(context.NodesById[childNodeId].Kind))
                {
                    evidenceNodeIds.Add(childNodeId);
                }

                nodesToVisit.Enqueue(childNodeId);
            }
        }

        return evidenceNodeIds;
    }

    private static double LogOddsToProbability(decimal logOdds)
    {
        double value = (double)logOdds;
        if (value >= 0d)
        {
            double inverseOdds = Math.Exp(-value);
            return 1d / (1d + inverseOdds);
        }

        double odds = Math.Exp(value);
        return odds / (1d + odds);
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

    public Task<NodeRobustnessDto?> GetLeastRobustNodeAsync(
        string slug,
        CancellationToken cancellationToken = default)
    {
        return GetLeastRobustNodeAsync(
            slug,
            benchmarkSetId: null,
            cancellationToken);
    }

    public async Task<NodeRobustnessDto?> GetLeastRobustNodeAsync(
        string slug,
        string? benchmarkSetId,
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
            benchmarkSetId,
            cancellationToken);

        return reported.Result;
    }

    public Task<NodeRobustnessDto?> GetLeastRobustNodeAsync(
        string slug,
        GraphDto graphContext,
        CancellationToken cancellationToken = default)
    {
        return GetLeastRobustNodeAsync(
            slug,
            graphContext,
            benchmarkSetId: null,
            cancellationToken);
    }

    public async Task<NodeRobustnessDto?> GetLeastRobustNodeAsync(
        string slug,
        GraphDto graphContext,
        string? benchmarkSetId,
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
            benchmarkSetId,
            cancellationToken);

        return reported.Result;
    }

    public Task<List<NodeRobustnessDto>?> GetNodeRobustnessRankingAsync(
        string slug,
        CancellationToken cancellationToken = default)
    {
        return GetNodeRobustnessRankingAsync(
            slug,
            benchmarkSetId: null,
            cancellationToken);
    }

    public async Task<List<NodeRobustnessDto>?> GetNodeRobustnessRankingAsync(
        string slug,
        string? benchmarkSetId,
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
            benchmarkSetId,
            cancellationToken);

        return reported.Result;
    }

    public Task<List<NodeRobustnessDto>?> GetNodeRobustnessRankingAsync(
        string slug,
        GraphDto graphContext,
        CancellationToken cancellationToken = default)
    {
        return GetNodeRobustnessRankingAsync(
            slug,
            graphContext,
            benchmarkSetId: null,
            cancellationToken);
    }

    public async Task<List<NodeRobustnessDto>?> GetNodeRobustnessRankingAsync(
        string slug,
        GraphDto graphContext,
        string? benchmarkSetId,
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
            benchmarkSetId,
            cancellationToken);

        return reported.Result;
    }

    public Task<EvidenceImpactRankingDto?> GetEvidenceImpactRankingAsync(
        string slug,
        string targetNodeId,
        CancellationToken cancellationToken = default)
    {
        return GetEvidenceImpactRankingAsync(
            slug,
            targetNodeId,
            benchmarkSetId: null,
            cancellationToken);
    }

    public async Task<EvidenceImpactRankingDto?> GetEvidenceImpactRankingAsync(
        string slug,
        string targetNodeId,
        string? benchmarkSetId,
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
            () => _posteriorOddsCalculator.GetEvidenceImpactRanking(
                graph,
                targetNodeId,
                cancellationToken),
            result => CreateEvidenceImpactDetails(graph, targetNodeId, result),
            result => result.SupportingEvidence.Count + result.CounterEvidence.Count,
            _ => PerformanceRunStatuses.Completed,
            benchmarkSetId,
            cancellationToken);

        return reported.Result;
    }

    public Task<EvidenceImpactRankingDto?> GetEvidenceImpactRankingAsync(
        string slug,
        string targetNodeId,
        GraphDto graphContext,
        CancellationToken cancellationToken = default)
    {
        return GetEvidenceImpactRankingAsync(
            slug,
            targetNodeId,
            graphContext,
            benchmarkSetId: null,
            cancellationToken);
    }

    public async Task<EvidenceImpactRankingDto?> GetEvidenceImpactRankingAsync(
        string slug,
        string targetNodeId,
        GraphDto graphContext,
        string? benchmarkSetId,
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
            () => _posteriorOddsCalculator.GetEvidenceImpactRanking(
                graph,
                targetNodeId,
                cancellationToken),
            result => CreateEvidenceImpactDetails(graph, targetNodeId, result),
            result => result.SupportingEvidence.Count + result.CounterEvidence.Count,
            _ => PerformanceRunStatuses.Completed,
            benchmarkSetId,
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

    public Task<bool> UpdateNodeAsync(
        string slug,
        string nodeId,
        GraphNodeUpdateDto node,
        CancellationToken cancellationToken = default)
    {
        return UpdateNodeAsync(
            slug,
            nodeId,
            node,
            benchmarkSetId: null,
            cancellationToken);
    }

    public async Task<bool> UpdateNodeAsync(
        string slug,
        string nodeId,
        GraphNodeUpdateDto node,
        string? benchmarkSetId,
        CancellationToken cancellationToken = default)
    {
        if (!node.PriorOdds.HasValue)
        {
            var updatedWithoutReport = await _graphRepository.UpdateNodeAsync(
                slug,
                nodeId,
                node,
                cancellationToken);

            // Kind changes path eligibility and the evidence-leaf base case.
            // Posterior odds changes affect authored evidence strength.
            if (updatedWithoutReport &&
                (node.Kind is not null || node.PosteriorOdds.HasValue))
            {
                await RecalculateAndPersistNodesAndAncestorsAsync(
                    slug,
                    [nodeId],
                    cancellationToken);
            }

            return updatedWithoutReport;
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

        var postUpdateLoadStopwatch = Stopwatch.StartNew();
        var graphForCalculation = await _graphRepository.GetBySlugAsync(
            slug,
            cancellationToken);
        postUpdateLoadStopwatch.Stop();
        totalLoadElapsedMilliseconds +=
            postUpdateLoadStopwatch.Elapsed.TotalMilliseconds;
        if (graphForCalculation is null)
        {
            return true;
        }

        var reportingGraph = graphForCalculation;
        IReadOnlyDictionary<string, decimal> recalculatedLogOdds =
            new Dictionary<string, decimal>();
        PerformanceMeasurementResult computeMeasurement;
        double? persistElapsedMilliseconds = null;
        var compute = PerformanceMeasurement.Start();
        try
        {
            recalculatedLogOdds =
                _posteriorOddsCalculator.RecalculateNodesAndAncestors(
                    graphForCalculation,
                    [nodeId],
                    cancellationToken);
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
                benchmarkSetId,
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
                benchmarkSetId,
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
            triggered: true,
            benchmarkSetId);

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

    private async Task<BoundedMinimalCounterSetDto> GetBoundedMinimalCounterSetAsync(
        Graph graph,
        string dataSource,
        string targetNodeId,
        DateTimeOffset operationStartedAtUtc,
        Stopwatch operationStopwatch,
        double? loadElapsedMilliseconds,
        string? benchmarkSetId,
        CancellationToken cancellationToken)
    {
        var invocation = CreateTargetInvocation(dataSource, targetNodeId);
        invocation.Parameters["timeBudgetMilliseconds"] =
            _boundedMinimalCounterSetSolver.ConfiguredTimeBudgetMilliseconds;

        var reported = await ExecuteReportedCalculationAsync(
            graph,
            CreateMinimalCounterSetAlgorithm(
                PerformanceAlgorithmImplementations.TimeBoundedExhaustive),
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
            result => result.StopReason == MinimalCounterSetStopReason.TimeBudget
                ? PerformanceRunStatuses.TimedOut
                : PerformanceRunStatuses.Completed,
            benchmarkSetId,
            cancellationToken);

        return new BoundedMinimalCounterSetDto
        {
            CounterNodeIds = reported.Result.ThresholdReached
                ? reported.Result.CounterNodeIds.ToList()
                : null,
            ProofStatus = ToProofStatusValue(reported.Result.ProofStatus),
            Status = reported.StoredRun.Outcome.Status,
            StopReason = ToStopReasonValue(reported.Result.StopReason),
            TimeBudgetMilliseconds =
                reported.Result.TimeBudgetMilliseconds ??
                _boundedMinimalCounterSetSolver.ConfiguredTimeBudgetMilliseconds,
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
        string? benchmarkSetId,
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
                BenchmarkSetId = NormalizeBenchmarkSetId(benchmarkSetId),
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
                BenchmarkSetId = NormalizeBenchmarkSetId(benchmarkSetId),
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
        string? benchmarkSetId,
        Exception? exception = null)
    {
        operationStopwatch.Stop();
        var isLeaf = !graph.Edges.Any(edge =>
            string.Equals(edge.To, changedNodeId, StringComparison.Ordinal));
        var details = new JsonObject
        {
            ["triggered"] = triggered,
            ["recalculationScope"] = "node-and-ancestors",
            ["affectedNodeCount"] = recalculatedLogOdds.Count,
            ["maximumAncestorDistance"] =
                TryGetMaximumAncestorDistance(graph, changedNodeId),
            ["persistedRowCount"] = persistedRowCount,
            ["changedNodeKind"] = changedNodeKind,
            ["isLeaf"] = isLeaf
        };
        var run = new PerformanceRunRecord
        {
            BenchmarkSetId = NormalizeBenchmarkSetId(benchmarkSetId),
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
        string? benchmarkSetId,
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
                benchmarkSetId,
                exception: exception);
        }
        catch
        {
            // Reporting must not replace the update's original failure.
        }
    }

    private static string? NormalizeBenchmarkSetId(string? benchmarkSetId)
    {
        return string.IsNullOrWhiteSpace(benchmarkSetId)
            ? null
            : benchmarkSetId.Trim();
    }

    private static PerformanceAlgorithmInfo CreateMinimalCounterSetAlgorithm(
        string implementation)
    {
        return new PerformanceAlgorithmInfo
        {
            Name = PerformanceAlgorithmNames.MinimalCounterSet,
            Implementation = implementation,
            CalculationModel = "graph-posterior-odds-calculator"
        };
    }

    private static PerformanceAlgorithmInfo CreateCurrentAlgorithm(string name)
    {
        return new PerformanceAlgorithmInfo
        {
            Name = name,
            Implementation = PerformanceAlgorithmImplementations.Current,
            CalculationModel = "graph-posterior-odds-calculator"
        };
    }

    private static PerformanceAlgorithmInfo CreateLeafUpdateAlgorithm()
    {
        return new PerformanceAlgorithmInfo
        {
            Name = PerformanceAlgorithmNames.LeafUpdate,
            Implementation = PerformanceAlgorithmImplementations.Current,
            CalculationModel = "graph-posterior-odds-calculator"
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
                    BayesianMinimalCounterSetEvaluator.DefaultThresholdLogOdds
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
            ["totalCandidateCount"] = JsonValue.Create(result.TotalCandidateCount),
            ["searchedCandidateCount"] = JsonValue.Create(result.SearchedCandidateCount),
            ["excludedCandidateCount"] = JsonValue.Create(result.ExcludedCandidateCount),
            ["candidatesExamined"] = result.CandidatesExamined,
            ["subsetEvaluations"] = result.SubsetEvaluations,
            ["largestCardinalityFullyExhausted"] =
                JsonValue.Create(result.LargestCardinalityFullyExhausted),
            ["activeCardinality"] = JsonValue.Create(result.ActiveCardinality),
            ["subsetEvaluationsAtActiveCardinality"] =
                JsonValue.Create(result.SubsetEvaluationsAtActiveCardinality),
            ["totalSubsetsAtActiveCardinality"] =
                result.TotalSubsetsAtActiveCardinality,
            ["totalPossibleSubsets"] = result.TotalPossibleSubsets,
            ["timeBudgetMilliseconds"] =
                JsonValue.Create(result.TimeBudgetMilliseconds),
            ["preparationElapsedMilliseconds"] =
                JsonValue.Create(result.PreparationElapsedMilliseconds),
            ["searchElapsedMilliseconds"] =
                JsonValue.Create(result.SearchElapsedMilliseconds),
            ["subsetEvaluationsPerSecond"] =
                JsonValue.Create(result.SubsetEvaluationsPerSecond),
            ["timeoutStage"] = ToTimeoutStageValue(result.TimeoutStage),
            ["returnedSetSize"] = result.CounterNodeIds.Count,
            ["bestSetSize"] = result.CounterNodeIds.Count,
            ["thresholdLogOdds"] = JsonValue.Create(result.ThresholdLogOdds),
            ["initialTargetLogOdds"] =
                JsonValue.Create(result.InitialTargetLogOdds),
            ["finalTargetLogOdds"] = JsonValue.Create(result.FinalTargetLogOdds),
            ["bestTargetLogOdds"] = JsonValue.Create(result.FinalTargetLogOdds),
            ["thresholdReached"] = result.ThresholdReached,
            ["proofStatus"] = ToProofStatusValue(result.ProofStatus),
            ["stopReason"] = ToStopReasonValue(result.StopReason),
            ["returnedNodeIds"] = new JsonArray(
                result.CounterNodeIds
                    .Take(MinimalCounterSetPreviewLimit)
                    .Select(nodeId => JsonValue.Create(nodeId))
                    .ToArray()),
            ["returnedNodeIdsTruncated"] =
                result.CounterNodeIds.Count > MinimalCounterSetPreviewLimit,
            ["bestNodeIds"] = new JsonArray(
                result.CounterNodeIds
                    .Take(MinimalCounterSetPreviewLimit)
                    .Select(nodeId => JsonValue.Create(nodeId))
                    .ToArray()),
            ["bestNodeIdsTruncated"] =
                result.CounterNodeIds.Count > MinimalCounterSetPreviewLimit
        };
    }

    private static JsonObject CreateEvidenceImpactDetails(
        Graph graph,
        string targetNodeId,
        EvidenceImpactRankingDto result)
    {
        var reachableEvidenceCount = CountReachableEvidenceNodes(graph, targetNodeId);
        var neutralEvidenceCount = result.SupportingEvidence
            .Concat(result.CounterEvidence)
            .Count(impact => impact.ProbabilityDifference == 0d);
        return new JsonObject
        {
            ["reachableNodeCount"] =
                PerformanceRunMetadataCapture.CountReachableNodes(graph, targetNodeId),
            ["reachableEvidenceCount"] = reachableEvidenceCount,
            ["supportingResultCount"] = result.SupportingEvidence.Count,
            ["counterResultCount"] = result.CounterEvidence.Count,
            ["neutralEvidenceCount"] = neutralEvidenceCount,
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
                    ["targetLogOddsImpact"] = item.LogLr,
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
            MinimalCounterSetStopReason.TimeBudget => "timeBudget",
            _ => "completed"
        };
    }

    private static string? ToTimeoutStageValue(
        MinimalCounterSetTimeoutStage? timeoutStage)
    {
        return timeoutStage switch
        {
            MinimalCounterSetTimeoutStage.Preparation => "preparation",
            MinimalCounterSetTimeoutStage.Search => "search",
            _ => null
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
