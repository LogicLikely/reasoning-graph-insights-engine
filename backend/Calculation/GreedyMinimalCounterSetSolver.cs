using Backend.Models.Domain;

namespace Backend.Calculation.MinimalCounterSets;

public sealed class GreedyMinimalCounterSetSolver : IMinimalCounterSetSolver
{
    private readonly IMinimalCounterSetEvaluator _evaluator;

    public GreedyMinimalCounterSetSolver(IMinimalCounterSetEvaluator evaluator)
    {
        _evaluator = evaluator ?? throw new ArgumentNullException(nameof(evaluator));
    }

    public MinimalCounterSetResult Solve(
        Graph graph,
        string targetNodeId,
        IEnumerable<string> nodeIds,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var problem = _evaluator.CreateProblem(
            graph,
            targetNodeId,
            nodeIds,
            cancellationToken);
        var candidates = problem.Candidates.ToArray();
        var counterQueue = new PriorityQueue<
            MinimalCounterCandidate,
            MinimalCounterCandidate>(
                MinimalCounterSetCandidateOrdering.PriorityComparer);
        foreach (var candidate in candidates)
        {
            counterQueue.Enqueue(candidate, candidate);
        }

        var targetLogOdds = problem.InitialTargetLogOdds;
        var countersUsed = new List<string>();
        var candidatesExamined = 0;

        while (counterQueue.Count > 0 &&
               targetLogOdds > problem.ThresholdLogOdds)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var candidate = counterQueue.Dequeue();
            countersUsed.Add(candidate.NodeId);
            candidatesExamined++;
            targetLogOdds += problem.GetTargetLogOddsContribution(
                candidate.NodeId,
                cancellationToken);
        }

        return new MinimalCounterSetResult
        {
            CounterNodeIds = countersUsed,
            ThresholdReached = targetLogOdds <= problem.ThresholdLogOdds,
            ThresholdLogOdds = problem.ThresholdLogOdds,
            InitialTargetLogOdds = problem.InitialTargetLogOdds,
            FinalTargetLogOdds = targetLogOdds,
            TotalCandidateCount = candidates.Length,
            SearchedCandidateCount = candidates.Length,
            CandidatesExamined = candidatesExamined,
            SubsetEvaluations = 0,
            LargestCardinalityFullyExhausted = null,
            ProofStatus = MinimalCounterSetProofStatus.NotApplicable,
            StopReason = MinimalCounterSetStopReason.Completed
        };
    }
}
