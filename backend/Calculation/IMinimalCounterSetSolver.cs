using Backend.Models.Domain;

namespace Backend.Calculation.MinimalCounterSets;

public interface IMinimalCounterSetSolver
{
    MinimalCounterSetResult Solve(
        Graph graph,
        string targetNodeId,
        IEnumerable<string> nodeIds,
        CancellationToken cancellationToken = default);
}

public interface IMinimalCounterSetEvaluator
{
    IMinimalCounterSetProblem CreateProblem(
        Graph graph,
        string targetNodeId,
        IEnumerable<string> nodeIds,
        CancellationToken cancellationToken = default);
}

public interface IMinimalCounterSetProblem
{
    decimal ThresholdLogOdds { get; }

    decimal InitialTargetLogOdds { get; }

    IReadOnlyList<MinimalCounterCandidate> Candidates { get; }

    /// <summary>
    /// Returns the one-time ordering priority used by the greedy solver.
    /// </summary>
    decimal GetGreedyPriority(
        MinimalCounterCandidate candidate,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        cancellationToken.ThrowIfCancellationRequested();
        return candidate.GreedyPriority;
    }

    /// <summary>
    /// Calculates the target log odds for the exact counter subset.
    /// </summary>
    /// <remarks>
    /// The default implementation preserves compatibility with evaluators whose
    /// counter effects are independent and additive. Bayesian-factor evaluators
    /// override this method because pruning and edge recurrence make combined
    /// counter effects nonlinear.
    /// </remarks>
    decimal CalculateTargetLogOdds(
        IReadOnlyList<string> counterNodeIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(counterNodeIds);
        cancellationToken.ThrowIfCancellationRequested();

        var targetLogOdds = InitialTargetLogOdds;
        foreach (var counterNodeId in counterNodeIds)
        {
            cancellationToken.ThrowIfCancellationRequested();
            targetLogOdds += GetTargetLogOddsContribution(
                counterNodeId,
                cancellationToken);
        }

        return targetLogOdds;
    }

    /// <summary>
    /// Returns one counter's effect when the evaluator supports independent,
    /// additive contributions.
    /// </summary>
    decimal GetTargetLogOddsContribution(
        string counterNodeId,
        CancellationToken cancellationToken = default);
}
