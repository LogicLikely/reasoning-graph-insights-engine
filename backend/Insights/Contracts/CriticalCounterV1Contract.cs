using Backend.Models.Domain;

namespace Backend.Insights.Contracts;

public sealed class CriticalCounterSelectionOutcome
{
    private CriticalCounterSelectionOutcome(
        IReadOnlyList<string> selectedNodeIds,
        decimal resultingLogOdds,
        decimal thresholdLogOdds)
    {
        SelectedNodeIds = selectedNodeIds;
        ResultingLogOdds = resultingLogOdds;
        ThresholdLogOdds = thresholdLogOdds;
    }

    public IReadOnlyList<string> SelectedNodeIds { get; }

    public decimal ResultingLogOdds { get; }

    public decimal ThresholdLogOdds { get; }

    public bool ThresholdAttained =>
        CriticalCounterV1Contract.IsThresholdAttained(ResultingLogOdds, ThresholdLogOdds);

    public decimal BelowThresholdMargin => ThresholdLogOdds - ResultingLogOdds;

    public static CriticalCounterSelectionOutcome Create(
        IEnumerable<string> selectedNodeIds,
        decimal resultingLogOdds,
        decimal thresholdLogOdds = CriticalCounterV1Contract.DefaultThresholdLogOdds)
    {
        return new CriticalCounterSelectionOutcome(
            CriticalCounterV1Contract.NormalizeNodeIdSequence(selectedNodeIds),
            resultingLogOdds,
            thresholdLogOdds);
    }
}

public sealed class CriticalCounterSelectionOutcomeComparer
    : IComparer<CriticalCounterSelectionOutcome>
{
    public static CriticalCounterSelectionOutcomeComparer Instance { get; } = new();

    private CriticalCounterSelectionOutcomeComparer()
    {
    }

    public int Compare(CriticalCounterSelectionOutcome? left, CriticalCounterSelectionOutcome? right)
    {
        if (ReferenceEquals(left, right))
        {
            return 0;
        }

        if (left is null)
        {
            return 1;
        }

        if (right is null)
        {
            return -1;
        }

        if (left.ThresholdAttained != right.ThresholdAttained)
        {
            return left.ThresholdAttained ? -1 : 1;
        }

        var cardinalityComparison = left.SelectedNodeIds.Count.CompareTo(right.SelectedNodeIds.Count);
        if (cardinalityComparison != 0)
        {
            return cardinalityComparison;
        }

        var marginComparison = right.BelowThresholdMargin.CompareTo(left.BelowThresholdMargin);
        if (marginComparison != 0)
        {
            return marginComparison;
        }

        for (var index = 0; index < left.SelectedNodeIds.Count; index++)
        {
            var nodeIdComparison = StringComparer.Ordinal.Compare(
                left.SelectedNodeIds[index],
                right.SelectedNodeIds[index]);
            if (nodeIdComparison != 0)
            {
                return nodeIdComparison;
            }
        }

        return 0;
    }
}

public sealed record CriticalCounterActiveProjection(
    Graph Graph,
    IReadOnlyList<string> EligibleCandidateNodeIds,
    IReadOnlyList<string> AppliedCandidateNodeIds);

public static class CriticalCounterV1Contract
{
    public const string SemanticVersion = AlgorithmSemanticIdentities.CriticalCounterV1;
    public const string ExactStrategy = OperationStrategyNames.Exact;
    public const string GreedyStrategy = OperationStrategyNames.Greedy;
    public const string AutoStrategy = OperationStrategyNames.Auto;
    public const decimal DefaultThresholdLogOdds = -1m;

    public const string EvaluationRule =
        "Rebuild an active graph from immutable input and run the standard versioned likelihood recalculation.";

    public const string RemovalRule =
        "Remove every eligible candidate node and every incident edge; restore selected nodes and only original edges whose endpoints are active.";

    private static readonly IReadOnlyList<string> StrategyValues =
        Array.AsReadOnly(new[] { ExactStrategy, GreedyStrategy, AutoStrategy });

    private static readonly IReadOnlyList<string> CandidateKindValues =
        Array.AsReadOnly(new[] { "objection", "counter" });

    public static IReadOnlyList<string> Strategies => StrategyValues;

    public static IReadOnlyList<string> CandidateKinds => CandidateKindValues;

    public static bool IsKnownStrategy(string? strategy) =>
        Strategies.Contains(strategy ?? string.Empty, StringComparer.Ordinal);

    public static bool IsCandidateKind(string? nodeKind) =>
        CandidateKinds.Contains(nodeKind ?? string.Empty, StringComparer.OrdinalIgnoreCase);

    public static bool IsThresholdAttained(decimal resultingLogOdds, decimal thresholdLogOdds) =>
        resultingLogOdds <= thresholdLogOdds;

    public static AlgorithmGraphContractValidationResult ValidateGraph(Graph graph) =>
        AlgorithmGraphContractValidation.ValidateDirectedAcyclicGraph(graph);

    public static IReadOnlyList<string> GetEligibleCandidateNodeIds(
        Graph graph,
        string targetNodeId)
    {
        EnsureValidGraphAndTarget(graph, targetNodeId);

        return Array.AsReadOnly(
            graph.Nodes
                .Where(node => IsCandidateKind(node.Kind))
                .Where(node => !string.Equals(node.Id, targetNodeId, StringComparison.Ordinal))
                .Where(node => HasDirectedStructuralPath(graph, node.Id, targetNodeId))
                .Select(node => node.Id)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(nodeId => nodeId, StringComparer.Ordinal)
                .ToArray());
    }

    public static bool IsEligibleCandidate(
        Graph graph,
        string targetNodeId,
        string candidateNodeId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(candidateNodeId);

        return GetEligibleCandidateNodeIds(graph, targetNodeId)
            .Contains(candidateNodeId, StringComparer.Ordinal);
    }

