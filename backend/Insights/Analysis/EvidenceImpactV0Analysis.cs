using Backend.Calculation;
using Backend.Insights.Contracts;
using Backend.Models.Domain;

namespace Backend.Insights.Analysis;

public static class EvidenceImpactV0Partitions
{
    public const string Supporting = "supporting";
    public const string Counter = "counter";
}

public sealed record EvidenceImpactV0Item(
    int Rank,
    string Partition,
    string NodeId,
    string Title,
    string Kind,
    decimal AccumulatedPathLogLikelihoodRatio,
    decimal BaselineProbability,
    decimal CounterfactualProbability,
    decimal RawProbabilityDelta,
    IReadOnlyList<string> NodeIds,
    IReadOnlyList<string> EdgeIds);

internal sealed record EvidenceImpactV0LegacyItem(
    string NodeId,
    decimal AccumulatedPathLogLikelihoodRatio,
    double RawProbabilityDelta);

public sealed record EvidenceImpactV0Summary(
    string TargetNodeId,
    string TargetTitle,
    string TargetKind,
    decimal BaselineLogOdds,
    decimal BaselineProbability,
    long SupportingEvidenceCount,
    long CounterEvidenceCount);

public sealed record EvidenceImpactV0PartitionDistribution(
    long Count,
    decimal? MinimumRawProbabilityDelta,
    decimal? MaximumRawProbabilityDelta,
    decimal? MeanAbsoluteProbabilityDelta);

public sealed record EvidenceImpactV0Distribution(
    EvidenceImpactV0PartitionDistribution Supporting,
    EvidenceImpactV0PartitionDistribution Counter);

public sealed class EvidenceImpactV0Result
{
    internal EvidenceImpactV0Result(
        string targetNodeId,
        IEnumerable<EvidenceImpactV0Item> supportingEvidence,
        IEnumerable<EvidenceImpactV0Item> counterEvidence,
        IEnumerable<EvidenceImpactV0LegacyItem> legacySupportingEvidence,
        IEnumerable<EvidenceImpactV0LegacyItem> legacyCounterEvidence,
        EvidenceImpactV0Summary summary,
        EvidenceImpactV0Distribution distribution,
        string resultDigest,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var supporting = supportingEvidence.ToArray();
        var counter = counterEvidence.ToArray();
        var completeItems = supporting.Concat(counter).ToArray();

        TargetNodeId = targetNodeId;
        SupportingEvidence = Array.AsReadOnly(supporting);
        CounterEvidence = Array.AsReadOnly(counter);
        LegacySupportingEvidence = Array.AsReadOnly(legacySupportingEvidence.ToArray());
        LegacyCounterEvidence = Array.AsReadOnly(legacyCounterEvidence.ToArray());
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
                        item.AccumulatedPathLogLikelihoodRatio);
                })
                .ToArray());
        ResultDigest = resultDigest;
        cancellationToken.ThrowIfCancellationRequested();
    }

    public string OperationKey => OperationKeys.EvidenceImpactRanking;

    public string AlgorithmSemanticIdentity => AlgorithmSemanticIdentities.EvidenceImpactV0;

    public string TargetNodeId { get; }

    public IReadOnlyList<EvidenceImpactV0Item> SupportingEvidence { get; }

    public IReadOnlyList<EvidenceImpactV0Item> CounterEvidence { get; }

    internal IReadOnlyList<EvidenceImpactV0LegacyItem> LegacySupportingEvidence { get; }

    internal IReadOnlyList<EvidenceImpactV0LegacyItem> LegacyCounterEvidence { get; }

    /// <summary>
    /// Complete logical digest order: the supporting partition followed by the
    /// counter partition, with each partition in its frozen deterministic order.
    /// </summary>
    public IReadOnlyList<EvidenceImpactV0Item> Items { get; }

    public IReadOnlyList<EvidenceImpactV0Item> TopItems { get; }

    public long TotalResultCardinality { get; }

    public EvidenceImpactV0Summary Summary { get; }

    public EvidenceImpactV0Distribution Distribution { get; }

    public IReadOnlyList<OrderedPathProjection> OrderedPaths { get; }

    public string ResultDigest { get; }
}

