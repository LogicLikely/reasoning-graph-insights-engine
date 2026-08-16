using System.Collections.ObjectModel;
using Backend.Calculation;
using Backend.Insights.Contracts;
using Backend.Models.Domain;

namespace Backend.Insights.Analysis;

public sealed record StrongestPathV1Item(
    int Rank,
    string EndNodeId,
    decimal AccumulatedLogLikelihoodRatio,
    IReadOnlyList<string> NodeIds,
    IReadOnlyList<string> EdgeIds);

public sealed record StrongestPathV1Summary(
    string StartNodeId,
    string Direction,
    long ReachableNodeCount,
    string StrongestEndNodeId,
    decimal StrongestAbsoluteAccumulatedLogLikelihoodRatio);

public sealed record StrongestPathV1Distribution(
    long SupportingPathCount,
    long CounterPathCount,
    long NeutralPathCount,
    decimal MinimumAccumulatedLogLikelihoodRatio,
    decimal MaximumAccumulatedLogLikelihoodRatio);

public sealed class StrongestPathV1Result
{
    internal StrongestPathV1Result(
        string startNodeId,
        PathDirection direction,
        IEnumerable<StrongestPathV1Item> items,
        StrongestPathV1Summary summary,
        StrongestPathV1Distribution distribution,
        string resultDigest,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var completeItems = items.ToArray();

        StartNodeId = startNodeId;
        Direction = direction;
        Items = Array.AsReadOnly(completeItems);
        TopItems = Array.AsReadOnly(
            completeItems.Take(OperationResultEnvelope.MaximumRetainedItems).ToArray());
        TotalResultCardinality = completeItems.LongLength;
        Summary = summary;
        Distribution = distribution;
        OrderedPaths = Array.AsReadOnly(
            completeItems
                .Select(item =>
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    return new OrderedPathProjection(
                        item.NodeIds,
                        item.EdgeIds,
                        item.AccumulatedLogLikelihoodRatio);
                })
                .ToArray());
        ResultDigest = resultDigest;
        cancellationToken.ThrowIfCancellationRequested();
    }

    public string OperationKey => OperationKeys.PathStrongest;

    public string AlgorithmSemanticIdentity => AlgorithmSemanticIdentities.StrongestPathV1;

    public string StartNodeId { get; }

    public PathDirection Direction { get; }

    public IReadOnlyList<StrongestPathV1Item> Items { get; }

    public IReadOnlyList<StrongestPathV1Item> TopItems { get; }

    public long TotalResultCardinality { get; }

    public StrongestPathV1Summary Summary { get; }

    public StrongestPathV1Distribution Distribution { get; }

    public IReadOnlyList<OrderedPathProjection> OrderedPaths { get; }

    public string ResultDigest { get; }
}

