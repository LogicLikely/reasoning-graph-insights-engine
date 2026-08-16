using System.Text.Json;
using Backend.Insights.Contracts;

namespace Backend.Insights.Benchmarking;

public sealed record BenchmarkRunSelection(
    string ProfileKey,
    string? ScenarioKey = null,
    string? OperationKey = null,
    string? DatasetId = null,
    JsonElement? Parameters = null,
    string? Strategy = null,
    TimeSpan? Timeout = null,
    bool Persist = false);

public sealed record BenchmarkSingleRunResult(
    BenchmarkScenarioDefinition Scenario,
    RunManifest Manifest,
    IReadOnlyList<RunSample> Samples,
    IReadOnlyList<CompactRunOutput> Outputs,
    VersionedRunExport Export,
    string ExportJson,
    VersionedRunExport DeserializedExport,
    bool WasPersisted,
    bool WasReloaded);

public sealed record BenchmarkProfileRunResult(
    BenchmarkProfileDefinition Profile,
    IReadOnlyList<BenchmarkSingleRunResult> Runs);

public sealed record BenchmarkScenarioPreparationResult(
    PreparedBenchmarkOperation Operation,
    ExecutionOutcome Execution,
    IReadOnlyList<RunSample> SetupSamples,
    GraphRunIdentity? GraphIdentity = null,
    DatasetRunIdentity? DatasetIdentity = null,
    DependencyVersions? Dependencies = null,
    string? EnvironmentProfile = null,
    RunnerType? RunnerType = null);

public interface IBenchmarkScenarioPreparer
{
    Task<BenchmarkScenarioPreparationResult> PrepareAsync(
        PreparedBenchmarkOperation operation,
        BenchmarkScenarioDefinition scenario,
        DeterministicStressGraphFixture fixture,
        TimeSpan timeout,
        CancellationToken cancellationToken);
}

public interface IBenchmarkIdentitySource
{
    Guid NewRunId();
    Guid NewSampleId();
    DateTimeOffset UtcNow();
}

public sealed class SystemBenchmarkIdentitySource : IBenchmarkIdentitySource
{
    public Guid NewRunId() => Guid.NewGuid();
    public Guid NewSampleId() => Guid.NewGuid();
    public DateTimeOffset UtcNow() => DateTimeOffset.UtcNow;
}
