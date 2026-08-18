using Backend.Models.Domain;

namespace Backend.Calculation.MinimalCounterSets;

public sealed class BoundedBruteForceMinimalCounterSetSolver : IMinimalCounterSetSolver
{
    public const int CandidateLimit = 20;

    private readonly IMinimalCounterSetEvaluator _evaluator;

    public BoundedBruteForceMinimalCounterSetSolver(
        IMinimalCounterSetEvaluator evaluator)
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
        var allCandidates = MinimalCounterSetCandidateOrdering.Order(problem.Candidates);
        var searchedCandidates = allCandidates
            .Take(CandidateLimit)
            .ToArray();
        var wasTruncated = allCandidates.Length > searchedCandidates.Length;

        long subsetEvaluations = 1;
        var bestLogOdds = problem.InitialTargetLogOdds;
        IReadOnlyList<string> bestCounterNodeIds = Array.Empty<string>();

        if (bestLogOdds <= problem.ThresholdLogOdds)
        {
            return CreateResult(
                problem,
                allCandidates.Length,
                searchedCandidates.Length,
                candidatesExamined: 0,
                subsetEvaluations,
                largestCardinalityFullyExhausted: 0,
                bestCounterNodeIds,
                bestLogOdds,
                thresholdReached: true,
                wasTruncated);
        }

        var contributions = new decimal[searchedCandidates.Length];
        var contributionLoaded = new bool[searchedCandidates.Length];
        var candidatesExamined = 0;

        for (var cardinality = 1; cardinality <= searchedCandidates.Length; cardinality++)
        {
            var indices = Enumerable.Range(0, cardinality).ToArray();

            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var targetLogOdds = problem.InitialTargetLogOdds;
                for (var position = 0; position < indices.Length; position++)
                {
                    var candidateIndex = indices[position];
                    if (!contributionLoaded[candidateIndex])
                    {
                        contributions[candidateIndex] =
                            problem.GetTargetLogOddsContribution(
                                searchedCandidates[candidateIndex].NodeId,
                                cancellationToken);
                        contributionLoaded[candidateIndex] = true;
                        candidatesExamined++;
                    }

                    targetLogOdds += contributions[candidateIndex];
                }

                subsetEvaluations++;
                if (targetLogOdds < bestLogOdds)
                {
                    bestLogOdds = targetLogOdds;
                    bestCounterNodeIds = GetCounterNodeIds(
                        searchedCandidates,
                        indices);
                }

                if (targetLogOdds <= problem.ThresholdLogOdds)
                {
                    var exhaustedSuccessfulCardinality =
                        IsLastCombination(indices, searchedCandidates.Length);
                    return CreateResult(
                        problem,
                        allCandidates.Length,
                        searchedCandidates.Length,
                        candidatesExamined,
                        subsetEvaluations,
                        largestCardinalityFullyExhausted:
                            exhaustedSuccessfulCardinality
                                ? cardinality
                                : cardinality - 1,
                        GetCounterNodeIds(searchedCandidates, indices),
                        targetLogOdds,
                        thresholdReached: true,
                        wasTruncated);
                }

                if (!MoveNextCombination(indices, searchedCandidates.Length))
                {
                    break;
                }
            }
        }

        return CreateResult(
            problem,
            allCandidates.Length,
            searchedCandidates.Length,
            candidatesExamined,
            subsetEvaluations,
            largestCardinalityFullyExhausted: searchedCandidates.Length,
            bestCounterNodeIds,
            bestLogOdds,
            thresholdReached: false,
            wasTruncated);
    }

    private static MinimalCounterSetResult CreateResult(
        IMinimalCounterSetProblem problem,
        int totalCandidateCount,
        int searchedCandidateCount,
        int candidatesExamined,
        long subsetEvaluations,
        int? largestCardinalityFullyExhausted,
        IReadOnlyList<string> counterNodeIds,
        decimal finalTargetLogOdds,
        bool thresholdReached,
        bool wasTruncated)
    {
        return new MinimalCounterSetResult
        {
            CounterNodeIds = counterNodeIds,
            ThresholdReached = thresholdReached,
            ThresholdLogOdds = problem.ThresholdLogOdds,
            InitialTargetLogOdds = problem.InitialTargetLogOdds,
            FinalTargetLogOdds = finalTargetLogOdds,
            TotalCandidateCount = totalCandidateCount,
            SearchedCandidateCount = searchedCandidateCount,
            CandidatesExamined = candidatesExamined,
            SubsetEvaluations = subsetEvaluations,
            LargestCardinalityFullyExhausted = largestCardinalityFullyExhausted,
            ProofStatus = wasTruncated
                ? MinimalCounterSetProofStatus.NotProven
                : MinimalCounterSetProofStatus.Proven,
            StopReason = wasTruncated
                ? MinimalCounterSetStopReason.CandidateLimit
                : MinimalCounterSetStopReason.Completed
        };
    }

    private static IReadOnlyList<string> GetCounterNodeIds(
        IReadOnlyList<MinimalCounterCandidate> candidates,
        IReadOnlyList<int> indices)
    {
        var nodeIds = new string[indices.Count];
        for (var index = 0; index < indices.Count; index++)
        {
            nodeIds[index] = candidates[indices[index]].NodeId;
        }

        return nodeIds;
    }

    private static bool MoveNextCombination(int[] indices, int candidateCount)
    {
        for (var position = indices.Length - 1; position >= 0; position--)
        {
            var maximumAtPosition = candidateCount - indices.Length + position;
            if (indices[position] >= maximumAtPosition)
            {
                continue;
            }

            indices[position]++;
            for (var next = position + 1; next < indices.Length; next++)
            {
                indices[next] = indices[next - 1] + 1;
            }

            return true;
        }

        return false;
    }

    private static bool IsLastCombination(
        IReadOnlyList<int> indices,
        int candidateCount)
    {
        for (var position = 0; position < indices.Count; position++)
        {
            if (indices[position] != candidateCount - indices.Count + position)
            {
                return false;
            }
        }

        return true;
    }
}