/// <summary>
/// Computes the strongest simple path from a requested start node to every
/// reachable node in a validated DAG. The analyzer is stateless and does not
/// mutate the input graph.
/// </summary>
public sealed class StrongestPathV1Analysis
{
    public StrongestPathV1Result Analyze(
        Graph graph,
        string startNodeId,
        PathDirection direction,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(startNodeId);
        ValidateDirection(direction);

        var validatedGraph = ValidatedAnalysisGraph.Create(graph, cancellationToken);
        if (!validatedGraph.NodesById.ContainsKey(startNodeId))
        {
            throw new ArgumentException(
                $"Start node '{startNodeId}' is not present in the graph.",
                nameof(startNodeId));
        }

        var computation = Compute(validatedGraph, startNodeId, direction, cancellationToken);
        var orderedPaths = computation.PathsByEndNodeId.Values.ToList();
        CancellationAwareOrdering.Sort(
            orderedPaths,
            (left, right) => CompareStrongestFirst(left, right, cancellationToken),
            cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        var items = new StrongestPathV1Item[orderedPaths.Count];
        for (var index = 0; index < orderedPaths.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var path = orderedPaths[index];
            var nodeIds = path.GetNodeIds(cancellationToken);
            var edgeIds = path.GetEdgeIds(cancellationToken);
            items[index] = new StrongestPathV1Item(
                index + 1,
                path.EndNodeId,
                CanonicalResultNumber.Normalize(path.AccumulatedLogLikelihoodRatio),
                nodeIds,
                edgeIds);
        }

        cancellationToken.ThrowIfCancellationRequested();
        var summary = new StrongestPathV1Summary(
            startNodeId,
            DirectionToken(direction),
            items.LongLength,
            items[0].EndNodeId,
            CanonicalResultNumber.Normalize(
                Math.Abs(items[0].AccumulatedLogLikelihoodRatio)));
        var distribution = new StrongestPathV1Distribution(
            items.LongCount(item => item.AccumulatedLogLikelihoodRatio > 0m),
            items.LongCount(item => item.AccumulatedLogLikelihoodRatio < 0m),
            items.LongCount(item => item.AccumulatedLogLikelihoodRatio == 0m),
            items.Min(item => item.AccumulatedLogLikelihoodRatio),
            items.Max(item => item.AccumulatedLogLikelihoodRatio));

        cancellationToken.ThrowIfCancellationRequested();
        var resultDigest = CanonicalJson.ComputeSha256Sequence(items, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        return new StrongestPathV1Result(
            startNodeId,
            direction,
            items,
            summary,
            distribution,
            resultDigest,
            cancellationToken);
    }

    internal static StrongestPathComputation Compute(
        ValidatedAnalysisGraph graph,
        string startNodeId,
        PathDirection direction,
        CancellationToken cancellationToken)
    {
        ValidateDirection(direction);

        var traversalsBySource = graph.NodesById.Keys.ToDictionary(
            nodeId => nodeId,
            _ => new List<AnalysisEdgeTraversal>(),
            StringComparer.Ordinal);

        foreach (var edge in graph.Edges)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var sourceNodeId = direction == PathDirection.Up ? edge.From : edge.To;
            var endNodeId = direction == PathDirection.Up ? edge.To : edge.From;
            traversalsBySource[sourceNodeId].Add(
                new AnalysisEdgeTraversal(edge, endNodeId));
        }

        foreach (var traversals in traversalsBySource.Values)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CancellationAwareOrdering.Sort(
                traversals,
                AnalysisEdgeTraversalComparer.Instance.Compare,
                cancellationToken);
        }

        var traversalOrder = direction == PathDirection.Up
            ? graph.NaturalTopologicalOrder
            : graph.NaturalTopologicalOrder.Reverse().ToArray();
        var minimumPaths = new Dictionary<string, AnalysisPathState>(StringComparer.Ordinal);
        var maximumPaths = new Dictionary<string, AnalysisPathState>(StringComparer.Ordinal);
        var startPath = AnalysisPathState.Start(startNodeId);
        minimumPaths[startNodeId] = startPath;
        maximumPaths[startNodeId] = startPath;

        foreach (var currentNodeId in traversalOrder)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!minimumPaths.TryGetValue(currentNodeId, out var minimumPath) ||
                !maximumPaths.TryGetValue(currentNodeId, out var maximumPath))
            {
                continue;
            }

