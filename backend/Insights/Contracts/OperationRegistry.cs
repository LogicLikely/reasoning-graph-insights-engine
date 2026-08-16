using System.Collections.ObjectModel;

namespace Backend.Insights.Contracts;

public enum OperationExposure
{
    BenchmarkDiagnostic,
    AnalysisAndBenchmark
}

public enum OperationResultSurface
{
    TimingAndCountSummary,
    GraphAndPayloadSummary,
    CountsAdmissionStatusAndOptionalSafeProjection,
    SummaryRankedOrderedPathsAndGraphMapFocus,
    DiagnosticResultAndTiming,
    SummaryDistributionAndDeterministicTop100,
    SelectedCountersQualityAndGraphMapFocus,
    LeastRobustSummaryDistributionAndDeterministicTop100,
    BeforeAndAfterLikelihoodSummary
}

public static class OperationKeys
{
    public const string GraphCatalog = "graph.catalog";
    public const string GraphFetch = "graph.fetch";
    public const string GraphSearch = "graph.search";
    public const string PathStrongest = "path.strongest";
    public const string PathSinglePair = "path.single-pair";
    public const string EvidenceImpactRanking = "evidence.impact-ranking";
    public const string CounterCriticalSet = "counter.critical-set";
    public const string NodeRobustness = "node.robustness";
    public const string LikelihoodRecalculate = "likelihood.recalculate";
}

public static class OperationStrategyNames
{
    public const string Minimum = "minimum";
    public const string Maximum = "maximum";
    public const string Exact = "exact";
    public const string Greedy = "greedy";
    public const string Auto = "auto";
}

public static class AlgorithmSemanticIdentities
{
    public const string GraphCatalogV1 = "graph-catalog-v1";
    public const string GraphFetchV1 = "graph-fetch-v1";
    public const string GraphSearchV1 = "graph-search-v1";
    public const string StrongestPathV1 = "strongest-path-v1";
    public const string StrongestPathScalarV0 = "strongest-path-scalar-v0";
    public const string SinglePairPathV0 = "single-pair-path-v0";
    public const string EvidenceImpactV0 = "evidence-impact-v0";
    public const string CriticalCounterV1 = "critical-counter-v1";
    public const string RobustnessV0 = "robustness-v0";
    public const string LikelihoodRecalculateV0 = "likelihood-recalculate-v0";

    // Characterizes the current endpoint without promoting it into the planned registry.
    public const string LegacyCriticalCounterHeuristicV0 = "critical-counter-heuristic-v0";
}

public sealed class OperationContract
{
    private readonly ReadOnlyCollection<string> _supportedRequestedStrategies;
    private readonly ReadOnlyCollection<string> _supportedUsedStrategies;

    public OperationContract(
        string key,
        string purpose,
        OperationExposure exposure,
        OperationResultSurface resultSurface,
        string semanticIdentity,
        IEnumerable<string>? supportedRequestedStrategies = null,
        IEnumerable<string>? supportedUsedStrategies = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentException.ThrowIfNullOrWhiteSpace(purpose);
        ArgumentException.ThrowIfNullOrWhiteSpace(semanticIdentity);
        _ = Backend.Insights.Contracts.SemanticIdentity.Parse(semanticIdentity);

        Key = key;
        Purpose = purpose;
        Exposure = exposure;
        ResultSurface = resultSurface;
        SemanticIdentity = semanticIdentity;

        var strategies = (supportedRequestedStrategies ?? [])
            .Select(strategy => strategy?.Trim() ?? string.Empty)
            .ToArray();

        if (strategies.Any(string.IsNullOrWhiteSpace))
        {
            throw new ArgumentException("Requested strategy names must not be empty.", nameof(supportedRequestedStrategies));
        }

        if (strategies.Distinct(StringComparer.Ordinal).Count() != strategies.Length)
        {
            throw new ArgumentException("Requested strategy names must be unique.", nameof(supportedRequestedStrategies));
        }

        _supportedRequestedStrategies = Array.AsReadOnly(strategies);

        var usedStrategies = (supportedUsedStrategies ?? strategies)
            .Select(strategy => strategy?.Trim() ?? string.Empty)
            .ToArray();
        if (usedStrategies.Any(string.IsNullOrWhiteSpace) ||
            usedStrategies.Distinct(StringComparer.Ordinal).Count() != usedStrategies.Length)
        {
            throw new ArgumentException(
                "Used strategy names must be non-empty and unique.",
                nameof(supportedUsedStrategies));
        }

        _supportedUsedStrategies = Array.AsReadOnly(usedStrategies);
    }

    public string Key { get; }

    public string Purpose { get; }

    public OperationExposure Exposure { get; }

    public OperationResultSurface ResultSurface { get; }

    public string SemanticIdentity { get; }

    public IReadOnlyList<string> SupportedRequestedStrategies => _supportedRequestedStrategies;

    public IReadOnlyList<string> SupportedUsedStrategies => _supportedUsedStrategies;

    public bool SupportsRequestedStrategy(string strategy)
    {
        return _supportedRequestedStrategies.Contains(strategy, StringComparer.Ordinal);
    }

    public bool SupportsUsedStrategy(string strategy)
    {
        return _supportedUsedStrategies.Contains(strategy, StringComparer.Ordinal);
    }
}

