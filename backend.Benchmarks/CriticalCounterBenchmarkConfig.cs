using System.Globalization;
using Backend.Insights.Analysis;
using BenchmarkDotNet.Columns;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Reports;
using BenchmarkDotNet.Running;

namespace Backend.Benchmarks;

internal sealed record CriticalCounterBenchmarkMetadata(
    string UsedStrategy,
    int CandidateCount,
    long EvaluationCount,
    decimal ThresholdLogOdds,
    long ResultCardinality);

internal static class CriticalCounterBenchmarkMetadataCatalog
{
    private static readonly IReadOnlyDictionary<string, CriticalCounterBenchmarkMetadata> ByMethod =
        new Dictionary<string, CriticalCounterBenchmarkMetadata>(StringComparer.Ordinal)
        {
            [nameof(CriticalCounterBenchmarks.Exact)] =
                new("exact", 4, 16, -1m, 1),
            [nameof(CriticalCounterBenchmarks.Greedy)] =
                new("greedy", 4, 10, -1m, 1),
            [nameof(CriticalCounterBenchmarks.AutoSelectionAndExecution)] =
                new("exact", 4, 16, -1m, 1),
            [nameof(CriticalCounterBenchmarks.AutoGreedySelectionAndExecution)] =
                new("greedy", 4, 10, -1m, 1)
        };

    public static CriticalCounterBenchmarkMetadata Resolve(string workloadMethodName)
    {
        if (!ByMethod.TryGetValue(workloadMethodName, out var metadata))
        {
            throw new InvalidOperationException(
                $"No critical-counter benchmark metadata is registered for '{workloadMethodName}'.");
        }

        return metadata;
    }

    public static void Verify(
        string workloadMethodName,
        CriticalCounterV1AnalysisResult result)
    {
        var expected = Resolve(workloadMethodName);
        if (!string.Equals(result.UsedStrategy, expected.UsedStrategy, StringComparison.Ordinal) ||
            result.CandidateCount != expected.CandidateCount ||
            result.EvaluationCount != expected.EvaluationCount ||
            result.ThresholdLogOdds != expected.ThresholdLogOdds ||
            result.TotalResultCardinality != expected.ResultCardinality)
        {
            throw new InvalidOperationException(
                $"Critical-counter benchmark metadata for '{workloadMethodName}' is stale. " +
                $"Actual: strategy={result.UsedStrategy}, candidates={result.CandidateCount}, " +
                $"evaluations={result.EvaluationCount}, threshold={result.ThresholdLogOdds}, " +
                $"results={result.TotalResultCardinality}.");
        }
    }
}

/// <summary>
/// Adds deterministic algorithm counters to every BenchmarkDotNet exporter.
/// The values are verified during GlobalSetup, so report generation never
/// executes the operation or changes the measured hot loop.
/// </summary>
public sealed class CriticalCounterBenchmarkConfig : ManualConfig
{
    public CriticalCounterBenchmarkConfig()
    {
        AddColumn(
            MetadataColumn.Text(
                "Used strategy",
                "Strategy actually selected by critical-counter-v1.",
                metadata => metadata.UsedStrategy),
            MetadataColumn.Number(
                "Candidates",
                "Eligible candidate count for the frozen input.",
                metadata => metadata.CandidateCount.ToString(CultureInfo.InvariantCulture)),
            MetadataColumn.Number(
                "Evaluations",
                "Algorithm subset-evaluation count for the frozen input.",
                metadata => metadata.EvaluationCount.ToString(CultureInfo.InvariantCulture)),
            MetadataColumn.Number(
                "Threshold (log-odds)",
                "Frozen threshold in log-odds.",
                metadata => metadata.ThresholdLogOdds.ToString(CultureInfo.InvariantCulture)),
            MetadataColumn.Number(
                "Results",
                "Logical result cardinality for the frozen input.",
                metadata => metadata.ResultCardinality.ToString(CultureInfo.InvariantCulture)));
    }

    private sealed class MetadataColumn : IColumn
    {
        private readonly Func<CriticalCounterBenchmarkMetadata, string> _value;

        private MetadataColumn(
            string columnName,
            string legend,
            bool isNumeric,
            Func<CriticalCounterBenchmarkMetadata, string> value)
        {
            ColumnName = columnName;
            Legend = legend;
            IsNumeric = isNumeric;
            _value = value;
        }

        public string Id => $"{nameof(CriticalCounterBenchmarkConfig)}.{ColumnName}";

        public string ColumnName { get; }

        public bool AlwaysShow => true;

        public ColumnCategory Category => ColumnCategory.Custom;

        public int PriorityInCategory => 0;

        public bool IsNumeric { get; }

        public UnitType UnitType => UnitType.Dimensionless;

        public string Legend { get; }

        public static MetadataColumn Text(
            string columnName,
            string legend,
            Func<CriticalCounterBenchmarkMetadata, string> value) =>
            new(columnName, legend, false, value);

        public static MetadataColumn Number(
            string columnName,
            string legend,
            Func<CriticalCounterBenchmarkMetadata, string> value) =>
            new(columnName, legend, true, value);

        public string GetValue(Summary summary, BenchmarkCase benchmarkCase) =>
            _value(CriticalCounterBenchmarkMetadataCatalog.Resolve(
                benchmarkCase.Descriptor.WorkloadMethod.Name));

        public string GetValue(
            Summary summary,
            BenchmarkCase benchmarkCase,
            SummaryStyle style) =>
            GetValue(summary, benchmarkCase);

        public bool IsDefault(Summary summary, BenchmarkCase benchmarkCase) => false;

        public bool IsAvailable(Summary summary) => true;
    }
}
