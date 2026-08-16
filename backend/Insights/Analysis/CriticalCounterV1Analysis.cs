using Backend.Calculation;
using Backend.Insights.Contracts;
using Backend.Models.Domain;

namespace Backend.Insights.Analysis;

/// <summary>
/// Immutable, serialization-friendly input for the critical-counter-v1 core.
/// Auto intentionally has no implicit cutoff: callers must supply a
/// non-negative value whose provenance can be recorded with the run.
/// </summary>
public sealed record CriticalCounterV1AnalysisRequest(
    Graph Graph,
    string TargetNodeId,
    string RequestedStrategy,
    decimal ThresholdLogOdds,
    int? AutoCandidateCutoff);

public sealed record CriticalCounterResponsiblePath(
    IReadOnlyList<string> NodeIds,
    IReadOnlyList<string> EdgeIds,
    decimal AccumulatedLogLikelihoodRatio);

public sealed record CriticalCounterSelectedDetail(
    string NodeId,
    string Kind,
    bool RecognizedByLikelihoodRecalculationV0,
    CriticalCounterResponsiblePath? ResponsiblePath);

/// <summary>
/// The single canonical logical item for a critical-counter result. Floating
/// values in this item are projected through <see cref="CanonicalResultNumber"/>
/// before the item digest is calculated.
/// </summary>
public sealed record CriticalCounterV1LogicalSolutionItem(
    string OperationKey,
    string AlgorithmSemanticIdentity,
    string TargetNodeId,
    int CandidateCount,
    decimal BaselineLogOdds,
    decimal BaselineProbability,
    decimal ResultingLogOdds,
    decimal ResultingProbability,
    decimal ThresholdLogOdds,
    decimal ThresholdProbability,
    decimal BelowThresholdMargin,
    bool ThresholdAttained,
    IReadOnlyList<string> SelectedNodeIds,
    IReadOnlyList<CriticalCounterSelectedDetail> SelectedCounters,
    bool SearchExhausted,
    bool ProvedUnattainable,
    bool OptimalCardinalityProven);

public sealed class CriticalCounterV1AnalysisResult
{
    internal CriticalCounterV1AnalysisResult(
        string requestedStrategy,
        string usedStrategy,
        int candidateCount,
        int? autoCandidateCutoff,
        string strategySelectionReason,
        decimal baselineLogOdds,
        decimal resultingLogOdds,
        decimal thresholdLogOdds,
        IReadOnlyList<CriticalCounterSelectedDetail> selectedCounters,
        long evaluationCount,
        bool searchExhausted,
        bool provedUnattainable,
        bool optimalCardinalityProven,
        CriticalCounterV1LogicalSolutionItem topItem,
        string resultDigest)
    {
        RequestedStrategy = requestedStrategy;
        UsedStrategy = usedStrategy;
        CandidateCount = candidateCount;
        AutoCandidateCutoff = autoCandidateCutoff;
        StrategySelectionReason = strategySelectionReason;
        BaselineLogOdds = baselineLogOdds;
        BaselineProbability = CriticalCounterV1Analyzer.LogOddsToCanonicalProbability(baselineLogOdds);
        ResultingLogOdds = resultingLogOdds;
        ResultingProbability = CriticalCounterV1Analyzer.LogOddsToCanonicalProbability(resultingLogOdds);
        ThresholdLogOdds = thresholdLogOdds;
        ThresholdProbability = CriticalCounterV1Analyzer.LogOddsToCanonicalProbability(thresholdLogOdds);
        BelowThresholdMargin = thresholdLogOdds - resultingLogOdds;
        ThresholdAttained = CriticalCounterV1Contract.IsThresholdAttained(
            resultingLogOdds,
            thresholdLogOdds);
        SelectedCounters = Array.AsReadOnly(selectedCounters.ToArray());
        SelectedNodeIds = Array.AsReadOnly(
            selectedCounters.Select(counter => counter.NodeId).ToArray());
        ResponsibleSelectedPaths = Array.AsReadOnly(
            selectedCounters
                .Where(counter => counter.ResponsiblePath is not null)
                .Select(counter => counter.ResponsiblePath!)
                .ToArray());
        EvaluationCount = evaluationCount;
        SearchExhausted = searchExhausted;
        ProvedUnattainable = provedUnattainable;
        OptimalCardinalityProven = optimalCardinalityProven;
        DeterministicTopItem = topItem;
        Items = Array.AsReadOnly(new[] { topItem });
        ResultDigest = resultDigest;
    }