public static class InsightOperationRegistry
{
    private static readonly ReadOnlyCollection<OperationContract> OrderedOperations = Array.AsReadOnly(
    [
        new OperationContract(
            OperationKeys.GraphCatalog,
            "Measure catalog retrieval/count aggregation with all stress graphs installed.",
            OperationExposure.BenchmarkDiagnostic,
            OperationResultSurface.TimingAndCountSummary,
            AlgorithmSemanticIdentities.GraphCatalogV1),
        new OperationContract(
            OperationKeys.GraphFetch,
            "Fetch, transfer, parse, and adapt a complete graph.",
            OperationExposure.BenchmarkDiagnostic,
            OperationResultSurface.GraphAndPayloadSummary,
            AlgorithmSemanticIdentities.GraphFetchV1),
        new OperationContract(
            OperationKeys.GraphSearch,
            "Find matches and the complete ancestor union, then admit or reject visualization.",
            OperationExposure.BenchmarkDiagnostic,
            OperationResultSurface.CountsAdmissionStatusAndOptionalSafeProjection,
            AlgorithmSemanticIdentities.GraphSearchV1),
        new OperationContract(
            OperationKeys.PathStrongest,
            "Find strongest paths in the requested direction.",
            OperationExposure.AnalysisAndBenchmark,
            OperationResultSurface.SummaryRankedOrderedPathsAndGraphMapFocus,
            AlgorithmSemanticIdentities.StrongestPathV1),
        new OperationContract(
            OperationKeys.PathSinglePair,
            "Exercise the current min/max single-pair path diagnostic.",
            OperationExposure.BenchmarkDiagnostic,
            OperationResultSurface.DiagnosticResultAndTiming,
            AlgorithmSemanticIdentities.SinglePairPathV0,
            [OperationStrategyNames.Minimum, OperationStrategyNames.Maximum]),
        new OperationContract(
            OperationKeys.EvidenceImpactRanking,
            "Rank supporting and counter evidence by target probability impact.",
            OperationExposure.AnalysisAndBenchmark,
            OperationResultSurface.SummaryDistributionAndDeterministicTop100,
            AlgorithmSemanticIdentities.EvidenceImpactV0),
        new OperationContract(
            OperationKeys.CounterCriticalSet,
            "Find a threshold-reaching counter set with exact, greedy, or auto strategy.",
            OperationExposure.AnalysisAndBenchmark,
            OperationResultSurface.SelectedCountersQualityAndGraphMapFocus,
            AlgorithmSemanticIdentities.CriticalCounterV1,
            [OperationStrategyNames.Exact, OperationStrategyNames.Greedy, OperationStrategyNames.Auto],
            [OperationStrategyNames.Exact, OperationStrategyNames.Greedy]),
        new OperationContract(
            OperationKeys.NodeRobustness,
            "Rank nodes by the versioned robustness calculation.",
            OperationExposure.AnalysisAndBenchmark,
            OperationResultSurface.LeastRobustSummaryDistributionAndDeterministicTop100,
            AlgorithmSemanticIdentities.RobustnessV0),
        new OperationContract(
            OperationKeys.LikelihoodRecalculate,
            "Recalculate a selected node/ancestor chain after a defined change.",
            OperationExposure.BenchmarkDiagnostic,
            OperationResultSurface.BeforeAndAfterLikelihoodSummary,
            AlgorithmSemanticIdentities.LikelihoodRecalculateV0)
    ]);

    private static readonly IReadOnlyDictionary<string, OperationContract> OperationsByKey =
        new ReadOnlyDictionary<string, OperationContract>(
            OrderedOperations.ToDictionary(operation => operation.Key, StringComparer.Ordinal));

    public static IReadOnlyList<OperationContract> Operations => OrderedOperations;

    public static OperationContract Get(string operationKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operationKey);
        return OperationsByKey.TryGetValue(operationKey, out var operation)
            ? operation
            : throw new KeyNotFoundException($"Unknown Insights operation '{operationKey}'.");
    }

    public static bool TryGet(string operationKey, out OperationContract? operation)
    {
        return OperationsByKey.TryGetValue(operationKey, out operation);
    }

    public static void ValidateResultStrategySelection(
        string operationKey,
        StrategySelection strategy,
        ExecutionStatus executionStatus)
    {
        ArgumentNullException.ThrowIfNull(strategy);
        var operation = Get(operationKey);

        // Failed requests must retain the caller's raw value for validation reporting.
        // Pending and interrupted executions may not have selected an implementation yet.
        if (executionStatus != ExecutionStatus.Succeeded)
        {
            return;
        }

        if (operation.SupportedRequestedStrategies.Count == 0)
        {
            if (strategy.Requested is not null || strategy.Used is not null)
            {
                throw new ArgumentException(
                    $"Operation '{operation.Key}' does not accept a strategy.",
                    nameof(strategy));
            }

            return;
        }

        if (strategy.Requested is null || !operation.SupportsRequestedStrategy(strategy.Requested))
        {
            throw new ArgumentException(
                $"Requested strategy '{strategy.Requested}' is not supported by operation '{operation.Key}'.",
                nameof(strategy));
        }

        if (strategy.Used is null || !operation.SupportsUsedStrategy(strategy.Used))
        {
            throw new ArgumentException(
                $"Used strategy '{strategy.Used}' is not supported by operation '{operation.Key}'.",
                nameof(strategy));
        }

        if (strategy.Requested != OperationStrategyNames.Auto &&
            !string.Equals(strategy.Requested, strategy.Used, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "A non-auto requested strategy must be the strategy actually used.",
                nameof(strategy));
        }
    }
}
