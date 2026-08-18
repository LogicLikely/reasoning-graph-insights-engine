using System.Text.Json.Serialization;

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
    TimeBudget
}

public enum MinimalCounterSetTimeoutStage
{
    Preparation,
    Search
}

public sealed record MinimalCounterSetResult
{
    public required IReadOnlyList<string> CounterNodeIds { get; init; }

    public required bool ThresholdReached { get; init; }

    public required decimal? ThresholdLogOdds { get; init; }

    public required decimal? InitialTargetLogOdds { get; init; }

    public required decimal? FinalTargetLogOdds { get; init; }

    public required int? TotalCandidateCount { get; init; }

    public required int? SearchedCandidateCount { get; init; }

    [JsonIgnore]
    public int CandidatesExamined { get; init; }

    [JsonIgnore]
    public long SubsetEvaluations { get; init; }

    [JsonIgnore]
    public int? LargestCardinalityFullyExhausted { get; init; }

    [JsonIgnore]
    public int? ActiveCardinality { get; init; }

    [JsonIgnore]
    public long? SubsetEvaluationsAtActiveCardinality { get; init; }

    [JsonIgnore]
    public string? TotalSubsetsAtActiveCardinality { get; init; }

    [JsonIgnore]
    public string? TotalPossibleSubsets { get; init; }

    [JsonIgnore]
    public double? TimeBudgetMilliseconds { get; init; }

    [JsonIgnore]
    public double? PreparationElapsedMilliseconds { get; init; }

    [JsonIgnore]
    public double? SearchElapsedMilliseconds { get; init; }

    [JsonIgnore]
    public double? SubsetEvaluationsPerSecond { get; init; }

    [JsonIgnore]
    public MinimalCounterSetTimeoutStage? TimeoutStage { get; init; }

    public required MinimalCounterSetProofStatus ProofStatus { get; init; }

    public required MinimalCounterSetStopReason StopReason { get; init; }

    public int? ExcludedCandidateCount =>
        TotalCandidateCount.HasValue && SearchedCandidateCount.HasValue
            ? TotalCandidateCount.Value - SearchedCandidateCount.Value
            : null;
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
