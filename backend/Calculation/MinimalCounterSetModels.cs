namespace Backend.Calculation.MinimalCounterSets;

public sealed record MinimalCounterCandidate(
    string NodeId,
    decimal GreedyPriority);

public enum MinimalCounterSetProofStatus
{
    NotApplicable,
    Proven,
    NotProven
}

public enum MinimalCounterSetStopReason
{
    Completed,
    CandidateLimit
}

public sealed record MinimalCounterSetResult
{
    public required IReadOnlyList<string> CounterNodeIds { get; init; }

    public required bool ThresholdReached { get; init; }

    public required decimal ThresholdLogOdds { get; init; }

    public required decimal InitialTargetLogOdds { get; init; }

    public required decimal FinalTargetLogOdds { get; init; }

    public required int TotalCandidateCount { get; init; }

    public required int SearchedCandidateCount { get; init; }

    public required int CandidatesExamined { get; init; }

    public required long SubsetEvaluations { get; init; }

    public required int? LargestCardinalityFullyExhausted { get; init; }

    public required MinimalCounterSetProofStatus ProofStatus { get; init; }

    public required MinimalCounterSetStopReason StopReason { get; init; }

    public int ExcludedCandidateCount => TotalCandidateCount - SearchedCandidateCount;
}

internal static class MinimalCounterSetCandidateOrdering
{
    public static IComparer<MinimalCounterCandidate> PriorityComparer { get; } =
        Comparer<MinimalCounterCandidate>.Create((left, right) =>
        {
            var priorityComparison = right.GreedyPriority.CompareTo(left.GreedyPriority);
            return priorityComparison != 0
                ? priorityComparison
                : StringComparer.Ordinal.Compare(left.NodeId, right.NodeId);
        });

    public static MinimalCounterCandidate[] Order(
        IEnumerable<MinimalCounterCandidate> candidates)
    {
        return candidates
            .Order(PriorityComparer)
            .ToArray();
    }
}
