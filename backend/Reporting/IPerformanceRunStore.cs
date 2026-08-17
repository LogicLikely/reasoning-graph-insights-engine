namespace Backend.Reporting;

public interface IPerformanceRunStore
{
    Task<PerformanceReportDocument> ReadAsync(
        CancellationToken cancellationToken = default);

    Task<PerformanceRunRecord> AppendAsync(
        PerformanceRunRecord run,
        CancellationToken cancellationToken = default);
}