    public string OperationKey => OperationKeys.CounterCriticalSet;

    public string AlgorithmSemanticIdentity => AlgorithmSemanticIdentities.CriticalCounterV1;

    public string RequestedStrategy { get; }

    public string UsedStrategy { get; }

    public int CandidateCount { get; }

    public int? AutoCandidateCutoff { get; }

    public string StrategySelectionReason { get; }

    public decimal BaselineLogOdds { get; }

    public decimal BaselineProbability { get; }

    public decimal ResultingLogOdds { get; }

    public decimal ResultingProbability { get; }

    public decimal ThresholdLogOdds { get; }

    public decimal ThresholdProbability { get; }

    public decimal BelowThresholdMargin { get; }

    public bool ThresholdAttained { get; }

    public IReadOnlyList<string> SelectedNodeIds { get; }

    public IReadOnlyList<CriticalCounterSelectedDetail> SelectedCounters { get; }

    public IReadOnlyList<CriticalCounterResponsiblePath> ResponsibleSelectedPaths { get; }

    public long EvaluationCount { get; }

    public bool SearchExhausted { get; }

    public bool ProvedUnattainable { get; }

    public bool OptimalCardinalityProven { get; }

    // A critical-set operation returns one logical solution, even when that
    // solution selects the empty set.
    public long TotalResultCardinality => 1;

    public CriticalCounterV1LogicalSolutionItem DeterministicTopItem { get; }

    public IReadOnlyList<CriticalCounterV1LogicalSolutionItem> Items { get; }

    public string ResultDigest { get; }
}

public sealed record CriticalCounterV1QualityComparison(
    bool ExactThresholdAttained,
    bool GreedyThresholdAttained,
    int ExactSelectedCardinality,
    int GreedySelectedCardinality,
    int? CardinalityGapFromOptimal,
    int SelectedSetOverlapCount,
    int SelectedSetUnionCount,
    decimal SelectedSetJaccardSimilarity,
    decimal ExactBelowThresholdMargin,
    decimal GreedyBelowThresholdMargin,
    long ExactEvaluationCount,
    long GreedyEvaluationCount,
    string ExactResultDigest,
    string GreedyResultDigest);

/// <summary>
/// Stateless implementation of critical-counter-v1. It has no repository,
/// HTTP, clock, or process dependency, so the same core can execute inside an
/// isolated worker. Every evaluated subset is rebuilt through
/// <see cref="CriticalCounterV1Contract.BuildActiveProjection"/> and receives
/// a fresh <see cref="GraphCalculationContext"/>.
///
/// Candidate kind <c>counter</c> remains a frozen eligibility alias, but the
/// current likelihood-recalculate-v0 implementation recognizes only
/// <c>evidence</c> and <c>objection</c> as evidence contributors. This analyzer
/// deliberately does not normalize <c>counter</c> to <c>objection</c>. Such a
/// node may still be selected as structural context for another candidate,
/// but it has no responsible contribution path of its own.
/// </summary>
public sealed class CriticalCounterV1Analyzer
{
    public const string RequestedExactReason = "requested-exact";
    public const string RequestedGreedyReason = "requested-greedy";
    public const string AutoExactReason = "candidate-count-at-or-below-cutoff";
    public const string AutoGreedyReason = "candidate-count-above-cutoff";

