using System.Globalization;
using System.Numerics;
using Backend.Models.Domain;

namespace Backend.Calculation.MinimalCounterSets;

/// <summary>
/// Exhaustively searches counter-node subsets in increasing cardinality until
/// it proves a minimum result or reaches its server-owned time budget.
///
/// The historical class name is retained to avoid changing the public route and
/// service surface while benchmark data identifies this implementation as the
/// time-bounded exhaustive reference algorithm.
/// </summary>
public sealed class BoundedBruteForceMinimalCounterSetSolver : IMinimalCounterSetSolver
{
    public const int TimeBudgetMilliseconds = 120_000;

    private static readonly TimeSpan DefaultTimeBudget =
        TimeSpan.FromMilliseconds(TimeBudgetMilliseconds);

    private readonly IMinimalCounterSetEvaluator _evaluator;
    private readonly TimeProvider _timeProvider;
    private readonly TimeSpan _timeBudget;

    public BoundedBruteForceMinimalCounterSetSolver(
        IMinimalCounterSetEvaluator evaluator,
        TimeProvider? timeProvider = null,
        TimeSpan? timeBudget = null)
    {
        _evaluator = evaluator ?? throw new ArgumentNullException(nameof(evaluator));
        _timeProvider = timeProvider ?? TimeProvider.System;
        _timeBudget = timeBudget ?? DefaultTimeBudget;

        if (_timeBudget <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(timeBudget),
                "The exhaustive-search time budget must be greater than zero.");
        }
    }

    public double ConfiguredTimeBudgetMilliseconds => _timeBudget.TotalMilliseconds;

    public MinimalCounterSetResult Solve(
        Graph graph,
        string targetNodeId,
        IEnumerable<string> nodeIds,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var operationStarted = _timeProvider.GetTimestamp();
        using var budgetCancellation = new CancellationTokenSource(
            _timeBudget,
            _timeProvider);
        using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            budgetCancellation.Token);

        IMinimalCounterSetProblem? problem = null;
        MinimalCounterCandidate[] searchedCandidates = [];
        int? totalCandidateCount = null;
        int? searchedCandidateCount = null;
        var candidatesExamined = 0;
        long subsetEvaluations = 0;
        int? largestCardinalityFullyExhausted = null;
        int? activeCardinality = null;
        long activeCardinalityEvaluations = 0;
        string? totalSubsetsAtActiveCardinality = null;
        string? totalPossibleSubsets = null;
        IReadOnlyList<string> bestCounterNodeIds = Array.Empty<string>();
        decimal? bestLogOdds = null;
        var preparationElapsedMilliseconds = 0d;
        long? searchStarted = null;
        var timeoutStage = MinimalCounterSetTimeoutStage.Preparation;

        try
        {
            EnsureCanContinue(
                cancellationToken,
                budgetCancellation,
                operationStarted);

            problem = _evaluator.CreateProblem(
                graph,
                targetNodeId,
                nodeIds,
                linkedCancellation.Token);
            totalCandidateCount = problem.Candidates.Count;
            searchedCandidateCount = totalCandidateCount;
            totalPossibleSubsets = FormatInteger(
                BigInteger.One << totalCandidateCount.Value);
            bestLogOdds = problem.InitialTargetLogOdds;
            preparationElapsedMilliseconds = GetElapsedMilliseconds(operationStarted);

            // Creating the problem also evaluates the empty set. Preserve that
            // proof if preparation finishes at the deadline.
            subsetEvaluations = 1;
            largestCardinalityFullyExhausted = 0;
            if (bestLogOdds.Value <= problem.ThresholdLogOdds)
            {
                return CreateResult(
                    problem,
                    totalCandidateCount,
                    searchedCandidateCount,
                    candidatesExamined,
                    subsetEvaluations,
                    largestCardinalityFullyExhausted,
                    activeCardinality: null,
                    subsetEvaluationsAtActiveCardinality: null,
                    totalSubsetsAtActiveCardinality: null,
                    totalPossibleSubsets,
                    bestCounterNodeIds,
                    bestLogOdds,
                    thresholdReached: true,
                    MinimalCounterSetProofStatus.Proven,
                    MinimalCounterSetStopReason.Completed,
                    timeoutStage: null,
                    preparationElapsedMilliseconds,
                    searchElapsedMilliseconds: 0d);
            }

            EnsureCanContinue(
                cancellationToken,
                budgetCancellation,
                operationStarted);

            searchedCandidates = MinimalCounterSetCandidateOrdering.Order(
                problem.Candidates);
            preparationElapsedMilliseconds = GetElapsedMilliseconds(operationStarted);

            EnsureCanContinue(
                cancellationToken,
                budgetCancellation,
                operationStarted);

            timeoutStage = MinimalCounterSetTimeoutStage.Search;
            searchStarted = _timeProvider.GetTimestamp();
            var evaluatedCandidates = new bool[searchedCandidates.Length];

            for (var cardinality = 1;
                 cardinality <= searchedCandidates.Length;
                 cardinality++)
            {
                activeCardinality = cardinality;
                activeCardinalityEvaluations = 0;
                totalSubsetsAtActiveCardinality = FormatInteger(
                    CalculateCombinationCount(
                        searchedCandidates.Length,
                        cardinality));
                var indices = Enumerable.Range(0, cardinality).ToArray();

                while (true)
                {
                    EnsureCanContinue(
                        cancellationToken,
                        budgetCancellation,
                        operationStarted);

                    var counterNodeIds = GetCounterNodeIds(
                        searchedCandidates,
                        indices);
                    var targetLogOdds = problem.CalculateTargetLogOdds(
                        counterNodeIds,
                        linkedCancellation.Token);

                    foreach (var candidateIndex in indices)
                    {
                        if (evaluatedCandidates[candidateIndex])
                        {
                            continue;
                        }

                        evaluatedCandidates[candidateIndex] = true;
                        candidatesExamined++;
                    }

                    subsetEvaluations++;
                    activeCardinalityEvaluations++;
                    if (!bestLogOdds.HasValue || targetLogOdds < bestLogOdds.Value)
                    {
                        bestLogOdds = targetLogOdds;
                        bestCounterNodeIds = GetCounterNodeIds(
                            searchedCandidates,
                            indices);
                    }

                    var isLastCombination = IsLastCombination(
                        indices,
                        searchedCandidates.Length);
                    if (isLastCombination)
                    {
                        largestCardinalityFullyExhausted = cardinality;
                        activeCardinality = null;
                        totalSubsetsAtActiveCardinality = null;
                    }

                    // Do not discard a proof that the just-completed evaluation
                    // established merely because the deadline elapsed while that
                    // evaluation was finishing. Explicit request cancellation still
                    // takes precedence if it raced the deadline.
                    cancellationToken.ThrowIfCancellationRequested();

                    if (targetLogOdds <= problem.ThresholdLogOdds)
                    {
                        return CreateResult(
                            problem,
                            totalCandidateCount,
                            searchedCandidateCount,
                            candidatesExamined,
                            subsetEvaluations,
                            largestCardinalityFullyExhausted,
                            activeCardinality: null,
                            subsetEvaluationsAtActiveCardinality: null,
                            totalSubsetsAtActiveCardinality: null,
                            totalPossibleSubsets,
                            counterNodeIds,
                            targetLogOdds,
                            thresholdReached: true,
                            MinimalCounterSetProofStatus.Proven,
                            MinimalCounterSetStopReason.Completed,
                            timeoutStage: null,
                            preparationElapsedMilliseconds,
                            GetElapsedMilliseconds(searchStarted.Value));
                    }

                    if (isLastCombination)
                    {
                        break;
                    }

                    EnsureCanContinue(
                        cancellationToken,
                        budgetCancellation,
                        operationStarted);
                    MoveNextCombination(indices, searchedCandidates.Length);
                }
            }

            return CreateResult(
                problem,
                totalCandidateCount,
                searchedCandidateCount,
                candidatesExamined,
                subsetEvaluations,
                largestCardinalityFullyExhausted,
                activeCardinality: null,
                subsetEvaluationsAtActiveCardinality: null,
                totalSubsetsAtActiveCardinality: null,
                totalPossibleSubsets,
                bestCounterNodeIds,
                bestLogOdds,
                thresholdReached: false,
                MinimalCounterSetProofStatus.Proven,
                MinimalCounterSetStopReason.Completed,
                timeoutStage: null,
                preparationElapsedMilliseconds,
                searchStarted.HasValue
                    ? GetElapsedMilliseconds(searchStarted.Value)
                    : 0d);
        }
        catch (TimeBudgetReachedException)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return CreateTimedOutResult();
        }
        catch (OperationCanceledException) when (
            !cancellationToken.IsCancellationRequested &&
            (budgetCancellation.IsCancellationRequested ||
             HasTimeBudgetElapsed(operationStarted)))
        {
            cancellationToken.ThrowIfCancellationRequested();
            return CreateTimedOutResult();
        }

        MinimalCounterSetResult CreateTimedOutResult()
        {
            if (timeoutStage == MinimalCounterSetTimeoutStage.Preparation)
            {
                preparationElapsedMilliseconds =
                    GetElapsedMilliseconds(operationStarted);
            }

            var searchElapsedMilliseconds = searchStarted.HasValue
                ? GetElapsedMilliseconds(searchStarted.Value)
                : 0d;

            return CreateResult(
                problem,
                totalCandidateCount,
                searchedCandidateCount,
                candidatesExamined,
                subsetEvaluations,
                largestCardinalityFullyExhausted,
                activeCardinality,
                activeCardinality.HasValue
                    ? activeCardinalityEvaluations
                    : null,
                totalSubsetsAtActiveCardinality,
                totalPossibleSubsets,
                bestCounterNodeIds,
                bestLogOdds,
                thresholdReached: false,
                MinimalCounterSetProofStatus.NotProven,
                MinimalCounterSetStopReason.TimeBudget,
                timeoutStage,
                preparationElapsedMilliseconds,
                searchElapsedMilliseconds);
        }
    }

    private MinimalCounterSetResult CreateResult(
        IMinimalCounterSetProblem? problem,
        int? totalCandidateCount,
        int? searchedCandidateCount,
        int candidatesExamined,
        long subsetEvaluations,
        int? largestCardinalityFullyExhausted,
        int? activeCardinality,
        long? subsetEvaluationsAtActiveCardinality,
        string? totalSubsetsAtActiveCardinality,
        string? totalPossibleSubsets,
        IReadOnlyList<string> counterNodeIds,
        decimal? finalTargetLogOdds,
        bool thresholdReached,
        MinimalCounterSetProofStatus proofStatus,
        MinimalCounterSetStopReason stopReason,
        MinimalCounterSetTimeoutStage? timeoutStage,
        double preparationElapsedMilliseconds,
        double searchElapsedMilliseconds)
    {
        var completedSearchEvaluations = Math.Max(0L, subsetEvaluations - 1L);
        double? evaluationsPerSecond = searchElapsedMilliseconds > 0d
            ? completedSearchEvaluations /
                (searchElapsedMilliseconds / 1_000d)
            : null;

        return new MinimalCounterSetResult
        {
            CounterNodeIds = counterNodeIds,
            ThresholdReached = thresholdReached,
            ThresholdLogOdds = problem?.ThresholdLogOdds,
            InitialTargetLogOdds = problem?.InitialTargetLogOdds,
            FinalTargetLogOdds = finalTargetLogOdds,
            TotalCandidateCount = totalCandidateCount,
            SearchedCandidateCount = searchedCandidateCount,
            CandidatesExamined = candidatesExamined,
            SubsetEvaluations = subsetEvaluations,
            LargestCardinalityFullyExhausted =
                largestCardinalityFullyExhausted,
            ActiveCardinality = activeCardinality,
            SubsetEvaluationsAtActiveCardinality =
                subsetEvaluationsAtActiveCardinality,
            TotalSubsetsAtActiveCardinality =
                totalSubsetsAtActiveCardinality,
            TotalPossibleSubsets = totalPossibleSubsets,
            TimeBudgetMilliseconds = _timeBudget.TotalMilliseconds,
            PreparationElapsedMilliseconds = preparationElapsedMilliseconds,
            SearchElapsedMilliseconds = searchElapsedMilliseconds,
            SubsetEvaluationsPerSecond = evaluationsPerSecond,
            ProofStatus = proofStatus,
            StopReason = stopReason,
            TimeoutStage = timeoutStage
        };
    }

    private void EnsureCanContinue(
        CancellationToken cancellationToken,
        CancellationTokenSource budgetCancellation,
        long operationStarted)
    {
        // An explicit request cancellation always wins if it races the deadline.
        cancellationToken.ThrowIfCancellationRequested();

        if (budgetCancellation.IsCancellationRequested ||
            HasTimeBudgetElapsed(operationStarted))
        {
            throw new TimeBudgetReachedException();
        }
    }

    private bool HasTimeBudgetElapsed(long operationStarted)
    {
        return _timeProvider.GetElapsedTime(operationStarted) >= _timeBudget;
    }

    private double GetElapsedMilliseconds(long started)
    {
        return _timeProvider.GetElapsedTime(started).TotalMilliseconds;
    }

    private static string FormatInteger(BigInteger value)
    {
        return value.ToString(CultureInfo.InvariantCulture);
    }

    private static BigInteger CalculateCombinationCount(int candidateCount, int cardinality)
    {
        var shorterCardinality = Math.Min(
            cardinality,
            candidateCount - cardinality);
        var result = BigInteger.One;
        for (var factor = 1; factor <= shorterCardinality; factor++)
        {
            result *= candidateCount - shorterCardinality + factor;
            result /= factor;
        }

        return result;
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

    private sealed class TimeBudgetReachedException : Exception
    {
    }
}
