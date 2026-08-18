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
        var targetLogOdds = problem.InitialTargetLogOdds;
        var counterQueue = new PriorityQueue<
            MinimalCounterCandidate,
            MinimalCounterCandidate>(
                MinimalCounterSetCandidateOrdering.PriorityComparer);
        if (targetLogOdds > problem.ThresholdLogOdds)
        {
            foreach (var candidate in candidates)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var rankedCandidate = candidate with
                {
                    GreedyPriority = problem.GetGreedyPriority(
                        candidate,
                        cancellationToken)
                };
                counterQueue.Enqueue(rankedCandidate, rankedCandidate);
            }
        }

        var countersUsed = new List<string>();
        IReadOnlyList<string> bestCounters = Array.Empty<string>();
        var bestTargetLogOdds = targetLogOdds;
        var candidatesExamined = 0;

        while (counterQueue.Count > 0 &&
               targetLogOdds > problem.ThresholdLogOdds)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var candidate = counterQueue.Dequeue();
            countersUsed.Add(candidate.NodeId);
            candidatesExamined++;
            targetLogOdds = problem.CalculateTargetLogOdds(
                countersUsed,
                cancellationToken);
            if (targetLogOdds < bestTargetLogOdds)
            {
                bestTargetLogOdds = targetLogOdds;
                bestCounters = countersUsed.ToArray();
            }
        }

        var thresholdReached = targetLogOdds <= problem.ThresholdLogOdds;
        var resultCounters = thresholdReached
            ? countersUsed
            : bestCounters;
        var resultTargetLogOdds = thresholdReached
            ? targetLogOdds
            : bestTargetLogOdds;

        return new MinimalCounterSetResult
        {
            CounterNodeIds = resultCounters,
            ThresholdReached = thresholdReached,
            ThresholdLogOdds = problem.ThresholdLogOdds,
            InitialTargetLogOdds = problem.InitialTargetLogOdds,
            FinalTargetLogOdds = resultTargetLogOdds,
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