    public CriticalCounterV1AnalysisResult Analyze(
        CriticalCounterV1AnalysisRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        ValidateStrategyAndCutoff(request.RequestedStrategy, request.AutoCandidateCutoff);
        var candidateNodeIds = CriticalCounterV1Contract.GetEligibleCandidateNodeIds(
            request.Graph,
            request.TargetNodeId,
            cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        var (usedStrategy, selectionReason) = ResolveStrategy(
            request.RequestedStrategy,
            candidateNodeIds.Count,
            request.AutoCandidateCutoff);

        return usedStrategy switch
        {
            OperationStrategyNames.Exact => AnalyzeExact(
                request,
                candidateNodeIds,
                selectionReason,
                cancellationToken),
            OperationStrategyNames.Greedy => AnalyzeGreedy(
                request,
                candidateNodeIds,
                selectionReason,
                cancellationToken),
            _ => throw new InvalidOperationException(
                $"Resolved unsupported critical-counter strategy '{usedStrategy}'.")
        };
    }

    public CriticalCounterV1QualityComparison CompareExactAndGreedy(
        Graph graph,
        string targetNodeId,
        decimal thresholdLogOdds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(graph);
        cancellationToken.ThrowIfCancellationRequested();

        var exact = Analyze(
            new CriticalCounterV1AnalysisRequest(
                graph,
                targetNodeId,
                OperationStrategyNames.Exact,
                thresholdLogOdds,
                null),
            cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        var greedy = Analyze(
            new CriticalCounterV1AnalysisRequest(
                graph,
                targetNodeId,
                OperationStrategyNames.Greedy,
                thresholdLogOdds,
                null),
            cancellationToken);

        var exactSet = exact.SelectedNodeIds.ToHashSet(StringComparer.Ordinal);
        var greedySet = greedy.SelectedNodeIds.ToHashSet(StringComparer.Ordinal);
        var overlapCount = exactSet.Count(nodeId => greedySet.Contains(nodeId));
        var unionCount = exactSet.Union(greedySet, StringComparer.Ordinal).Count();
        var jaccard = unionCount == 0
            ? 1m
            : CanonicalResultNumber.Normalize((decimal)overlapCount / unionCount);
        int? cardinalityGap = exact.ThresholdAttained &&
                              exact.OptimalCardinalityProven &&
                              greedy.ThresholdAttained
            ? greedy.SelectedNodeIds.Count - exact.SelectedNodeIds.Count
            : null;

        return new CriticalCounterV1QualityComparison(
            exact.ThresholdAttained,
            greedy.ThresholdAttained,
            exact.SelectedNodeIds.Count,
            greedy.SelectedNodeIds.Count,
            cardinalityGap,
            overlapCount,
            unionCount,
            jaccard,
            CanonicalResultNumber.Normalize(exact.BelowThresholdMargin),
            CanonicalResultNumber.Normalize(greedy.BelowThresholdMargin),
            exact.EvaluationCount,
            greedy.EvaluationCount,
            exact.ResultDigest,
            greedy.ResultDigest);
    }

    internal static decimal LogOddsToCanonicalProbability(decimal logOdds)
    {
        var value = (double)logOdds;
        double probability;
        if (value >= 0d)
        {
            var inverseOdds = Math.Exp(-value);
            probability = 1d / (1d + inverseOdds);
        }
        else
        {
            var odds = Math.Exp(value);
            probability = odds / (1d + odds);
        }

        return CanonicalResultNumber.Normalize(probability);
    }

    private static CriticalCounterV1AnalysisResult AnalyzeExact(
        CriticalCounterV1AnalysisRequest request,
        IReadOnlyList<string> candidateNodeIds,
        string selectionReason,
        CancellationToken cancellationToken)
    {
        long evaluationCount = 0;
        var baseline = EvaluateSubset(
            request.Graph,
            request.TargetNodeId,
            [],
            candidateNodeIds,
            request.ThresholdLogOdds,
            cancellationToken);
        evaluationCount++;

        if (baseline.ThresholdAttained)
        {
            return CreateResult(
                request,
                OperationStrategyNames.Exact,
                selectionReason,
                candidateNodeIds,
                baseline,
                baseline,
                evaluationCount,
                searchExhausted: candidateNodeIds.Count == 0,
                provedUnattainable: false,
                optimalCardinalityProven: true,
                cancellationToken);
        }

        for (var cardinality = 1; cardinality <= candidateNodeIds.Count; cardinality++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CriticalCounterSelectionOutcome? bestAtCardinality = null;

            foreach (var selectedNodeIds in EnumerateOrdinalCombinations(candidateNodeIds, cardinality))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var outcome = EvaluateSubset(
                    request.Graph,
                    request.TargetNodeId,
                    selectedNodeIds,
                    candidateNodeIds,
                    request.ThresholdLogOdds,
                    cancellationToken);
                evaluationCount++;

                if (!outcome.ThresholdAttained)
                {
                    continue;
                }

                if (bestAtCardinality is null ||
                    CriticalCounterSelectionOutcomeComparer.Instance.Compare(
                        outcome,
                        bestAtCardinality) < 0)
                {
                    bestAtCardinality = outcome;
                }
            }

            // A complete cardinality is evaluated before margin and ordinal-ID
            // tie-breaking choose its winner.
            if (bestAtCardinality is not null)
            {
                return CreateResult(
                    request,
                    OperationStrategyNames.Exact,
                    selectionReason,
                    candidateNodeIds,
                    baseline,
                    bestAtCardinality,
                    evaluationCount,
                    searchExhausted: cardinality == candidateNodeIds.Count,
                    provedUnattainable: false,
                    optimalCardinalityProven: true,
                    cancellationToken);
            }
        }

        // The frozen non-attaining objective prefers the empty baseline by
        // cardinality. Exhaustion proves that no attaining set exists, not that
        // the returned non-attaining set is an optimal critical set.
        return CreateResult(
            request,
            OperationStrategyNames.Exact,
            selectionReason,
            candidateNodeIds,
            baseline,
            baseline,
            evaluationCount,
            searchExhausted: true,
            provedUnattainable: true,
            optimalCardinalityProven: false,
            cancellationToken);
    }

    private static CriticalCounterV1AnalysisResult AnalyzeGreedy(
        CriticalCounterV1AnalysisRequest request,
        IReadOnlyList<string> candidateNodeIds,
        string selectionReason,
        CancellationToken cancellationToken)
    {
        long evaluationCount = 0;
        var baseline = EvaluateSubset(
            request.Graph,
            request.TargetNodeId,
            [],
            candidateNodeIds,
            request.ThresholdLogOdds,
            cancellationToken);
        evaluationCount++;

        var current = baseline;
        var selectedNodeIds = new List<string>();
        var remainingNodeIds = new SortedSet<string>(candidateNodeIds, StringComparer.Ordinal);

        while (!current.ThresholdAttained && remainingNodeIds.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string? bestCandidateNodeId = null;
            CriticalCounterSelectionOutcome? bestCandidateOutcome = null;

            foreach (var candidateNodeId in remainingNodeIds)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var trialSelection = selectedNodeIds.Append(candidateNodeId).ToArray();
                CancellationAwareOrdering.Sort(
                    trialSelection,
                    StringComparer.Ordinal.Compare,
                    cancellationToken);
                var trialOutcome = EvaluateSubset(
                    request.Graph,
                    request.TargetNodeId,
                    trialSelection,
                    candidateNodeIds,
                    request.ThresholdLogOdds,
                    cancellationToken);
                evaluationCount++;

                if (bestCandidateOutcome is null ||
                    trialOutcome.ResultingLogOdds < bestCandidateOutcome.ResultingLogOdds ||
                    (trialOutcome.ResultingLogOdds == bestCandidateOutcome.ResultingLogOdds &&
                     StringComparer.Ordinal.Compare(candidateNodeId, bestCandidateNodeId) < 0))
                {
                    bestCandidateNodeId = candidateNodeId;
                    bestCandidateOutcome = trialOutcome;
                }
            }

            if (bestCandidateOutcome is null ||
                bestCandidateOutcome.ResultingLogOdds >= current.ResultingLogOdds)
            {
                break;
            }

            selectedNodeIds.Add(bestCandidateNodeId!);
            CancellationAwareOrdering.Sort(
                selectedNodeIds,
                StringComparer.Ordinal.Compare,
                cancellationToken);
            remainingNodeIds.Remove(bestCandidateNodeId!);
            current = bestCandidateOutcome;
        }

        return CreateResult(
            request,
            OperationStrategyNames.Greedy,
            selectionReason,
            candidateNodeIds,
            baseline,
            current,
            evaluationCount,
            searchExhausted: false,
            provedUnattainable: false,
            optimalCardinalityProven: false,
            cancellationToken);
    }

