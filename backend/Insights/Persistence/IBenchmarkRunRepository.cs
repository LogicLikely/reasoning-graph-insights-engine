using Backend.Insights.Contracts;

namespace Backend.Insights.Persistence;

public interface IBenchmarkRunRepository
{
    Task CreateRunAsync(
        ExplicitBenchmarkRunIntent intent,
        RunManifest manifest,
        CancellationToken cancellationToken = default);

    Task UpdateLifecycleAsync(
        ExplicitBenchmarkRunIntent intent,
        ExecutionOutcome execution,
        DateTimeOffset? completedAt,
        CancellationToken cancellationToken = default);

    Task AppendSampleAsync(
        ExplicitBenchmarkRunIntent intent,
        RunSample sample,
        CancellationToken cancellationToken = default);

    Task AppendOutputAsync(
        ExplicitBenchmarkRunIntent intent,
        CompactRunOutput output,
        CancellationToken cancellationToken = default);

    Task<BenchmarkRunSnapshot?> GetSnapshotAsync(
        Guid runId,
        CancellationToken cancellationToken = default);
}
