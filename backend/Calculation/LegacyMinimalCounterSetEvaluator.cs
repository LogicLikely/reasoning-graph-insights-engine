using Backend.Models.Domain;

namespace Backend.Calculation.MinimalCounterSets;

/// <summary>
/// Prepares the legacy likelihood-ratio inputs shared by the greedy and bounded
/// minimal-counter-set solvers. This intentionally preserves the calculation
/// used by GraphService before the solvers were extracted.
/// </summary>
public sealed class LegacyMinimalCounterSetEvaluator : IMinimalCounterSetEvaluator
{
    public const decimal DefaultThresholdLogOdds = -1m;

    private readonly GraphLikelihoodCalculator _calculator;

    public LegacyMinimalCounterSetEvaluator(GraphLikelihoodCalculator calculator)
    {
        _calculator = calculator ?? throw new ArgumentNullException(nameof(calculator));
    }

    public IMinimalCounterSetProblem CreateProblem(
        Graph graph,
        string targetNodeId,
        IEnumerable<string> nodeIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetNodeId);
        ArgumentNullException.ThrowIfNull(nodeIds);
        cancellationToken.ThrowIfCancellationRequested();

        var selectedNodeIds = nodeIds
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var context = GraphCalculationContext.From(graph.Nodes, graph.Edges);

        if (!context.NodesById.ContainsKey(targetNodeId))
        {
            throw new InvalidOperationException(
                $"Target node '{targetNodeId}' does not exist in the calculation context.");
        }

        var registeredNodeIds = ExcludeCounterNodes(context, selectedNodeIds);
        if (!registeredNodeIds.Contains(targetNodeId, StringComparer.Ordinal))
        {
            registeredNodeIds.Add(targetNodeId);
        }

        var candidates = GetCandidates(
            context,
            targetNodeId,
            selectedNodeIds,
            cancellationToken);

        cancellationToken.ThrowIfCancellationRequested();
        var normalLogOdds = _calculator.RecalculateNodesAndAncestors(
            context,
            selectedNodeIds);

        cancellationToken.ThrowIfCancellationRequested();
        var recalculatedLogOdds = _calculator.RecalculateNodesAndAncestors(
            context,
            registeredNodeIds);

        if (!recalculatedLogOdds.TryGetValue(targetNodeId, out var initialTargetLogOdds))
        {
            throw new InvalidOperationException(
                $"Target node '{targetNodeId}' does not exist in the recalculated log-odds dictionary.");
        }

