namespace Backend.Reporting;

public sealed class PerformanceReportDocument
{
    public const int CurrentSchemaVersion = 2;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;

    public List<PerformanceBenchmarkSet> BenchmarkSets { get; init; } = [];

    public List<PerformanceRunRecord> Runs { get; init; } = [];
}

public sealed record PerformanceBenchmarkSet
{
    public required string Id { get; init; }

    public required string Name { get; init; }

    public required DateTimeOffset CreatedAtUtc { get; init; }
}
