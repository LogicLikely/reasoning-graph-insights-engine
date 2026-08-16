using System.Text.Json;
using Backend.Calculation;
using Backend.Insights.Contracts;
using Backend.Insights.Measurement;
using Backend.Models.Domain;

namespace Backend.Insights.Analysis;

public sealed record RobustnessV0AnalysisItem(
    string NodeId,
    string Title,
    string Kind,
    int Rank,
    decimal RobustnessScore,
    double OriginalProbability,
    double HypotheticalProbability,
    double AbsoluteProbabilityDelta,
    decimal AccumulatedPathLogLikelihoodRatio,
    decimal? AccumulatedPathLikelihoodRatio,
    IReadOnlyList<string> NodeIds,
    IReadOnlyList<string> EdgeIds,
    string SemanticVersion);

public sealed record RobustnessV0Distribution(
    long Count,
    decimal? MinimumScore,
    decimal? MedianScore,
    decimal? MaximumScore,
    decimal? MeanScore);

public sealed class RobustnessV0AnalysisResult
{
    internal RobustnessV0AnalysisResult(
        IReadOnlyList<RobustnessV0AnalysisItem> ranking,
        IReadOnlyList<RobustnessV0AnalysisItem> top100,
        IReadOnlyList<JsonElement> retainedItems,
        RobustnessV0Distribution distribution,
        IReadOnlyList<OrderedPathProjection> orderedPaths,
        string resultDigest)
    {
        Ranking = ranking;
        LeastRobust = ranking.FirstOrDefault();
        TotalResultCardinality = ranking.Count;
        Top100 = top100;
        RetainedItems = retainedItems;
        Distribution = distribution;
        OrderedPaths = orderedPaths;
        ResultDigest = resultDigest;
    }

    public string OperationKey => OperationKeys.NodeRobustness;

    public string AlgorithmSemanticIdentity => RobustnessV0Contract.SemanticVersion;

    public IReadOnlyList<RobustnessV0AnalysisItem> Ranking { get; }

    public IReadOnlyList<RobustnessV0AnalysisItem> Items => Ranking;

    public RobustnessV0AnalysisItem? LeastRobust { get; }

    public long TotalResultCardinality { get; }

    public IReadOnlyList<RobustnessV0AnalysisItem> Top100 { get; }

    public IReadOnlyList<RobustnessV0AnalysisItem> TopItems => Top100;

    /// <summary>
    /// The normalized JSON projection of <see cref="Top100"/>. These are the
    /// exact item values covered by <see cref="ResultDigest"/> when the full
    /// result cardinality is at most 100.
    /// </summary>
    public IReadOnlyList<JsonElement> RetainedItems { get; }

    public RobustnessV0Distribution Distribution { get; }

    public IReadOnlyList<OrderedPathProjection> OrderedPaths { get; }

    public string ResultDigest { get; }
}

/// <summary>
/// Produces the rich, deterministic result surface for the frozen
/// <c>robustness-v0</c> calculation. The recursive path memo deliberately
/// preserves v0's deep-graph stack risk; changing that execution property is
/// gated by the later deep-chain checkpoint.
/// </summary>
public sealed class RobustnessV0Analyzer
{
    public RobustnessV0AnalysisResult Analyze(
        Graph graph,
        CancellationToken cancellationToken = default)
    {
        return Analyze(graph, null, cancellationToken);
    }