            foreach (var traversal in traversalsBySource[currentNodeId])
            {
                cancellationToken.ThrowIfCancellationRequested();

                var minimumCandidate = minimumPath.Extend(traversal);
                if (!minimumPaths.TryGetValue(traversal.EndNodeId, out var existingMinimum) ||
                    CompareForMinimum(minimumCandidate, existingMinimum, cancellationToken) < 0)
                {
                    minimumPaths[traversal.EndNodeId] = minimumCandidate;
                }

                var maximumCandidate = maximumPath.Extend(traversal);
                if (!maximumPaths.TryGetValue(traversal.EndNodeId, out var existingMaximum) ||
                    CompareForMaximum(maximumCandidate, existingMaximum, cancellationToken) < 0)
                {
                    maximumPaths[traversal.EndNodeId] = maximumCandidate;
                }
            }
        }

        var orderedNodeIds = minimumPaths.Keys.ToArray();
        CancellationAwareOrdering.Sort(
            orderedNodeIds,
            StringComparer.Ordinal.Compare,
            cancellationToken);
        var strongestPaths = new Dictionary<string, AnalysisPathState>(StringComparer.Ordinal);
        foreach (var nodeId in orderedNodeIds)
        {
            cancellationToken.ThrowIfCancellationRequested();
            strongestPaths[nodeId] = SelectStrongest(
                minimumPaths[nodeId],
                maximumPaths[nodeId],
                cancellationToken);
        }

        return new StrongestPathComputation(
            new ReadOnlyDictionary<string, AnalysisPathState>(strongestPaths));
    }

    private static AnalysisPathState SelectStrongest(
        AnalysisPathState minimum,
        AnalysisPathState maximum,
        CancellationToken cancellationToken)
    {
        var magnitudeComparison = Math.Abs(maximum.AccumulatedLogLikelihoodRatio)
            .CompareTo(Math.Abs(minimum.AccumulatedLogLikelihoodRatio));
        if (magnitudeComparison > 0)
        {
            return maximum;
        }

        if (magnitudeComparison < 0)
        {
            return minimum;
        }

        var signedComparison = maximum.AccumulatedLogLikelihoodRatio
            .CompareTo(minimum.AccumulatedLogLikelihoodRatio);
        if (signedComparison > 0)
        {
            return maximum;
        }

        if (signedComparison < 0)
        {
            return minimum;
        }

        return ComparePathSequences(minimum, maximum, cancellationToken) <= 0
            ? minimum
            : maximum;
    }

    private static int CompareStrongestFirst(
        AnalysisPathState left,
        AnalysisPathState right,
        CancellationToken cancellationToken)
    {
        var magnitudeComparison = Math.Abs(right.AccumulatedLogLikelihoodRatio)
            .CompareTo(Math.Abs(left.AccumulatedLogLikelihoodRatio));
        if (magnitudeComparison != 0)
        {
            return magnitudeComparison;
        }

        var signedComparison = right.AccumulatedLogLikelihoodRatio
            .CompareTo(left.AccumulatedLogLikelihoodRatio);
        return signedComparison != 0
            ? signedComparison
            : ComparePathSequences(left, right, cancellationToken);
    }

    private static int CompareForMinimum(
        AnalysisPathState candidate,
        AnalysisPathState current,
        CancellationToken cancellationToken)
    {
        var scoreComparison = candidate.AccumulatedLogLikelihoodRatio
            .CompareTo(current.AccumulatedLogLikelihoodRatio);
        return scoreComparison != 0
            ? scoreComparison
            : ComparePathSequences(candidate, current, cancellationToken);
    }

    private static int CompareForMaximum(
        AnalysisPathState candidate,
        AnalysisPathState current,
        CancellationToken cancellationToken)
    {
        var scoreComparison = current.AccumulatedLogLikelihoodRatio
            .CompareTo(candidate.AccumulatedLogLikelihoodRatio);
        return scoreComparison != 0
            ? scoreComparison
            : ComparePathSequences(candidate, current, cancellationToken);
    }

    private static int ComparePathSequences(
        AnalysisPathState left,
        AnalysisPathState right,
        CancellationToken cancellationToken)
    {
        var nodeComparison = OrdinalSequenceComparer.Compare(
            left.GetNodeIds(cancellationToken),
            right.GetNodeIds(cancellationToken));
        return nodeComparison != 0
            ? nodeComparison
            : OrdinalSequenceComparer.Compare(
                left.GetEdgeIds(cancellationToken),
                right.GetEdgeIds(cancellationToken));
    }

    private static void ValidateDirection(PathDirection direction)
    {
        if (!Enum.IsDefined(direction))
        {
            throw new ArgumentOutOfRangeException(
                nameof(direction),
                direction,
                "Unknown strongest-path direction.");
        }
    }

    internal static string DirectionToken(PathDirection direction) => direction switch
    {
        PathDirection.Up => "up",
        PathDirection.Down => "down",
        _ => throw new ArgumentOutOfRangeException(nameof(direction), direction, null)
    };
}

internal sealed record StrongestPathComputation(
    IReadOnlyDictionary<string, AnalysisPathState> PathsByEndNodeId);

internal sealed class AnalysisPathState
{
    private IReadOnlyList<string>? _nodeIds;
    private IReadOnlyList<string>? _edgeIds;

