using Backend.Models.Domain;

namespace Backend.Calculation;

/// <summary>
/// Orchestrates graph pruning, Bayes-factor calculation, and the final
/// log-odds update. Evidence-leaf posterior odds are treated as authored
/// observations, and only structural leaves of the current pruned graph are
/// used as leaf Bayes-factor inputs.
/// </summary>
/// <remarks>
/// PriorOdds and PosteriorOdds store natural-log odds. Persistence initializes
/// evidence and objection PriorOdds to zero, so their authored PosteriorOdds
/// directly equal log(BF); the subtraction remains explicit here. A
/// non-evidence target without downstream evidence is BF-neutral. The final
/// update is PosteriorOdds(H) = PriorOdds(H) + log(BF_H), clamped to the
/// supported log-odds range. CalculateNodeLogPosteriorOdds is read-only;
/// RecalculateAncestors and RecalculateNodesAndAncestors update node state.
/// </remarks>
public sealed class GraphPosteriorOddsCalculator
{
    private const decimal MinLogOdds = -100m;
    private const decimal MaxLogOdds = 100m;

    private readonly GraphBayesFactorPruner _pruner;
    private readonly GraphBayesFactorCalculator _bayesFactorCalculator;

    /// <summary>Creates an orchestrator with the default pruning and BF components.</summary>
    public GraphPosteriorOddsCalculator()
        : this(new GraphBayesFactorPruner(), new GraphBayesFactorCalculator())
    {
    }

    /// <summary>Creates an orchestrator from explicit calculation components.</summary>
    public GraphPosteriorOddsCalculator(
        GraphBayesFactorPruner pruner,
        GraphBayesFactorCalculator bayesFactorCalculator)
    {
        ArgumentNullException.ThrowIfNull(pruner);
        ArgumentNullException.ThrowIfNull(bayesFactorCalculator);

        _pruner = pruner;
        _bayesFactorCalculator = bayesFactorCalculator;
    }

    /// <summary>
    /// Calculates one node's posterior log odds without mutating the graph.
    /// </summary>
    public decimal CalculateNodeLogPosteriorOdds(
        Graph graph,
        string hypothesisNodeId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(hypothesisNodeId);
        cancellationToken.ThrowIfCancellationRequested();

        var hypothesis = graph.Nodes.FirstOrDefault(
            node => string.Equals(
                node.Id,
                hypothesisNodeId,
                StringComparison.Ordinal));
        if (hypothesis is null)
        {
            throw new InvalidOperationException(
                $"Hypothesis node '{hypothesisNodeId}' does not exist in the graph.");
        }

        // Stage 1: select one compatible evidence-path continuation at every
        // merge before combining any Bayes-factor contributions.
        Graph prunedGraph = _pruner.Prune(
            graph,
            hypothesisNodeId,
            cancellationToken);

        // Stage 2: construct the base-case log BF for each structural leaf.
        var nodeIdsWithChildren = prunedGraph.Edges
            .Select(edge => edge.To)
            .ToHashSet(StringComparer.Ordinal);
        var leafLogBayesFactors = new Dictionary<string, decimal>(
            StringComparer.Ordinal);

        foreach (var leaf in prunedGraph.Nodes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (nodeIdsWithChildren.Contains(leaf.Id))
            {
                continue;
            }

            // Evidence and objection leaves carry an externally authored
            // observation: log(BF_E) = log(O_post(E)) - log(O_prior(E)).
            // A hypothesis with no downstream evidence is BF-neutral, which
            // avoids feeding a stale calculated posterior back into itself.
            leafLogBayesFactors[leaf.Id] = IsEvidenceKind(leaf.Kind)
                ? checked(leaf.PosteriorOdds - leaf.PriorOdds)
                : 0m;
        }

        // Stage 3: evaluate the pruned DAG from its leaves to the hypothesis.
        decimal hypothesisLogBayesFactor =
            _bayesFactorCalculator.CalculateLogBayesFactor(
                prunedGraph,
                hypothesisNodeId,
                leafLogBayesFactors,
                cancellationToken);

        // Stage 4: PriorOdds already stores log prior odds, so add log(BF_H)
        // directly and clamp the persisted representation's supported range.
        decimal posteriorLogOdds = checked(
            hypothesis.PriorOdds + hypothesisLogBayesFactor);
        return Math.Clamp(posteriorLogOdds, MinLogOdds, MaxLogOdds);
    }

    /// <summary>Recalculates and mutates every ancestor, excluding the changed node.</summary>
    public Dictionary<string, decimal> RecalculateAncestors(
        Graph graph,
        string changedNodeId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(changedNodeId);
        cancellationToken.ThrowIfCancellationRequested();

        var context = GraphCalculationContext.From(graph.Nodes, graph.Edges);
        if (!context.NodesById.ContainsKey(changedNodeId))
        {
            throw new InvalidOperationException(
                $"Changed node '{changedNodeId}' does not exist in the calculation context.");
        }

        var affectedDistances = CollectAffectedAncestorDistances(
            context,
            changedNodeId,
            cancellationToken);
        return RecalculateAffectedNodes(
            graph,
            affectedDistances,
            cancellationToken);
    }