        return new LegacyMinimalCounterSetProblem(
            _calculator,
            context,
            targetNodeId,
            candidates,
            normalLogOdds,
            initialTargetLogOdds);
    }

    private static IReadOnlyList<MinimalCounterCandidate> GetCandidates(
        GraphCalculationContext context,
        string targetNodeId,
        IEnumerable<string> nodeIds,
        CancellationToken cancellationToken)
    {
        var candidates = new List<MinimalCounterCandidate>();

        foreach (var nodeId in nodeIds)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!context.NodesById.TryGetValue(nodeId, out var node))
            {
                throw new InvalidOperationException(
                    $"Node '{nodeId}' does not exist in the calculation context.");
            }

            if (string.Equals(nodeId, targetNodeId, StringComparison.Ordinal) ||
                !IsCounterNode(node))
            {
                continue;
            }

            var multiplier = GetAncestorLikelihoodMultiplier(
                context,
                nodeId,
                targetNodeId,
                cancellationToken);
            if (!multiplier.HasValue)
            {
                continue;
            }

            candidates.Add(new MinimalCounterCandidate(
                nodeId,
                node.PosteriorOdds * multiplier.Value));
        }

        return candidates;
    }

    private static decimal? GetAncestorLikelihoodMultiplier(
        GraphCalculationContext context,
        string startNodeId,
        string targetNodeId,
        CancellationToken cancellationToken)
    {
        var stack = new Stack<CounterTraversalState>();
        stack.Push(new CounterTraversalState(startNodeId, 1m, [startNodeId]));

        decimal? bestMultiplier = null;
        while (stack.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var current = stack.Pop();
            if (string.Equals(current.NodeId, targetNodeId, StringComparison.Ordinal))
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

                var nextMultiplier = current.Multiplier *
                    (parentEdge.ImportanceToParent / 10m);
                var nextPath = new HashSet<string>(current.Path, StringComparer.Ordinal)
                {
                    parentNodeId
                };
                stack.Push(new CounterTraversalState(
                    parentNodeId,
                    nextMultiplier,
                    nextPath));
            }
        }

        return bestMultiplier;
    }

    private static bool IsCounterNode(GraphNodeCalcState node)
    {
        return string.Equals(node.Kind, "objection", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(node.Kind, "counter", StringComparison.OrdinalIgnoreCase);
    }

    private static List<string> ExcludeCounterNodes(
        GraphCalculationContext context,
        IEnumerable<string> nodeIds)
    {
        return nodeIds
            .Where(nodeId =>
            {
                if (!context.NodesById.TryGetValue(nodeId, out var node))
                {
                    throw new InvalidOperationException(
                        $"Node '{nodeId}' does not exist in the calculation context.");
                }

                return !IsCounterNode(node);
            })
            .ToList();
    }

    private sealed record CounterTraversalState(
        string NodeId,
        decimal Multiplier,
        HashSet<string> Path);

    private sealed class LegacyMinimalCounterSetProblem : IMinimalCounterSetProblem
    {
        private readonly GraphLikelihoodCalculator _calculator;
        private readonly GraphCalculationContext _context;
        private readonly string _targetNodeId;
        private readonly IReadOnlyDictionary<string, decimal> _normalLogOdds;
        private readonly Dictionary<string, decimal> _contributions =
            new(StringComparer.Ordinal);
        private readonly HashSet<string> _candidateNodeIds;

        public LegacyMinimalCounterSetProblem(
            GraphLikelihoodCalculator calculator,
            GraphCalculationContext context,
            string targetNodeId,
            IReadOnlyList<MinimalCounterCandidate> candidates,
            IReadOnlyDictionary<string, decimal> normalLogOdds,
            decimal initialTargetLogOdds)
        {
            _calculator = calculator;
            _context = context;
            _targetNodeId = targetNodeId;
            _normalLogOdds = normalLogOdds;
            Candidates = candidates;
            InitialTargetLogOdds = initialTargetLogOdds;
            _candidateNodeIds = candidates
                .Select(candidate => candidate.NodeId)
                .ToHashSet(StringComparer.Ordinal);
        }

        public decimal ThresholdLogOdds => DefaultThresholdLogOdds;

        public decimal InitialTargetLogOdds { get; }

        public IReadOnlyList<MinimalCounterCandidate> Candidates { get; }

        public decimal GetTargetLogOddsContribution(
            string counterNodeId,
            CancellationToken cancellationToken = default)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(counterNodeId);
            cancellationToken.ThrowIfCancellationRequested();

            if (!_candidateNodeIds.Contains(counterNodeId))
            {
                throw new InvalidOperationException(
                    $"Node '{counterNodeId}' is not a counter candidate for target '{_targetNodeId}'.");
            }

            if (_contributions.TryGetValue(counterNodeId, out var contribution))
            {
                return contribution;
            }

            if (!_normalLogOdds.TryGetValue(counterNodeId, out var counterLogOdds))
            {
                throw new InvalidOperationException(
                    $"Counter node '{counterNodeId}' does not exist in the normal log-odds dictionary.");
            }

            var counterLikelihoodRatio = _calculator.GetSingleAccumulatedLR(
                _context,
                counterNodeId,
                _targetNodeId);
            cancellationToken.ThrowIfCancellationRequested();

            contribution = counterLikelihoodRatio.HasValue
                ? counterLogOdds +
                  (decimal)Math.Log((double)counterLikelihoodRatio.Value)
                : 0m;
            _contributions[counterNodeId] = contribution;
            return contribution;
        }
    }
}