    public static CriticalCounterActiveProjection BuildActiveProjection(
        Graph immutableInput,
        string targetNodeId,
        IEnumerable<string> selectedCandidateNodeIds)
    {
        ArgumentNullException.ThrowIfNull(selectedCandidateNodeIds);

        var eligibleCandidateNodeIds = GetEligibleCandidateNodeIds(immutableInput, targetNodeId);
        var eligibleCandidateSet = eligibleCandidateNodeIds.ToHashSet(StringComparer.Ordinal);
        var selectedNodeIds = NormalizeNodeIdSequence(selectedCandidateNodeIds);

        foreach (var selectedNodeId in selectedNodeIds)
        {
            if (!eligibleCandidateSet.Contains(selectedNodeId))
            {
                throw new ArgumentException(
                    $"Selected node '{selectedNodeId}' is not an eligible critical-counter candidate for target '{targetNodeId}'.",
                    nameof(selectedCandidateNodeIds));
            }
        }

        var selectedNodeIdSet = selectedNodeIds.ToHashSet(StringComparer.Ordinal);
        var activeNodeIds = immutableInput.Nodes
            .Where(node => !eligibleCandidateSet.Contains(node.Id) || selectedNodeIdSet.Contains(node.Id))
            .Select(node => node.Id)
            .ToHashSet(StringComparer.Ordinal);

        var projection = new Graph
        {
            Id = immutableInput.Id,
            Slug = immutableInput.Slug,
            Title = immutableInput.Title,
            Description = immutableInput.Description,
            Nodes = immutableInput.Nodes
                .Where(node => activeNodeIds.Contains(node.Id))
                .Select(CloneNode)
                .ToList(),
            Edges = immutableInput.Edges
                .Where(edge => activeNodeIds.Contains(edge.From) && activeNodeIds.Contains(edge.To))
                .Select(CloneEdge)
                .ToList()
        };

        return new CriticalCounterActiveProjection(
            projection,
            eligibleCandidateNodeIds,
            selectedNodeIds);
    }

    internal static IReadOnlyList<string> NormalizeNodeIdSequence(IEnumerable<string> nodeIds)
    {
        ArgumentNullException.ThrowIfNull(nodeIds);

        var normalized = nodeIds.ToArray();
        if (normalized.Any(string.IsNullOrWhiteSpace))
        {
            throw new ArgumentException("Selected node IDs must be non-empty.", nameof(nodeIds));
        }

        if (normalized.Distinct(StringComparer.Ordinal).Count() != normalized.Length)
        {
            throw new ArgumentException("Selected node IDs must be distinct.", nameof(nodeIds));
        }

        Array.Sort(normalized, StringComparer.Ordinal);
        return Array.AsReadOnly(normalized);
    }

    private static bool HasDirectedStructuralPath(Graph graph, string startNodeId, string targetNodeId)
    {
        if (string.Equals(startNodeId, targetNodeId, StringComparison.Ordinal))
        {
            return true;
        }

        var targetsBySource = graph.Edges
            .GroupBy(edge => edge.From, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.Select(edge => edge.To).Distinct(StringComparer.Ordinal).ToArray(),
                StringComparer.Ordinal);
        var pending = new Stack<string>();
        var visited = new HashSet<string>(StringComparer.Ordinal) { startNodeId };
        pending.Push(startNodeId);

        while (pending.Count > 0)
        {
            var currentNodeId = pending.Pop();
            if (!targetsBySource.TryGetValue(currentNodeId, out var targetIds))
            {
                continue;
            }

            foreach (var nextNodeId in targetIds)
            {
                if (string.Equals(nextNodeId, targetNodeId, StringComparison.Ordinal))
                {
                    return true;
                }

                if (visited.Add(nextNodeId))
                {
                    pending.Push(nextNodeId);
                }
            }
        }

        return false;
    }

    private static void EnsureValidGraphAndTarget(Graph graph, string targetNodeId)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetNodeId);

        var validation = ValidateGraph(graph);
        if (!validation.IsValid)
        {
            throw new ArgumentException(
                $"Graph violates the critical-counter-v1 input contract: {string.Join("; ", validation.Issues.Select(issue => issue.Message))}",
                nameof(graph));
        }

        var targetNode = graph.Nodes.FirstOrDefault(node =>
            string.Equals(node.Id, targetNodeId, StringComparison.Ordinal));
        if (targetNode is null)
        {
            throw new ArgumentException(
                $"Target node '{targetNodeId}' is not present in the graph.",
                nameof(targetNodeId));
        }

        if (IsCandidateKind(targetNode.Kind))
        {
            throw new ArgumentException(
                $"Target node '{targetNodeId}' must not have a critical-counter candidate kind.",
                nameof(targetNodeId));
        }
    }

    private static GraphNode CloneNode(GraphNode node)
    {
        return new GraphNode
        {
            Id = node.Id,
            Kind = node.Kind,
            Title = node.Title,
            BodyText = node.BodyText,
            Category = node.Category,
            Tags = node.Tags.ToList(),
            PriorOdds = node.PriorOdds,
            PosteriorOdds = node.PosteriorOdds,
            Evidence = node.Evidence is null
                ? null
                : new GraphEvidenceDetails
                {
                    Type = node.Evidence.Type,
                    Score = node.Evidence.Score,
                    Rationale = node.Evidence.Rationale
                }
        };
    }

    private static GraphEdge CloneEdge(GraphEdge edge)
    {
        return new GraphEdge
        {
            Id = edge.Id,
            From = edge.From,
            To = edge.To,
            Kind = edge.Kind,
            ImportanceToParent = edge.ImportanceToParent
        };
    }
}
