namespace Backend.Reporting;

public sealed class PerformanceReportDocument
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;

    public List<PerformanceRunRecord> Runs { get; init; } = [];
}
