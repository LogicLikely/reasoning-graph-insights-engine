namespace Backend.Reporting;

public interface IPerformanceRunStore
{
    Task<PerformanceRunRecord> AppendAsync(
        PerformanceRunRecord run,
        CancellationToken cancellationToken = default);
}