    internal RobustnessV0AnalysisResult Analyze(
        Graph graph,
        IInsightPhaseTimingCollector? phaseTimings,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(graph);
        cancellationToken.ThrowIfCancellationRequested();

        using (phaseTimings?.Measure(
                   InsightMeasurementLayers.BackendServiceApi,
                   InsightMeasurementPhases.Validation))
        {
            var validation = RobustnessV0Contract.ValidateGraph(graph, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (!validation.IsValid)
            {
                throw new ArgumentException(
                    $"Graph violates the robustness-v0 input contract: {string.Join("; ", validation.Issues.Select(issue => issue.Message))}",
                    nameof(graph));
            }
        }

        GraphCalculationContext context;
        using (phaseTimings?.Measure(
                   InsightMeasurementLayers.BackendServiceApi,
                   InsightMeasurementPhases.CalculationContextConstruction))
        {
            context = GraphCalculationContext.From(
                graph.Nodes,
                graph.Edges,
                cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
        }

        List<ComputedNode> computedNodes;
        using (phaseTimings?.Measure(
                   InsightMeasurementLayers.BackendServiceApi,
                   InsightMeasurementPhases.Algorithm))
        {
            var domainNodesById = graph.Nodes.ToDictionary(
                node => node.Id,
                StringComparer.Ordinal);
            var pathByNodeId = new Dictionary<string, MaximumPathState>(StringComparer.Ordinal);
            var nodesBeingCalculated = new HashSet<string>(StringComparer.Ordinal);
            computedNodes = new List<ComputedNode>(context.NodesById.Count);

            using (phaseTimings?.Measure(
                       InsightMeasurementLayers.BackendServiceApi,
                       InsightMeasurementPhases.AlgorithmSubphase("maximum-path-evaluation")))
            {
                foreach (var nodeId in context.NodesById.Keys)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var path = GetMaximumLeafPath(
                        context,
                        nodeId,
                        pathByNodeId,
                        nodesBeingCalculated,
                        cancellationToken);
                    var node = domainNodesById[nodeId];
                    var vector = RobustnessV0Contract.Evaluate(
                        node,
                        path.AccumulatedPathLogLikelihoodRatio);
                    computedNodes.Add(new ComputedNode(node, path, vector));
                }
            }
        }

        RobustnessV0AnalysisItem[] ranking;
        using (phaseTimings?.Measure(
                   InsightMeasurementLayers.BackendServiceApi,
                   InsightMeasurementPhases.Ranking))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var orderedNodes = computedNodes.ToArray();
            CancellationAwareOrdering.Sort(
                orderedNodes,
                (left, right) =>
                {
                    var scoreComparison = left.Vector.RobustnessScore.CompareTo(
                        right.Vector.RobustnessScore);
                    return scoreComparison != 0
                        ? scoreComparison
                        : StringComparer.Ordinal.Compare(left.Node.Id, right.Node.Id);
                },
                cancellationToken);

            ranking = new RobustnessV0AnalysisItem[orderedNodes.Length];
            for (var index = 0; index < orderedNodes.Length; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var computed = orderedNodes[index];
                ranking[index] = new RobustnessV0AnalysisItem(
                    computed.Node.Id,
                    computed.Node.Title,
                    computed.Node.Kind,
                    index + 1,
                    computed.Vector.RobustnessScore,
                    computed.Vector.OriginalProbability,
                    computed.Vector.HypotheticalProbability,
                    computed.Vector.AbsoluteProbabilityDelta,
                    computed.Vector.AccumulatedPathLogLikelihoodRatio,
                    computed.Vector.AccumulatedPathLikelihoodRatio,
                    computed.Path.NodeIds,
                    computed.Path.EdgeIds,
                    RobustnessV0Contract.SemanticVersion);
            }
        }

        using (phaseTimings?.Measure(
                   InsightMeasurementLayers.BackendServiceApi,
                   InsightMeasurementPhases.ResultShaping))
        {
            var frozenRanking = Array.AsReadOnly(ranking);
            var top100 = Array.AsReadOnly(
                ranking.Take(OperationResultEnvelope.MaximumRetainedItems).ToArray());
            var distribution = CreateDistribution(ranking, cancellationToken);
            var orderedPaths = CreateOrderedPaths(ranking, cancellationToken);
            var normalizedItems = CreateNormalizedItems(ranking, cancellationToken);
            var retainedItems = Array.AsReadOnly(
                normalizedItems.Take(OperationResultEnvelope.MaximumRetainedItems).ToArray());
            string resultDigest;
            using (phaseTimings?.Measure(
                       InsightMeasurementLayers.BackendServiceApi,
                       InsightMeasurementPhases.DigestGeneration))
            {
                resultDigest = ComputeCompleteResultDigest(normalizedItems, cancellationToken);
            }

            return new RobustnessV0AnalysisResult(
                frozenRanking,
                top100,
                retainedItems,
                distribution,
                orderedPaths,
                resultDigest);
        }
    }