    private AnalysisPathState(
        string endNodeId,
        decimal accumulatedLogLikelihoodRatio,
        AnalysisPathState? previous,
        string? incomingEdgeId)
    {
        EndNodeId = endNodeId;
        AccumulatedLogLikelihoodRatio = accumulatedLogLikelihoodRatio;
        Previous = previous;
        IncomingEdgeId = incomingEdgeId;
    }

    public string EndNodeId { get; }

    public decimal AccumulatedLogLikelihoodRatio { get; }

    private AnalysisPathState? Previous { get; }

    private string? IncomingEdgeId { get; }

    public static AnalysisPathState Start(string startNodeId) =>
        new(startNodeId, 0m, null, null);

    public AnalysisPathState Extend(AnalysisEdgeTraversal traversal)
    {
        var edgeLogLikelihoodRatio =
            (decimal)Math.Log((double)traversal.Edge.ImportanceToParent);
        return new AnalysisPathState(
            traversal.EndNodeId,
            AccumulatedLogLikelihoodRatio + edgeLogLikelihoodRatio,
            this,
            traversal.Edge.Id);
    }

    public IReadOnlyList<string> GetNodeIds(CancellationToken cancellationToken)
    {
        if (_nodeIds is not null)
        {
            return _nodeIds;
        }

        var reversed = new List<string>();
        for (var current = this; current is not null; current = current.Previous)
        {
            cancellationToken.ThrowIfCancellationRequested();
            reversed.Add(current.EndNodeId);
        }

        reversed.Reverse();
        _nodeIds = Array.AsReadOnly(reversed.ToArray());
        return _nodeIds;
    }

    public IReadOnlyList<string> GetEdgeIds(CancellationToken cancellationToken)
    {
        if (_edgeIds is not null)
        {
            return _edgeIds;
        }

        var reversed = new List<string>();
        for (var current = this; current.Previous is not null; current = current.Previous)
        {
            cancellationToken.ThrowIfCancellationRequested();
            reversed.Add(current.IncomingEdgeId!);
        }

        reversed.Reverse();
        _edgeIds = Array.AsReadOnly(reversed.ToArray());
        return _edgeIds;
    }
}

internal sealed record AnalysisEdgeTraversal(GraphEdge Edge, string EndNodeId);

internal sealed class AnalysisEdgeTraversalComparer : IComparer<AnalysisEdgeTraversal>
{
    public static AnalysisEdgeTraversalComparer Instance { get; } = new();

    public int Compare(AnalysisEdgeTraversal? left, AnalysisEdgeTraversal? right)
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

        var nodeComparison = StringComparer.Ordinal.Compare(left.EndNodeId, right.EndNodeId);
        return nodeComparison != 0
            ? nodeComparison
            : StringComparer.Ordinal.Compare(left.Edge.Id, right.Edge.Id);
    }
}

internal static class OrdinalSequenceComparer
{
    public static int Compare(IReadOnlyList<string> left, IReadOnlyList<string> right)
    {
        var commonLength = Math.Min(left.Count, right.Count);
        for (var index = 0; index < commonLength; index++)
        {
            var comparison = StringComparer.Ordinal.Compare(left[index], right[index]);
            if (comparison != 0)
            {
                return comparison;
            }
        }

        return left.Count.CompareTo(right.Count);
    }
}

internal sealed class ValidatedAnalysisGraph
{
    private ValidatedAnalysisGraph(
        IReadOnlyDictionary<string, GraphNode> nodesById,
        IReadOnlyList<GraphEdge> edges,
        IReadOnlyList<string> naturalTopologicalOrder)
    {
        NodesById = nodesById;
        Edges = edges;
        NaturalTopologicalOrder = naturalTopologicalOrder;
    }

    public IReadOnlyDictionary<string, GraphNode> NodesById { get; }

    public IReadOnlyList<GraphEdge> Edges { get; }

    public IReadOnlyList<string> NaturalTopologicalOrder { get; }

