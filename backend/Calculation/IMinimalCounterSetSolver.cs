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

    decimal GetTargetLogOddsContribution(
        string counterNodeId,
        CancellationToken cancellationToken = default);
}