    /// <summary>Recalculates and mutates the supplied nodes and affected ancestors.</summary>
    public Dictionary<string, decimal> RecalculateNodesAndAncestors(
        Graph graph,
        IEnumerable<string> nodeIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(nodeIds);
        cancellationToken.ThrowIfCancellationRequested();

        var context = GraphCalculationContext.From(graph.Nodes, graph.Edges);
        var affectedDistances = CollectAffectedNodeAndAncestorDistances(
            context,
            nodeIds,
            cancellationToken);
        return RecalculateAffectedNodes(
            graph,
            affectedDistances,
            cancellationToken);
    }

    /// <summary>Calculates and applies affected values from descendants upward.</summary>
    private Dictionary<string, decimal> RecalculateAffectedNodes(
        Graph graph,
        Dictionary<string, int> affectedDistances,
        CancellationToken cancellationToken)
    {
        var nodesById = graph.Nodes.ToDictionary(
            node => node.Id,
            StringComparer.Ordinal);
        var recalculatedValues = new Dictionary<string, decimal>(
            affectedDistances.Count,
            StringComparer.Ordinal);

        foreach (string nodeId in affectedDistances
                     .OrderBy(affected => affected.Value)
                     .ThenBy(affected => affected.Key, StringComparer.Ordinal)
                     .Select(affected => affected.Key))
        {
            cancellationToken.ThrowIfCancellationRequested();
            decimal posteriorLogOdds = CalculateNodeLogPosteriorOdds(
                graph,
                nodeId,
                cancellationToken);
            nodesById[nodeId].PosteriorOdds = posteriorLogOdds;
            recalculatedValues[nodeId] = posteriorLogOdds;
        }

        return recalculatedValues;
    }

    /// <summary>Collects supplied nodes and ancestors with their maximum distance.</summary>
    private static Dictionary<string, int> CollectAffectedNodeAndAncestorDistances(
        GraphCalculationContext context,
        IEnumerable<string> nodeIds,
        CancellationToken cancellationToken)
    {
        var affectedDistances = new Dictionary<string, int>(
            StringComparer.Ordinal);
        var stack = new Stack<AncestorTraversalState>();

        foreach (string nodeId in nodeIds.Distinct(StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!context.NodesById.ContainsKey(nodeId))
            {
                throw new InvalidOperationException(
                    $"Node '{nodeId}' does not exist in the calculation context.");
            }

            affectedDistances[nodeId] = 0;
            stack.Push(new AncestorTraversalState(nodeId, 0, [nodeId]));
        }

        CollectAncestorDistances(
            context,
            stack,
            affectedDistances,
            cancellationToken);

        return affectedDistances;
    }

    /// <summary>Collects ancestors of one node with their maximum distance.</summary>
    private static Dictionary<string, int> CollectAffectedAncestorDistances(
        GraphCalculationContext context,
        string changedNodeId,
        CancellationToken cancellationToken)
    {
        var affectedDistances = new Dictionary<string, int>(
            StringComparer.Ordinal);
        var stack = new Stack<AncestorTraversalState>();
        stack.Push(new AncestorTraversalState(
            changedNodeId,
            0,
            [changedNodeId]));

        CollectAncestorDistances(
            context,
            stack,
            affectedDistances,
            cancellationToken);

        return affectedDistances;
    }

    /// <summary>Traverses child-to-parent edges while detecting reachable cycles.</summary>
    private static void CollectAncestorDistances(
        GraphCalculationContext context,
        Stack<AncestorTraversalState> stack,
        Dictionary<string, int> affectedDistances,
        CancellationToken cancellationToken)
    {
        while (stack.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var current = stack.Pop();
            if (!context.ParentEdgesByChildId.TryGetValue(
                    current.NodeId,
                    out var parentEdges))
            {
                continue;
            }

            foreach (var parentEdge in parentEdges)
            {
                string parentNodeId = parentEdge.ToNodeId;
                if (current.Path.Contains(parentNodeId))
                {
                    throw new InvalidOperationException(
                        "Cycle detected while recalculating graph posterior " +
                        $"odds at node '{parentNodeId}'.");
                }

                int nextDistance = current.Distance + 1;
                if (!affectedDistances.TryGetValue(
                        parentNodeId,
                        out int existingDistance) ||
                    nextDistance > existingDistance)
                {
                    affectedDistances[parentNodeId] = nextDistance;
                }

                var nextPath = new HashSet<string>(
                    current.Path,
                    StringComparer.Ordinal)
                {
                    parentNodeId
                };
                stack.Push(new AncestorTraversalState(
                    parentNodeId,
                    nextDistance,
                    nextPath));
            }
        }
    }

    /// <summary>Identifies node kinds that can supply an authored leaf log BF.</summary>
    private static bool IsEvidenceKind(string nodeKind)
    {
        return string.Equals(
                   nodeKind,
                   "evidence",
                   StringComparison.OrdinalIgnoreCase) ||
               string.Equals(
                   nodeKind,
                   "objection",
                   StringComparison.OrdinalIgnoreCase);
    }

    private sealed record AncestorTraversalState(
        string NodeId,
        int Distance,
        HashSet<string> Path);
}