    public static ValidatedAnalysisGraph Create(
        Graph graph,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(graph);
        if (graph.Nodes is null)
        {
            throw new ArgumentException("The graph node collection must not be null.", nameof(graph));
        }

        if (graph.Edges is null)
        {
            throw new ArgumentException("The graph edge collection must not be null.", nameof(graph));
        }

        var nodesById = new Dictionary<string, GraphNode>(StringComparer.Ordinal);
        foreach (var node in graph.Nodes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (node is null)
            {
                throw new ArgumentException("Graph nodes must not contain null entries.", nameof(graph));
            }

            if (string.IsNullOrWhiteSpace(node.Id))
            {
                throw new ArgumentException("Every graph node must have a non-empty ID.", nameof(graph));
            }

            if (!nodesById.TryAdd(node.Id, node))
            {
                throw new ArgumentException(
                    $"Node ID '{node.Id}' occurs more than once.",
                    nameof(graph));
            }
        }

        var edgeIds = new HashSet<string>(StringComparer.Ordinal);
        var edges = new List<GraphEdge>(graph.Edges.Count);
        foreach (var edge in graph.Edges)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (edge is null)
            {
                throw new ArgumentException("Graph edges must not contain null entries.", nameof(graph));
            }

            if (string.IsNullOrWhiteSpace(edge.Id) || !edgeIds.Add(edge.Id))
            {
                throw new ArgumentException(
                    $"Edge ID '{edge.Id}' must be non-empty and unique.",
                    nameof(graph));
            }

            if (!nodesById.ContainsKey(edge.From) || !nodesById.ContainsKey(edge.To))
            {
                throw new ArgumentException(
                    $"Edge '{edge.Id}' must reference existing From and To nodes.",
                    nameof(graph));
            }

            if (edge.ImportanceToParent <= 0m)
            {
                throw new ArgumentException(
                    $"Edge '{edge.Id}' has non-positive likelihood ratio '{edge.ImportanceToParent}'.",
                    nameof(graph));
            }

            edges.Add(edge);
        }

        CancellationAwareOrdering.Sort(
            edges,
            (left, right) => StringComparer.Ordinal.Compare(left.Id, right.Id),
            cancellationToken);
        var outgoingEdges = nodesById.Keys.ToDictionary(
            nodeId => nodeId,
            _ => new List<GraphEdge>(),
            StringComparer.Ordinal);
        var incomingCounts = nodesById.Keys.ToDictionary(
            nodeId => nodeId,
            _ => 0,
            StringComparer.Ordinal);

        foreach (var edge in edges)
        {
            cancellationToken.ThrowIfCancellationRequested();
            outgoingEdges[edge.From].Add(edge);
            incomingCounts[edge.To]++;
        }

        foreach (var outgoing in outgoingEdges.Values)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CancellationAwareOrdering.Sort(
                outgoing,
                (left, right) =>
                {
                    var targetComparison = StringComparer.Ordinal.Compare(left.To, right.To);
                    return targetComparison != 0
                        ? targetComparison
                        : StringComparer.Ordinal.Compare(left.Id, right.Id);
                },
                cancellationToken);
        }

        var ready = new SortedSet<string>(
            incomingCounts
                .Where(entry => entry.Value == 0)
                .Select(entry => entry.Key),
            StringComparer.Ordinal);
        var topologicalOrder = new List<string>(nodesById.Count);
        while (ready.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var nodeId = ready.Min!;
            ready.Remove(nodeId);
            topologicalOrder.Add(nodeId);

            foreach (var edge in outgoingEdges[nodeId])
            {
                cancellationToken.ThrowIfCancellationRequested();
                incomingCounts[edge.To]--;
                if (incomingCounts[edge.To] == 0)
                {
                    ready.Add(edge.To);
                }
            }
        }

        if (topologicalOrder.Count != nodesById.Count)
        {
            throw new ArgumentException(
                "Versioned strongest-path analysis requires a directed acyclic graph.",
                nameof(graph));
        }

        cancellationToken.ThrowIfCancellationRequested();
        return new ValidatedAnalysisGraph(
            new ReadOnlyDictionary<string, GraphNode>(nodesById),
            Array.AsReadOnly(edges.ToArray()),
            Array.AsReadOnly(topologicalOrder.ToArray()));
    }
}