    private static CriticalCounterSelectionOutcome EvaluateSubset(
        Graph immutableInput,
        string targetNodeId,
        IReadOnlyList<string> selectedNodeIds,
        IReadOnlyList<string> eligibleCandidateNodeIds,
        decimal thresholdLogOdds,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var projection = CriticalCounterV1Contract.BuildActiveProjectionFromEligibleCandidates(
            immutableInput,
            targetNodeId,
            selectedNodeIds,
            eligibleCandidateNodeIds,
            cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        var context = GraphCalculationContext.From(
            projection.Graph.Nodes,
            projection.Graph.Edges,
            cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        // Recalculating the target itself works for the empty baseline and for
        // selections whose original path becomes disconnected when another
        // candidate is excluded. The fresh context ensures no subset mutation
        // can leak into another evaluation.
        var recalculated = new GraphLikelihoodCalculator().RecalculateNodesAndAncestors(
            context,
            [targetNodeId],
            cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        if (!recalculated.TryGetValue(targetNodeId, out var resultingLogOdds))
        {
            throw new InvalidOperationException(
                $"Likelihood recalculation did not return target node '{targetNodeId}'.");
        }

        return CriticalCounterSelectionOutcome.Create(
            selectedNodeIds,
            resultingLogOdds,
            thresholdLogOdds);
    }

    private static CriticalCounterV1AnalysisResult CreateResult(
        CriticalCounterV1AnalysisRequest request,
        string usedStrategy,
        string selectionReason,
        IReadOnlyList<string> candidateNodeIds,
        CriticalCounterSelectionOutcome baseline,
        CriticalCounterSelectionOutcome selectedOutcome,
        long evaluationCount,
        bool searchExhausted,
        bool provedUnattainable,
        bool optimalCardinalityProven,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var selectedCounters = BuildSelectedDetails(
            request.Graph,
            request.TargetNodeId,
            selectedOutcome.SelectedNodeIds,
            candidateNodeIds,
            cancellationToken);

        var frozenSelectedIds = Array.AsReadOnly(
            selectedCounters.Select(counter => counter.NodeId).ToArray());
        var frozenSelectedCounters = Array.AsReadOnly(selectedCounters.ToArray());
        var topItem = new CriticalCounterV1LogicalSolutionItem(
            OperationKeys.CounterCriticalSet,
            AlgorithmSemanticIdentities.CriticalCounterV1,
            request.TargetNodeId,
            candidateNodeIds.Count,
            CanonicalResultNumber.Normalize(baseline.ResultingLogOdds),
            LogOddsToCanonicalProbability(baseline.ResultingLogOdds),
            CanonicalResultNumber.Normalize(selectedOutcome.ResultingLogOdds),
            LogOddsToCanonicalProbability(selectedOutcome.ResultingLogOdds),
            CanonicalResultNumber.Normalize(request.ThresholdLogOdds),
            LogOddsToCanonicalProbability(request.ThresholdLogOdds),
            CanonicalResultNumber.Normalize(selectedOutcome.BelowThresholdMargin),
            selectedOutcome.ThresholdAttained,
            frozenSelectedIds,
            frozenSelectedCounters,
            searchExhausted,
            provedUnattainable,
            optimalCardinalityProven);
        var items = new[] { topItem };
        var resultDigest = CanonicalJson.ComputeSha256Sequence(items, cancellationToken);

        return new CriticalCounterV1AnalysisResult(
            request.RequestedStrategy,
            usedStrategy,
            candidateNodeIds.Count,
            request.AutoCandidateCutoff,
            selectionReason,
            baseline.ResultingLogOdds,
            selectedOutcome.ResultingLogOdds,
            request.ThresholdLogOdds,
            frozenSelectedCounters,
            evaluationCount,
            searchExhausted,
            provedUnattainable,
            optimalCardinalityProven,
            topItem,
            resultDigest);
    }

    private static IReadOnlyList<CriticalCounterSelectedDetail> BuildSelectedDetails(
        Graph immutableInput,
        string targetNodeId,
        IReadOnlyList<string> selectedNodeIds,
        IReadOnlyList<string> eligibleCandidateNodeIds,
        CancellationToken cancellationToken)
    {
        if (selectedNodeIds.Count == 0)
        {
            return Array.Empty<CriticalCounterSelectedDetail>();
        }

        cancellationToken.ThrowIfCancellationRequested();
        var projection = CriticalCounterV1Contract.BuildActiveProjectionFromEligibleCandidates(
            immutableInput,
            targetNodeId,
            selectedNodeIds,
            eligibleCandidateNodeIds,
            cancellationToken);
        var nodesById = projection.Graph.Nodes.ToDictionary(node => node.Id, StringComparer.Ordinal);
        var selectedDetails = new List<CriticalCounterSelectedDetail>(selectedNodeIds.Count);

        foreach (var nodeId in selectedNodeIds)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var node = nodesById[nodeId];
            var recognized = IsLikelihoodEvidenceKindV0(node.Kind);
            var responsiblePath = recognized
                ? FindResponsiblePath(
                    projection.Graph,
                    nodeId,
                    targetNodeId,
                    cancellationToken)
                : null;
            selectedDetails.Add(new CriticalCounterSelectedDetail(
                node.Id,
                node.Kind,
                recognized,
                responsiblePath));
        }

        return Array.AsReadOnly(selectedDetails.ToArray());
    }

    private static CriticalCounterResponsiblePath? FindResponsiblePath(
        Graph graph,
        string startNodeId,
        string targetNodeId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var incomingCounts = graph.Nodes.ToDictionary(node => node.Id, _ => 0, StringComparer.Ordinal);
        var outgoingEdges = graph.Nodes.ToDictionary(
            node => node.Id,
            _ => new List<GraphEdge>(),
            StringComparer.Ordinal);

        foreach (var edge in graph.Edges)
        {
            cancellationToken.ThrowIfCancellationRequested();
            outgoingEdges[edge.From].Add(edge);
            incomingCounts[edge.To]++;
        }

        foreach (var edges in outgoingEdges.Values)
        {
            CancellationAwareOrdering.Sort(
                edges,
                static (left, right) =>
                {
                    var targetComparison = StringComparer.Ordinal.Compare(left.To, right.To);
                    return targetComparison != 0
                        ? targetComparison
                        : StringComparer.Ordinal.Compare(left.Id, right.Id);
                },
                cancellationToken);
        }

        var ready = new SortedSet<string>(
            incomingCounts.Where(entry => entry.Value == 0).Select(entry => entry.Key),
            StringComparer.Ordinal);
        var topologicalOrder = new List<string>(graph.Nodes.Count);
        while (ready.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var nodeId = ready.Min!;
            ready.Remove(nodeId);
            topologicalOrder.Add(nodeId);

            foreach (var edge in outgoingEdges[nodeId])
            {
                incomingCounts[edge.To]--;
                if (incomingCounts[edge.To] == 0)
                {
                    ready.Add(edge.To);
                }
            }
        }

        if (topologicalOrder.Count != graph.Nodes.Count)
        {
            throw new InvalidOperationException(
                "A directed cycle was encountered while reconstructing a critical-counter path.");
        }

        var pathExtremesByNodeId = new Dictionary<string, ResponsiblePathExtremes>(StringComparer.Ordinal)
        {
            [targetNodeId] = new(
                new ResponsiblePathCandidate([targetNodeId], [], 0m),
                new ResponsiblePathCandidate([targetNodeId], [], 0m))
        };

        for (var index = topologicalOrder.Count - 1; index >= 0; index--)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var nodeId = topologicalOrder[index];
            if (string.Equals(nodeId, targetNodeId, StringComparison.Ordinal))
            {
                continue;
            }

            ResponsiblePathCandidate? minimum = null;
            ResponsiblePathCandidate? maximum = null;
            foreach (var edge in outgoingEdges[nodeId])
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!pathExtremesByNodeId.TryGetValue(edge.To, out var suffixExtremes))
                {
                    continue;
                }

                foreach (var suffix in DistinctExtremes(suffixExtremes))
                {
                    var candidate = new ResponsiblePathCandidate(
                        new[] { nodeId }.Concat(suffix.NodeIds).ToArray(),
                        new[] { edge.Id }.Concat(suffix.EdgeIds).ToArray(),
                        (decimal)Math.Log((double)edge.ImportanceToParent) + suffix.RawAccumulatedLogLr);
                    if (minimum is null ||
                        candidate.RawAccumulatedLogLr < minimum.RawAccumulatedLogLr ||
                        (candidate.RawAccumulatedLogLr == minimum.RawAccumulatedLogLr &&
                         IsOrdinalPathEarlier(candidate, minimum)))
                    {
                        minimum = candidate;
                    }

                    if (maximum is null ||
                        candidate.RawAccumulatedLogLr > maximum.RawAccumulatedLogLr ||
                        (candidate.RawAccumulatedLogLr == maximum.RawAccumulatedLogLr &&
                         IsOrdinalPathEarlier(candidate, maximum)))
                    {
                        maximum = candidate;
                    }
                }
            }

            if (minimum is not null && maximum is not null)
            {
                pathExtremesByNodeId[nodeId] = new ResponsiblePathExtremes(minimum, maximum);
            }
        }