/// <summary>
/// Rich projection of the frozen evidence-impact-v0 calculation. The baseline
/// is recalculated from the target prior and strongest downstream evidence or
/// objection paths; stored posterior odds are deliberately not used.
/// </summary>
public sealed class EvidenceImpactV0Analysis
{
    private const decimal MinimumLogOdds = -100m;
    private const decimal MaximumLogOdds = 100m;

    public EvidenceImpactV0Result Analyze(
        Graph graph,
        string targetNodeId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(targetNodeId);

        var validatedGraph = ValidatedAnalysisGraph.Create(graph, cancellationToken);
        if (!validatedGraph.NodesById.TryGetValue(targetNodeId, out var targetNode))
        {
            throw new ArgumentException(
                $"Target node '{targetNodeId}' is not present in the graph.",
                nameof(targetNodeId));
        }

        var strongestPaths = StrongestPathV1Analysis.Compute(
            validatedGraph,
            targetNodeId,
            PathDirection.Down,
            cancellationToken);

        var orderedNodeIds = validatedGraph.NodesById.Keys.ToArray();
        CancellationAwareOrdering.Sort(
            orderedNodeIds,
            StringComparer.Ordinal.Compare,
            cancellationToken);
        var evidencePaths = new List<AnalysisPathState>();
        foreach (var nodeId in orderedNodeIds)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var node = validatedGraph.NodesById[nodeId];
            if (IsFrozenEvidenceKind(node.Kind) &&
                strongestPaths.PathsByEndNodeId.TryGetValue(node.Id, out var path))
            {
                evidencePaths.Add(path);
            }
        }

        decimal recalculatedBaselineLogOdds = targetNode.PriorOdds;
        foreach (var evidencePath in evidencePaths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            recalculatedBaselineLogOdds += evidencePath.AccumulatedLogLikelihoodRatio;
        }

        recalculatedBaselineLogOdds = Math.Clamp(
            recalculatedBaselineLogOdds,
            MinimumLogOdds,
            MaximumLogOdds);
        var baselineProbability = LogOddsToProbability(recalculatedBaselineLogOdds);

        var supportingCandidates = new List<EvidenceImpactCandidate>();
        var counterCandidates = new List<EvidenceImpactCandidate>();
        foreach (var path in evidencePaths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (path.AccumulatedLogLikelihoodRatio == 0m)
            {
                continue;
            }

            var counterfactualLogOdds =
                recalculatedBaselineLogOdds - path.AccumulatedLogLikelihoodRatio;
            var counterfactualProbability = LogOddsToProbability(counterfactualLogOdds);
            var candidate = new EvidenceImpactCandidate(
                validatedGraph.NodesById[path.EndNodeId],
                path,
                baselineProbability,
                counterfactualProbability,
                baselineProbability - counterfactualProbability);

            if (path.AccumulatedLogLikelihoodRatio > 0m)
            {
                supportingCandidates.Add(candidate);
            }
            else
            {
                counterCandidates.Add(candidate);
            }
        }

        SortPartition(supportingCandidates, cancellationToken);
        SortPartition(counterCandidates, cancellationToken);
        var supportingItems = ShapePartition(
            supportingCandidates,
            EvidenceImpactV0Partitions.Supporting,
            cancellationToken);
        var counterItems = ShapePartition(
            counterCandidates,
            EvidenceImpactV0Partitions.Counter,
            cancellationToken);
        var legacySupportingItems = ShapeLegacyPartition(
            supportingCandidates,
            cancellationToken);
        var legacyCounterItems = ShapeLegacyPartition(
            counterCandidates,
            cancellationToken);
        var completeItems = supportingItems.Concat(counterItems).ToArray();

        cancellationToken.ThrowIfCancellationRequested();
        var normalizedBaselineLogOdds = CanonicalResultNumber.Normalize(
            recalculatedBaselineLogOdds);
        var normalizedBaselineProbability = CanonicalResultNumber.Normalize(
            baselineProbability);
        var summary = new EvidenceImpactV0Summary(
            targetNodeId,
            targetNode.Title ?? string.Empty,
            targetNode.Kind ?? string.Empty,
            normalizedBaselineLogOdds,
            normalizedBaselineProbability,
            supportingItems.LongLength,
            counterItems.LongLength);
        var distribution = new EvidenceImpactV0Distribution(
            DistributionFor(supportingItems),
            DistributionFor(counterItems));

