namespace Backend.Reporting;

/// <summary>
/// Keeps calculation-focused tests and direct service construction from writing
/// a report file. Production dependency injection uses JsonPerformanceRunStore.
/// </summary>
public sealed class NullPerformanceRunStore : IPerformanceRunStore
{
    public static NullPerformanceRunStore Instance { get; } = new();

    private NullPerformanceRunStore()
    {
    }

    public Task<PerformanceReportDocument> ReadAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new PerformanceReportDocument());
    }

    public Task<PerformanceRunRecord> AppendAsync(
        PerformanceRunRecord run,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(run);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(run with { RunNumber = 0 });
    }

    public Task<PerformanceBenchmarkSet> CreateBenchmarkSetAsync(
        string name,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new PerformanceBenchmarkSet
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = name.Trim(),
            CreatedAtUtc = DateTimeOffset.UtcNow
        });
    }
}
