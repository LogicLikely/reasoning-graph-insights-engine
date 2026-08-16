using System.Collections.ObjectModel;
using System.Text.Json;

namespace Backend.Insights.Contracts;

public enum RunnerType
{
    CommandLine,
    LabUserInterface,
    BenchmarkDotNet,
    ApiBrowserJourney
}

public sealed record GraphRunIdentity(
    string Slug,
    string? GraphId,
    string Shape,
    long ActualNodeCount,
    long ActualEdgeCount,
    long MaximumDepth);

public sealed record DatasetRunIdentity(
    string GeneratorVersion,
    string CorpusId,
    string CorpusFingerprint,
    string TopologyFingerprint,
    string InputFingerprint,
    string DatasetInputFingerprint);

public sealed record AlgorithmRunIdentity(string Key, string SemanticIdentity);

public sealed record RunTargets(
    IReadOnlyList<string> NodeIds,
    IReadOnlyList<string> PathIds);

public sealed record SourceRevision(string GitCommitSha, bool DirtyWorktree);

public sealed record DependencyVersions(
    string DotNet,
    string Node,
    string Browser,
    string GraphMap,
    string PostgreSql,
    IReadOnlyDictionary<string, string> RelevantDependencies);

public sealed record HostEnvironment(
    string OperatingSystem,
    string Architecture,
    string Cpu,
    int LogicalCoreCount,
    long MemoryBytes);

public sealed record WarmupSampleCachePolicy(
    int WarmupIterations,
    int SampleIterations,
    string WarmupPolicy,
    string SamplePolicy,
    string JitPolicy,
    string CachePolicy);

public sealed record TimeoutCancellationPolicy(
    TimeSpan Timeout,
    string CancellationPolicy,
    bool IsolatedWorker);

public sealed record MeasurementUnitContract(
    string WallClockDuration,
    string CpuTime,
    string Memory,
    string PayloadSize,
    string Counts,
    string Density);

public sealed record RunManifest(
    Guid RunId,
    string Name,
    ExecutionOutcome Execution,
    DateTimeOffset StartedAt,
    DateTimeOffset? CompletedAt,
    RunnerType RunnerType,
    string ScenarioKey,
    string OperationKey,
    GraphRunIdentity Graph,
    DatasetRunIdentity Dataset,
    AlgorithmRunIdentity Algorithm,
    StrategySelection Strategy,
    CanonicalParameters CanonicalParameters,
    RunTargets Targets,
    SourceRevision SourceRevision,
    string BuildConfiguration,
    string BuildMode,
    DependencyVersions Dependencies,
    HostEnvironment Host,
    string EnvironmentProfile,
    WarmupSampleCachePolicy SamplingPolicy,
    TimeoutCancellationPolicy ExecutionPolicy,
    MeasurementUnitContract MeasurementUnits);

public sealed record IterationClassification(
    string IterationKind,
    string Temperature,
    string JitState,
    string CacheState);

public sealed record SampleNodeCounts(
    long? Requested,
    long? Canonical,
    long? Synthetic,
    long? Rendered);

public sealed record SampleEdgeCounts(
    long? Requested,
    long? Rendered,
    decimal? Density);

public sealed record SampleSearchCounts(
    long? Matches,
    long? CompleteRequiredAncestorUnion);

public sealed record SampleTransportMeasurements(
    long? RequestBytes,
    long? ResponseBytes,
    decimal? TimeToFirstByte,
    decimal? FullTransferDuration);

public sealed record RunSample(
    Guid RunId,
    Guid SampleId,
    string ScenarioKey,
    string OperationKey,
    string Layer,
    string Phase,
    decimal WallClockDuration,
    int Iteration,
    IterationClassification Classification,
    SampleNodeCounts NodeCounts,
    SampleEdgeCounts EdgeCounts,
    SampleSearchCounts SearchCounts,
    long? ResultCardinality,
    SampleTransportMeasurements Transport,
    RuntimeResourceMeasurements Resources,
    ExecutionOutcome Execution,
    MeasurementUnitContract MeasurementUnits);

