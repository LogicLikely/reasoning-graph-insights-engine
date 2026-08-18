using System.Numerics;

namespace Backend.Seeding;

/// <summary>
/// Defines the deterministic minimal-counter-set workload encoded by the
/// non-deep stress graphs. The seed SQL solves node priors from these values
/// while preserving the graph topology and edge likelihood ratios.
/// </summary>
public static class StressGraphBenchmarkContract
{
    public const decimal ThresholdLogOdds = -1m;

    public const decimal InitialTargetLogOdds = 0.2m;

    public const decimal EffectiveCounterContributionLogOdds = -0.16m;

    public const int ExpectedMinimumCounterSetCardinality = 8;

    public static decimal TargetLogOddsAfterCounters(int counterCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(counterCount);

        return InitialTargetLogOdds +
            (counterCount * EffectiveCounterContributionLogOdds);
    }

    public static BigInteger ExpectedExhaustiveEvaluationsToFirstMinimum(
        int candidateCount)
    {
        if (candidateCount < ExpectedMinimumCounterSetCardinality)
        {
            throw new ArgumentOutOfRangeException(
                nameof(candidateCount),
                candidateCount,
                $"At least {ExpectedMinimumCounterSetCardinality} candidates are required.");
        }

        var evaluations = BigInteger.One;
        for (var cardinality = 1;
             cardinality < ExpectedMinimumCounterSetCardinality;
             cardinality++)
        {
            evaluations += Combination(candidateCount, cardinality);
        }

        // Every subset with a smaller cardinality is exhausted before the
        // first qualifying eight-counter subset is evaluated.
        return evaluations + BigInteger.One;
    }

    private static BigInteger Combination(int itemCount, int selectionCount)
    {
        var result = BigInteger.One;
        for (var index = 1; index <= selectionCount; index++)
        {
            result = result * (itemCount - selectionCount + index) / index;
        }

        return result;
    }
}
