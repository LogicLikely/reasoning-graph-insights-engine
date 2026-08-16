using Backend.Insights.Contracts;
using Backend.Models.Domain;

namespace Backend.Insights.Benchmarking;

public sealed record CriticalCounterCandidateLimit(
    int MaximumCandidateCount,
    IReadOnlyList<string> EligibleCandidateNodeIds)
{
    public int ActualCandidateCount => EligibleCandidateNodeIds.Count;
}

public sealed class CriticalCounterCandidateLimitExceededException : InvalidOperationException
{
    public CriticalCounterCandidateLimitExceededException(
        string targetNodeId,
        int maximumCandidateCount,
        int actualCandidateCount)
        : base(
            $"Critical-counter target '{targetNodeId}' has {actualCandidateCount} eligible candidates, exceeding the explicit limit {maximumCandidateCount}.")
    {
        TargetNodeId = targetNodeId;
        MaximumCandidateCount = maximumCandidateCount;
        ActualCandidateCount = actualCandidateCount;
    }

    public string TargetNodeId { get; }

    public int MaximumCandidateCount { get; }

    public int ActualCandidateCount { get; }
}

public static class CriticalCounterCandidateGuard
{
    public static CriticalCounterCandidateLimit RequireAtMost(
        Graph graph,
        string targetNodeId,
        int maximumCandidateCount,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetNodeId);
        ArgumentOutOfRangeException.ThrowIfNegative(maximumCandidateCount);

        var candidateNodeIds = CriticalCounterV1Contract.GetEligibleCandidateNodeIds(
            graph,
            targetNodeId,
            cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        if (candidateNodeIds.Count > maximumCandidateCount)
        {
            throw new CriticalCounterCandidateLimitExceededException(
                targetNodeId,
                maximumCandidateCount,
                candidateNodeIds.Count);
        }

        return new CriticalCounterCandidateLimit(
            maximumCandidateCount,
            Array.AsReadOnly(candidateNodeIds.ToArray()));
    }
}