    private static MaximumPathState GetMaximumLeafPath(
        GraphCalculationContext context,
        string nodeId,
        Dictionary<string, MaximumPathState> memo,
        HashSet<string> nodesBeingCalculated,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (memo.TryGetValue(nodeId, out var cachedPath))
        {
            return cachedPath;
        }

        if (!nodesBeingCalculated.Add(nodeId))
        {
            throw new InvalidOperationException(
                $"Cycle detected while calculating robustness-v0 at node '{nodeId}'.");
        }

        try
        {
            if (!context.ChildEdgesByParentId.TryGetValue(nodeId, out var childEdges) ||
                childEdges.Count == 0)
            {
                var leafPath = new MaximumPathState(
                    0m,
                    Array.AsReadOnly([nodeId]),
                    Array.Empty<string>());
                memo[nodeId] = leafPath;
                return leafPath;
            }

            MaximumPathState? bestPath = null;
            foreach (var edge in childEdges)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var childPath = GetMaximumLeafPath(
                    context,
                    edge.FromNodeId,
                    memo,
                    nodesBeingCalculated,
                    cancellationToken);
                var candidate = AppendParent(
                    childPath,
                    edge,
                    nodeId,
                    cancellationToken);

                if (bestPath is null || ComparePaths(candidate, bestPath, cancellationToken) < 0)
                {
                    bestPath = candidate;
                }
            }

            var selectedPath = bestPath ?? throw new InvalidOperationException(
                $"Node '{nodeId}' has a structural child collection with no paths.");
            memo[nodeId] = selectedPath;
            return selectedPath;
        }
        finally
        {
            nodesBeingCalculated.Remove(nodeId);
        }
    }

    private static MaximumPathState AppendParent(
        MaximumPathState childPath,
        GraphEdgeCalcState edge,
        string parentNodeId,
        CancellationToken cancellationToken)
    {
        if (edge.ImportanceToParent <= 0m)
        {
            throw new InvalidOperationException(
                $"Edge '{edge.Id}' has invalid likelihood ratio '{edge.ImportanceToParent}'. Likelihood ratios must be greater than zero.");
        }

        var nodeIds = new string[childPath.NodeIds.Count + 1];
        for (var index = 0; index < childPath.NodeIds.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            nodeIds[index] = childPath.NodeIds[index];
        }

        nodeIds[^1] = parentNodeId;

        var edgeIds = new string[childPath.EdgeIds.Count + 1];
        for (var index = 0; index < childPath.EdgeIds.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            edgeIds[index] = childPath.EdgeIds[index];
        }

        edgeIds[^1] = edge.Id;

        return new MaximumPathState(
            (decimal)Math.Log((double)edge.ImportanceToParent) +
            childPath.AccumulatedPathLogLikelihoodRatio,
            Array.AsReadOnly(nodeIds),
            Array.AsReadOnly(edgeIds));
    }

    private static int ComparePaths(
        MaximumPathState left,
        MaximumPathState right,
        CancellationToken cancellationToken)
    {
        var scoreComparison = right.AccumulatedPathLogLikelihoodRatio.CompareTo(
            left.AccumulatedPathLogLikelihoodRatio);
        if (scoreComparison != 0)
        {
            return scoreComparison;
        }

        var nodeComparison = CompareOrdinalSequences(
            left.NodeIds,
            right.NodeIds,
            cancellationToken);
        return nodeComparison != 0
            ? nodeComparison
            : CompareOrdinalSequences(left.EdgeIds, right.EdgeIds, cancellationToken);
    }

    private static int CompareOrdinalSequences(
        IReadOnlyList<string> left,
        IReadOnlyList<string> right,
        CancellationToken cancellationToken)
    {
        var sharedLength = Math.Min(left.Count, right.Count);
        for (var index = 0; index < sharedLength; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var comparison = StringComparer.Ordinal.Compare(left[index], right[index]);
            if (comparison != 0)
            {
                return comparison;
            }
        }

        return left.Count.CompareTo(right.Count);
    }

    private static RobustnessV0Distribution CreateDistribution(
        IReadOnlyList<RobustnessV0AnalysisItem> ranking,
        CancellationToken cancellationToken)
    {
        if (ranking.Count == 0)
        {
            return new RobustnessV0Distribution(0, null, null, null, null);
        }

        decimal scoreSum = 0m;
        foreach (var item in ranking)
        {
            cancellationToken.ThrowIfCancellationRequested();
            scoreSum += item.RobustnessScore;
        }

        var middle = ranking.Count / 2;
        var median = ranking.Count % 2 == 1
            ? ranking[middle].RobustnessScore
            : (ranking[middle - 1].RobustnessScore + ranking[middle].RobustnessScore) / 2m;

        return new RobustnessV0Distribution(
            ranking.Count,
            CanonicalResultNumber.Normalize(ranking[0].RobustnessScore),
            CanonicalResultNumber.Normalize(median),
            CanonicalResultNumber.Normalize(ranking[^1].RobustnessScore),
            CanonicalResultNumber.Normalize(scoreSum / ranking.Count));
    }

    private static IReadOnlyList<OrderedPathProjection> CreateOrderedPaths(
        IReadOnlyList<RobustnessV0AnalysisItem> ranking,
        CancellationToken cancellationToken)
    {
        var paths = new OrderedPathProjection[ranking.Count];
        for (var index = 0; index < ranking.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var item = ranking[index];
            paths[index] = new OrderedPathProjection(
                item.NodeIds,
                item.EdgeIds,
                item.AccumulatedPathLogLikelihoodRatio);
        }

        return Array.AsReadOnly(paths);
    }

    private static IReadOnlyList<JsonElement> CreateNormalizedItems(
        IReadOnlyList<RobustnessV0AnalysisItem> ranking,
        CancellationToken cancellationToken)
    {
        var serializerOptions = CanonicalJson.CreateSerializerOptions();
        var normalizedItems = new JsonElement[ranking.Count];
        for (var index = 0; index < ranking.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var item = ranking[index];
            var canonicalItem = new CanonicalRobustnessV0Item(
                item.NodeId,
                item.Title,
                item.Kind,
                item.Rank,
                CanonicalResultNumber.Normalize(item.RobustnessScore),
                CanonicalResultNumber.Normalize(item.OriginalProbability),
                CanonicalResultNumber.Normalize(item.HypotheticalProbability),
                CanonicalResultNumber.Normalize(item.AbsoluteProbabilityDelta),
                CanonicalResultNumber.Normalize(item.AccumulatedPathLogLikelihoodRatio),
                item.AccumulatedPathLikelihoodRatio.HasValue
                    ? CanonicalResultNumber.Normalize(item.AccumulatedPathLikelihoodRatio.Value)
                    : null,
                item.NodeIds,
                item.EdgeIds,
                item.SemanticVersion);
            normalizedItems[index] = JsonSerializer.SerializeToElement(
                canonicalItem,
                serializerOptions);
        }

        return Array.AsReadOnly(normalizedItems);
    }

    private static string ComputeCompleteResultDigest(
        IReadOnlyList<JsonElement> normalizedItems,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var digest = CanonicalJson.ComputeSha256Sequence(
            normalizedItems,
            cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        return digest;
    }

    private sealed record MaximumPathState(
        decimal AccumulatedPathLogLikelihoodRatio,
        IReadOnlyList<string> NodeIds,
        IReadOnlyList<string> EdgeIds);

    private sealed record ComputedNode(
        GraphNode Node,
        MaximumPathState Path,
        RobustnessV0Vector Vector);

    private sealed record CanonicalRobustnessV0Item(
        string NodeId,
        string Title,
        string Kind,
        int Rank,
        decimal RobustnessScore,
        decimal OriginalProbability,
        decimal HypotheticalProbability,
        decimal AbsoluteProbabilityDelta,
        decimal AccumulatedPathLogLikelihoodRatio,
        decimal? AccumulatedPathLikelihoodRatio,
        IReadOnlyList<string> NodeIds,
        IReadOnlyList<string> EdgeIds,
        string SemanticVersion);

}