        cancellationToken.ThrowIfCancellationRequested();
        var resultDigest = CanonicalJson.ComputeSha256Sequence(
            completeItems,
            cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        return new EvidenceImpactV0Result(
            targetNodeId,
            supportingItems,
            counterItems,
            legacySupportingItems,
            legacyCounterItems,
            summary,
            distribution,
            resultDigest,
            cancellationToken);
    }

    private static bool IsFrozenEvidenceKind(string? kind) =>
        string.Equals(kind, "evidence", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(kind, "objection", StringComparison.OrdinalIgnoreCase);

    private static void SortPartition(
        List<EvidenceImpactCandidate> candidates,
        CancellationToken cancellationToken)
    {
        CancellationAwareOrdering.Sort(
            candidates,
            (left, right) =>
            {
                var magnitudeComparison = Math.Abs(right.RawProbabilityDelta)
                    .CompareTo(Math.Abs(left.RawProbabilityDelta));
                return magnitudeComparison != 0
                    ? magnitudeComparison
                    : StringComparer.Ordinal.Compare(left.Node.Id, right.Node.Id);
            },
            cancellationToken);
    }

    private static EvidenceImpactV0Item[] ShapePartition(
        IReadOnlyList<EvidenceImpactCandidate> candidates,
        string partition,
        CancellationToken cancellationToken)
    {
        var items = new EvidenceImpactV0Item[candidates.Count];
        for (var index = 0; index < candidates.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var candidate = candidates[index];
            items[index] = new EvidenceImpactV0Item(
                index + 1,
                partition,
                candidate.Node.Id,
                candidate.Node.Title ?? string.Empty,
                candidate.Node.Kind ?? string.Empty,
                CanonicalResultNumber.Normalize(
                    candidate.Path.AccumulatedLogLikelihoodRatio),
                CanonicalResultNumber.Normalize(candidate.BaselineProbability),
                CanonicalResultNumber.Normalize(candidate.CounterfactualProbability),
                CanonicalResultNumber.Normalize(candidate.RawProbabilityDelta),
                candidate.Path.GetNodeIds(cancellationToken),
                candidate.Path.GetEdgeIds(cancellationToken));
        }

        return items;
    }

    private static IReadOnlyList<EvidenceImpactV0LegacyItem> ShapeLegacyPartition(
        IReadOnlyList<EvidenceImpactCandidate> candidates,
        CancellationToken cancellationToken)
    {
        var items = new EvidenceImpactV0LegacyItem[candidates.Count];
        for (var index = 0; index < candidates.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var candidate = candidates[index];
            items[index] = new EvidenceImpactV0LegacyItem(
                candidate.Node.Id,
                candidate.Path.AccumulatedLogLikelihoodRatio,
                candidate.RawProbabilityDelta);
        }

        return Array.AsReadOnly(items);
    }

    private static EvidenceImpactV0PartitionDistribution DistributionFor(
        IReadOnlyList<EvidenceImpactV0Item> items)
    {
        if (items.Count == 0)
        {
            return new EvidenceImpactV0PartitionDistribution(0, null, null, null);
        }

        return new EvidenceImpactV0PartitionDistribution(
            items.Count,
            items.Min(item => item.RawProbabilityDelta),
            items.Max(item => item.RawProbabilityDelta),
            CanonicalResultNumber.Normalize(
                items.Average(item => Math.Abs(item.RawProbabilityDelta))));
    }

    private static double LogOddsToProbability(decimal logOdds)
    {
        var value = (double)logOdds;
        if (value >= 0d)
        {
            var inverseOdds = Math.Exp(-value);
            return 1d / (1d + inverseOdds);
        }

        var odds = Math.Exp(value);
        return odds / (1d + odds);
    }

    private sealed record EvidenceImpactCandidate(
        GraphNode Node,
        AnalysisPathState Path,
        double BaselineProbability,
        double CounterfactualProbability,
        double RawProbabilityDelta);
}