public sealed record CompactRunOutput
{
    public CompactRunOutput(
        Guid runId,
        Guid sampleId,
        string scenarioKey,
        string operationKey,
        string algorithmSemanticIdentity,
        StrategySelection strategy,
        GraphTargetIdentifiers identifiers,
        CanonicalParameters canonicalParameters,
        ExecutionOutcome execution,
        JsonElement summary,
        JsonElement distribution,
        long totalResultCardinality,
        IReadOnlyList<JsonElement> items,
        string resultDigest,
        string? fullResultArtifactReference,
        IReadOnlyList<OrderedPathProjection> orderedPaths)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scenarioKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(operationKey);
        _ = SemanticIdentity.Parse(algorithmSemanticIdentity);

        InsightOperationRegistry.ValidateResultStrategySelection(
            operationKey,
            strategy,
            execution.Status);
        ArgumentException.ThrowIfNullOrWhiteSpace(resultDigest);
        ArgumentOutOfRangeException.ThrowIfNegative(totalResultCardinality);

        var retainedItems = items.ToArray();
        if (retainedItems.Length > OperationResultEnvelope.MaximumRetainedItems)
        {
            throw new ArgumentException(
                $"A compact output may retain at most {OperationResultEnvelope.MaximumRetainedItems} items.",
                nameof(items));
        }

        if (retainedItems.LongLength > totalResultCardinality)
        {
            throw new ArgumentException("Retained item count cannot exceed total result cardinality.", nameof(items));
        }

        RunId = runId;
        SampleId = sampleId;
        ScenarioKey = scenarioKey;
        OperationKey = operationKey;
        AlgorithmSemanticIdentity = algorithmSemanticIdentity;
        Strategy = strategy;
        Identifiers = identifiers;
        CanonicalParameters = canonicalParameters;
        Execution = execution;
        Summary = summary;
        Distribution = distribution;
        TotalResultCardinality = totalResultCardinality;
        Items = Array.AsReadOnly(retainedItems);
        ResultDigest = resultDigest;
        FullResultArtifactReference = fullResultArtifactReference;
        OrderedPaths = Array.AsReadOnly(orderedPaths.ToArray());
    }

    public Guid RunId { get; }
    public Guid SampleId { get; }
    public string ScenarioKey { get; }
    public string OperationKey { get; }
    public string AlgorithmSemanticIdentity { get; }
    public StrategySelection Strategy { get; }
    public GraphTargetIdentifiers Identifiers { get; }
    public CanonicalParameters CanonicalParameters { get; }
    public ExecutionOutcome Execution { get; }
    public JsonElement Summary { get; }
    public JsonElement Distribution { get; }
    public long TotalResultCardinality { get; }
    public IReadOnlyList<JsonElement> Items { get; }
    public string ResultDigest { get; }
    public string? FullResultArtifactReference { get; }
    public IReadOnlyList<OrderedPathProjection> OrderedPaths { get; }
}

public sealed record RunExportDigests(
    string ManifestDigest,
    string SamplesDigest,
    string OutputsDigest);

public sealed record VersionedRunExport
{
    public const string CurrentSchemaIdentity = "insights-run-export-v1";
    public const int CurrentSchemaVersion = 1;

    [System.Text.Json.Serialization.JsonConstructor]
    public VersionedRunExport(
        string schemaIdentity,
        int schemaVersion,
        RunManifest manifest,
        IReadOnlyList<RunSample> samples,
        IReadOnlyList<CompactRunOutput> outputs,
        RunExportDigests digests)
    {
        if (!string.Equals(schemaIdentity, CurrentSchemaIdentity, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"Schema identity must be '{CurrentSchemaIdentity}'.",
                nameof(schemaIdentity));
        }

        if (schemaVersion != CurrentSchemaVersion)
        {
            throw new ArgumentOutOfRangeException(
                nameof(schemaVersion),
                schemaVersion,
                $"Schema version must be {CurrentSchemaVersion}.");
        }

        SchemaIdentity = schemaIdentity;
        SchemaVersion = schemaVersion;
        InsightOperationRegistry.ValidateResultStrategySelection(
            manifest.OperationKey,
            manifest.Strategy,
            manifest.Execution.Status);
        Manifest = manifest;
        Samples = Array.AsReadOnly(samples.ToArray());
        Outputs = Array.AsReadOnly(outputs.ToArray());
        Digests = digests;
    }

    public string SchemaIdentity { get; }
    public int SchemaVersion { get; }
    public RunManifest Manifest { get; }
    public IReadOnlyList<RunSample> Samples { get; }
    public IReadOnlyList<CompactRunOutput> Outputs { get; }
    public RunExportDigests Digests { get; }
}