        if (!pathExtremesByNodeId.TryGetValue(startNodeId, out var resultExtremes))
        {
            return null;
        }

        var result = IsResponsiblePathBetter(resultExtremes.Minimum, resultExtremes.Maximum)
            ? resultExtremes.Minimum
            : resultExtremes.Maximum;

        return new CriticalCounterResponsiblePath(
            Array.AsReadOnly(result.NodeIds.ToArray()),
            Array.AsReadOnly(result.EdgeIds.ToArray()),
            CanonicalResultNumber.Normalize(result.RawAccumulatedLogLr));
    }

    private static bool IsResponsiblePathBetter(
        ResponsiblePathCandidate candidate,
        ResponsiblePathCandidate current)
    {
        var magnitudeComparison = Math.Abs(candidate.RawAccumulatedLogLr)
            .CompareTo(Math.Abs(current.RawAccumulatedLogLr));
        if (magnitudeComparison != 0)
        {
            return magnitudeComparison > 0;
        }

        var signedComparison = candidate.RawAccumulatedLogLr.CompareTo(current.RawAccumulatedLogLr);
        if (signedComparison != 0)
        {
            return signedComparison > 0;
        }

        var nodeComparison = CompareOrdinalSequences(candidate.NodeIds, current.NodeIds);
        return nodeComparison != 0
            ? nodeComparison < 0
            : CompareOrdinalSequences(candidate.EdgeIds, current.EdgeIds) < 0;
    }

    private static bool IsOrdinalPathEarlier(
        ResponsiblePathCandidate candidate,
        ResponsiblePathCandidate current)
    {
        var nodeComparison = CompareOrdinalSequences(candidate.NodeIds, current.NodeIds);
        return nodeComparison != 0
            ? nodeComparison < 0
            : CompareOrdinalSequences(candidate.EdgeIds, current.EdgeIds) < 0;
    }

    private static IEnumerable<ResponsiblePathCandidate> DistinctExtremes(
        ResponsiblePathExtremes extremes)
    {
        yield return extremes.Minimum;
        if (extremes.Minimum.RawAccumulatedLogLr != extremes.Maximum.RawAccumulatedLogLr ||
            CompareOrdinalSequences(extremes.Minimum.NodeIds, extremes.Maximum.NodeIds) != 0 ||
            CompareOrdinalSequences(extremes.Minimum.EdgeIds, extremes.Maximum.EdgeIds) != 0)
        {
            yield return extremes.Maximum;
        }
    }

    private static int CompareOrdinalSequences(
        IReadOnlyList<string> left,
        IReadOnlyList<string> right)
    {
        var sharedLength = Math.Min(left.Count, right.Count);
        for (var index = 0; index < sharedLength; index++)
        {
            var comparison = StringComparer.Ordinal.Compare(left[index], right[index]);
            if (comparison != 0)
            {
                return comparison;
            }
        }

        return left.Count.CompareTo(right.Count);
    }

    private static IEnumerable<IReadOnlyList<string>> EnumerateOrdinalCombinations(
        IReadOnlyList<string> candidateNodeIds,
        int cardinality)
    {
        if (cardinality < 0 || cardinality > candidateNodeIds.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(cardinality));
        }

        if (cardinality == 0)
        {
            yield return Array.Empty<string>();
            yield break;
        }

        var indices = Enumerable.Range(0, cardinality).ToArray();
        while (true)
        {
            var combination = new string[cardinality];
            for (var index = 0; index < cardinality; index++)
            {
                combination[index] = candidateNodeIds[indices[index]];
            }

            yield return Array.AsReadOnly(combination);

            var position = cardinality - 1;
            while (position >= 0 &&
                   indices[position] == candidateNodeIds.Count - cardinality + position)
            {
                position--;
            }

            if (position < 0)
            {
                yield break;
            }

            indices[position]++;
            for (var index = position + 1; index < cardinality; index++)
            {
                indices[index] = indices[index - 1] + 1;
            }
        }
    }

    private static void ValidateStrategyAndCutoff(string strategy, int? autoCandidateCutoff)
    {
        if (!CriticalCounterV1Contract.IsKnownStrategy(strategy))
        {
            throw new ArgumentException(
                $"Unknown critical-counter-v1 strategy '{strategy}'.",
                nameof(strategy));
        }

        if (autoCandidateCutoff < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(autoCandidateCutoff),
                autoCandidateCutoff,
                "The auto candidate cutoff must be non-negative.");
        }

        if (string.Equals(strategy, OperationStrategyNames.Auto, StringComparison.Ordinal) &&
            autoCandidateCutoff is null)
        {
            throw new ArgumentException(
                "The auto strategy requires an explicitly supplied non-negative candidate cutoff.",
                nameof(autoCandidateCutoff));
        }
    }

    private static (string UsedStrategy, string SelectionReason) ResolveStrategy(
        string requestedStrategy,
        int candidateCount,
        int? autoCandidateCutoff)
    {
        return requestedStrategy switch
        {
            OperationStrategyNames.Exact =>
                (OperationStrategyNames.Exact, RequestedExactReason),
            OperationStrategyNames.Greedy =>
                (OperationStrategyNames.Greedy, RequestedGreedyReason),
            OperationStrategyNames.Auto when candidateCount <= autoCandidateCutoff!.Value =>
                (OperationStrategyNames.Exact, AutoExactReason),
            OperationStrategyNames.Auto =>
                (OperationStrategyNames.Greedy, AutoGreedyReason),
            _ => throw new ArgumentException(
                $"Unknown critical-counter-v1 strategy '{requestedStrategy}'.",
                nameof(requestedStrategy))
        };
    }

    private static bool IsLikelihoodEvidenceKindV0(string nodeKind)
    {
        return string.Equals(nodeKind, "evidence", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(nodeKind, "objection", StringComparison.OrdinalIgnoreCase);
    }

    private sealed record ResponsiblePathCandidate(
        IReadOnlyList<string> NodeIds,
        IReadOnlyList<string> EdgeIds,
        decimal RawAccumulatedLogLr);

    private sealed record ResponsiblePathExtremes(
        ResponsiblePathCandidate Minimum,
        ResponsiblePathCandidate Maximum);
}
