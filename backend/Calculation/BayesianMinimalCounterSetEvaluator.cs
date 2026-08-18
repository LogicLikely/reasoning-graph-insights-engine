using Backend.Models.Domain;

namespace Backend.Calculation.MinimalCounterSets;

/// <summary>
/// Creates minimal-counter problems evaluated with the production Bayes-factor
/// pruning and recurrence calculation.
/// </summary>
public sealed class BayesianMinimalCounterSetEvaluator : IMinimalCounterSetEvaluator
{
    public const decimal DefaultThresholdLogOdds = -1m;

    private readonly GraphPosteriorOddsCalculator _calculator;

    public BayesianMinimalCounterSetEvaluator(
        GraphPosteriorOddsCalculator calculator)
    {
        _calculator = calculator ??
            throw new ArgumentNullException(nameof(calculator));
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

        var nodesById = graph.Nodes.ToDictionary(
            node => node.Id,
            StringComparer.Ordinal);
        if (!nodesById.ContainsKey(targetNodeId))
        {
            throw new InvalidOperationException(
                $"Target node '{targetNodeId}' does not exist in the graph.");
        }

        var availableNodeIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var nodeId in nodeIds)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!nodesById.ContainsKey(nodeId))
            {
                throw new InvalidOperationException(
                    $"Node '{nodeId}' does not exist in the graph.");
            }

            availableNodeIds.Add(nodeId);
        }

        availableNodeIds.Add(targetNodeId);

        var nodeIdsReachingTarget = CollectNodeIdsReachingTarget(
            graph,
            targetNodeId,
            availableNodeIds,
            cancellationToken);

        var candidateNodeIds = availableNodeIds
            .Where(nodeId =>
                !string.Equals(nodeId, targetNodeId, StringComparison.Ordinal) &&
                nodeIdsReachingTarget.Contains(nodeId) &&
                IsCounterNode(nodesById[nodeId]))
            .ToArray();
        var baselineNodeIds = availableNodeIds
            .Where(nodeId => !IsCounterNode(nodesById[nodeId]))
            .ToHashSet(StringComparer.Ordinal);
        baselineNodeIds.Add(targetNodeId);

        var problem = new BayesianMinimalCounterSetProblem(
            _calculator,
            graph,
            targetNodeId,
            baselineNodeIds,
            candidateNodeIds,
            cancellationToken);

        return problem;
    }

    private static bool IsCounterNode(GraphNode node)
    {
        return string.Equals(
            node.Kind,
            "objection",
            StringComparison.OrdinalIgnoreCase);
    }

    private static HashSet<string> CollectNodeIdsReachingTarget(
        Graph graph,
        string targetNodeId,
        IReadOnlySet<string> availableNodeIds,
        CancellationToken cancellationToken)
    {
        var childrenByParentId = graph.Edges
            .GroupBy(edge => edge.To, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.Select(edge => edge.From).ToArray(),
                StringComparer.Ordinal);
        var nodeIdsReachingTarget = new HashSet<string>(StringComparer.Ordinal)
        {
            targetNodeId
        };
        var pending = new Queue<string>();
        pending.Enqueue(targetNodeId);

        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var parentNodeId = pending.Dequeue();
            if (!childrenByParentId.TryGetValue(parentNodeId, out var childNodeIds))
            {
                continue;
            }

            foreach (var childNodeId in childNodeIds)
            {
                if (availableNodeIds.Contains(childNodeId) &&
                    nodeIdsReachingTarget.Add(childNodeId))
                {
                    pending.Enqueue(childNodeId);
                }
            }
        }

        return nodeIdsReachingTarget;
    }

    private sealed class BayesianMinimalCounterSetProblem : IMinimalCounterSetProblem
    {
        private readonly GraphPosteriorOddsCalculator _calculator;
        private readonly Graph _source;
        private readonly string _targetNodeId;
        private readonly HashSet<string> _baselineNodeIds;
        private readonly HashSet<string> _candidateNodeIds;
        private readonly Dictionary<string, decimal> _singletonTargetLogOdds =
            new(StringComparer.Ordinal);

        public BayesianMinimalCounterSetProblem(
            GraphPosteriorOddsCalculator calculator,
            Graph source,
            string targetNodeId,
            HashSet<string> baselineNodeIds,
            IReadOnlyList<string> candidateNodeIds,
            CancellationToken cancellationToken)
        {
            _calculator = calculator;
            _source = source;
            _targetNodeId = targetNodeId;
            _baselineNodeIds = baselineNodeIds;
            _candidateNodeIds = candidateNodeIds.ToHashSet(StringComparer.Ordinal);

            InitialTargetLogOdds = CalculateTargetLogOddsCore(
                Array.Empty<string>(),
                cancellationToken);
            Candidates = candidateNodeIds
                .Select(candidateNodeId => new MinimalCounterCandidate(
                    candidateNodeId,
                    GreedyPriority: 0m))
                .ToArray();
        }

        public decimal ThresholdLogOdds => DefaultThresholdLogOdds;

        public decimal InitialTargetLogOdds { get; }

        public IReadOnlyList<MinimalCounterCandidate> Candidates { get; }

        public decimal GetGreedyPriority(
            MinimalCounterCandidate candidate,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(candidate);
            cancellationToken.ThrowIfCancellationRequested();
            ValidateCandidate(candidate.NodeId);

            return checked(
                InitialTargetLogOdds -
                GetSingletonTargetLogOdds(
                    candidate.NodeId,
                    cancellationToken));
        }

        public decimal CalculateTargetLogOdds(
            IReadOnlyList<string> counterNodeIds,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(counterNodeIds);
            cancellationToken.ThrowIfCancellationRequested();

            if (counterNodeIds.Count == 0)
            {
                return InitialTargetLogOdds;
            }

            ValidateCandidates(counterNodeIds, cancellationToken);
            if (counterNodeIds.Count == 1)
            {
                return GetSingletonTargetLogOdds(
                    counterNodeIds[0],
                    cancellationToken);
            }

            return CalculateTargetLogOddsCore(
                counterNodeIds,
                cancellationToken);
        }

        public decimal GetTargetLogOddsContribution(
            string counterNodeId,
            CancellationToken cancellationToken = default)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(counterNodeId);
            cancellationToken.ThrowIfCancellationRequested();
            ValidateCandidate(counterNodeId);

            return checked(
                GetSingletonTargetLogOdds(
                    counterNodeId,
                    cancellationToken) -
                InitialTargetLogOdds);
        }

        private decimal GetSingletonTargetLogOdds(
            string counterNodeId,
            CancellationToken cancellationToken)
        {
            if (_singletonTargetLogOdds.TryGetValue(
                    counterNodeId,
                    out var targetLogOdds))
            {
                return targetLogOdds;
            }

            targetLogOdds = CalculateTargetLogOddsCore(
                [counterNodeId],
                cancellationToken);
            _singletonTargetLogOdds[counterNodeId] = targetLogOdds;
            return targetLogOdds;
        }

        private decimal CalculateTargetLogOddsCore(
            IReadOnlyList<string> counterNodeIds,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var includedNodeIds = new HashSet<string>(
                _baselineNodeIds,
                StringComparer.Ordinal);
            foreach (var counterNodeId in counterNodeIds)
            {
                cancellationToken.ThrowIfCancellationRequested();
                includedNodeIds.Add(counterNodeId);
            }

            var graph = new Graph
            {
                Id = _source.Id,
                Slug = _source.Slug,
                Title = _source.Title,
                Description = _source.Description,
                Nodes = _source.Nodes
                    .Where(node => includedNodeIds.Contains(node.Id))
                    .ToList(),
                Edges = _source.Edges
                    .Where(edge =>
                        includedNodeIds.Contains(edge.From) &&
                        includedNodeIds.Contains(edge.To))
                    .ToList()
            };

            return _calculator.CalculateNodeLogPosteriorOdds(
                graph,
                _targetNodeId,
                cancellationToken);
        }

        private void ValidateCandidates(
            IReadOnlyList<string> counterNodeIds,
            CancellationToken cancellationToken)
        {
            var seenNodeIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var counterNodeId in counterNodeIds)
            {
                cancellationToken.ThrowIfCancellationRequested();
                ValidateCandidate(counterNodeId);
                if (!seenNodeIds.Add(counterNodeId))
                {
                    throw new InvalidOperationException(
                        $"Counter node '{counterNodeId}' was selected more than once.");
                }
            }
        }

        private void ValidateCandidate(string counterNodeId)
        {
            if (!_candidateNodeIds.Contains(counterNodeId))
            {
                throw new InvalidOperationException(
                    $"Node '{counterNodeId}' is not a counter candidate for target '{_targetNodeId}'.");
            }
        }
    }
}
