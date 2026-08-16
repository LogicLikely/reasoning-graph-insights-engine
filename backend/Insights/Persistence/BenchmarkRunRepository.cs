using System.Text.Json;
using System.Text.RegularExpressions;
using Backend.Data;
using Backend.Insights.Contracts;
using Dapper;

namespace Backend.Insights.Persistence;

public sealed class BenchmarkRunRepository : IBenchmarkRunRepository
{
    private const string InsertRunSql = """
        INSERT INTO benchmark.runs (
            run_id,
            name,
            status,
            failure_kind,
            started_at,
            completed_at,
            runner_type,
            profile_key,
            scenario_key,
            operation_key,
            graph_slug,
            dataset_input_fingerprint,
            algorithm_semantic_identity,
            parameter_digest,
            environment_profile,
            build_mode,
            actual_strategy,
            sample_mode,
            measurement_units,
            manifest_json
        ) VALUES (
            @RunId,
            @Name,
            @Status,
            @FailureKind,
            @StartedAt,
            @CompletedAt,
            @RunnerType,
            @ProfileKey,
            @ScenarioKey,
            @OperationKey,
            @GraphSlug,
            @DatasetInputFingerprint,
            @AlgorithmSemanticIdentity,
            @ParameterDigest,
            @EnvironmentProfile,
            @BuildMode,
            @ActualStrategy,
            @SampleMode,
            @MeasurementUnitsJson::jsonb,
            @ManifestJson::jsonb
        );
        """;

    private const string UpdateLifecycleSql = """
        UPDATE benchmark.runs
        SET
            status = CASE WHEN status IN (
                'succeeded', 'failed', 'timed-out', 'cancelled', 'crashed', 'skipped'
            ) THEN status ELSE @Status END,
            failure_kind = CASE WHEN status IN (
                'succeeded', 'failed', 'timed-out', 'cancelled', 'crashed', 'skipped'
            ) THEN failure_kind ELSE @FailureKind END,
            completed_at = CASE WHEN status IN (
                'succeeded', 'failed', 'timed-out', 'cancelled', 'crashed', 'skipped'
            ) THEN completed_at ELSE @CompletedAt::timestamp with time zone END,
            manifest_json = CASE WHEN status IN (
                'succeeded', 'failed', 'timed-out', 'cancelled', 'crashed', 'skipped'
            ) THEN manifest_json ELSE jsonb_set(
                    jsonb_set(
                        manifest_json,
                        '{execution}',
                        @ExecutionJson::jsonb,
                        false
                    ),
                    '{completedAt}',
                    @CompletedAtJson::jsonb,
                    false
                ) END,
            updated_at = CASE WHEN status IN (
                'succeeded', 'failed', 'timed-out', 'cancelled', 'crashed', 'skipped'
            ) THEN updated_at ELSE now() END
        WHERE run_id = @RunId
          AND (
              @CompletedAt::timestamp with time zone IS NULL
              OR @CompletedAt::timestamp with time zone >= started_at
          )
          AND (
              (
                  status = 'queued'
                  AND @Status IN (
                      'running',
                      'succeeded',
                      'failed',
                      'timed-out',
                      'cancelled',
                      'crashed',
                      'skipped'
                  )
              )
              OR (
                  status = 'running'
                  AND @Status IN (
                      'succeeded',
                      'failed',
                      'timed-out',
                      'cancelled',
                      'crashed',
                      'skipped'
                  )
              )
              OR (
                  status IN (
                      'succeeded',
                      'failed',
                      'timed-out',
                      'cancelled',
                      'crashed',
                      'skipped'
                  )
                  AND status = @Status
                  AND manifest_json->'execution' = @ExecutionJson::jsonb
                  AND manifest_json->'completedAt' = @CompletedAtJson::jsonb
              )
          );
        """;

    private const string InsertSampleSql = """
        INSERT INTO benchmark.samples (
            run_id,
            sample_id,
            scenario_key,
            operation_key,
            iteration,
            layer,
            phase,
            timing_boundary_provenance,
            wall_clock_duration,
            status,
            failure_kind,
            sample_json
        )
        SELECT
            @RunId,
            @SampleId,
            @ScenarioKey,
            @OperationKey,
            @Iteration,
            @Layer,
            @Phase,
            @TimingBoundaryProvenance,
            @WallClockDuration,
            @Status,
            @FailureKind,
            @SampleJson::jsonb
        FROM benchmark.runs AS run
        WHERE run.run_id = @RunId
          AND run.scenario_key = @ScenarioKey
          AND run.operation_key = @OperationKey
          AND run.measurement_units = @MeasurementUnitsJson::jsonb;
        """;

    private const string InsertOutputSql = """
        INSERT INTO benchmark.outputs (
            run_id,
            sample_id,
            scenario_key,
            operation_key,
            algorithm_semantic_identity,
            status,
            failure_kind,
            total_result_cardinality,
            result_digest,
            output_json
        )
        SELECT
            @RunId,
            @SampleId,
            @ScenarioKey,
            @OperationKey,
            @AlgorithmSemanticIdentity,
            @Status,
            @FailureKind,
            @TotalResultCardinality,
            @ResultDigest,
            @OutputJson::jsonb
        FROM benchmark.runs AS run
        WHERE run.run_id = @RunId
          AND run.scenario_key = @ScenarioKey
          AND run.operation_key = @OperationKey
          AND run.graph_slug = @GraphSlug
          AND run.algorithm_semantic_identity = @AlgorithmSemanticIdentity
          AND run.parameter_digest = @ParameterDigest
          AND run.manifest_json->'strategy' = @StrategyJson::jsonb;
        """;

    private const string SelectRunSql = """
        SELECT manifest_json::text AS "ManifestJson"
        FROM benchmark.runs
        WHERE run_id = @RunId;
        """;

    private const string SelectSamplesSql = """
        SELECT sample_json::text AS "PayloadJson"
        FROM benchmark.samples
        WHERE run_id = @RunId
        ORDER BY entry_id;
        """;

    private const string SelectOutputsSql = """
        SELECT output_json::text AS "PayloadJson"
        FROM benchmark.outputs
        WHERE run_id = @RunId
        ORDER BY entry_id;
        """;

    private static readonly JsonSerializerOptions SerializerOptions =
        CanonicalJson.CreateSerializerOptions();

    private static readonly Regex Sha256DigestPattern = new(
        "^sha256:[0-9a-f]{64}$",
        RegexOptions.CultureInvariant);

    private readonly DbConnectionFactory _dbConnectionFactory;

    public BenchmarkRunRepository(DbConnectionFactory dbConnectionFactory)
    {
        _dbConnectionFactory = dbConnectionFactory;
    }

    public async Task CreateRunAsync(
        ExplicitBenchmarkRunIntent intent,
        RunManifest manifest,
        CancellationToken cancellationToken = default)
    {
        RequireMatchingIntent(intent, manifest.RunId);
        ValidateManifest(manifest);

        using var connection = _dbConnectionFactory.CreateConnection();
        connection.Open();

        var affectedRows = await connection.ExecuteAsync(new CommandDefinition(
            InsertRunSql,
            new
            {
                manifest.RunId,
                manifest.Name,
                Status = EnumToken(manifest.Execution.Status),
                FailureKind = NullableEnumToken(manifest.Execution.Failure?.Kind),
                StartedAt = manifest.StartedAt.UtcDateTime,
                CompletedAt = manifest.CompletedAt?.UtcDateTime,
                RunnerType = EnumToken(manifest.RunnerType),
                manifest.ProfileKey,
                manifest.ScenarioKey,
                manifest.OperationKey,
                GraphSlug = manifest.Graph.Slug,
                manifest.Dataset.DatasetInputFingerprint,
                AlgorithmSemanticIdentity = manifest.Algorithm.SemanticIdentity,
                ParameterDigest = manifest.CanonicalParameters.Digest,
                manifest.EnvironmentProfile,
                manifest.BuildMode,
                ActualStrategy = manifest.Strategy.Used,
                manifest.SamplingPolicy.SampleMode,
                MeasurementUnitsJson = SerializeJson(manifest.MeasurementUnits),
                ManifestJson = SerializeJson(manifest)
            },
            cancellationToken: cancellationToken));

        EnsureOneRowWasWritten(affectedRows, "The explicit benchmark run was not created.");
    }

    public async Task UpdateLifecycleAsync(
        ExplicitBenchmarkRunIntent intent,
        ExecutionOutcome execution,
        DateTimeOffset? completedAt,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(intent);
        ArgumentNullException.ThrowIfNull(execution);
        ValidateLifecycle(execution, completedAt);

        using var connection = _dbConnectionFactory.CreateConnection();
        connection.Open();

        var affectedRows = await connection.ExecuteAsync(new CommandDefinition(
            UpdateLifecycleSql,
            new
            {
                intent.RunId,
                Status = EnumToken(execution.Status),
                FailureKind = NullableEnumToken(execution.Failure?.Kind),
                CompletedAt = completedAt?.UtcDateTime,
                ExecutionJson = SerializeJson(execution),
                CompletedAtJson = SerializeJson(completedAt)
            },
            cancellationToken: cancellationToken));

        EnsureOneRowWasWritten(
            affectedRows,
            "The benchmark run does not exist, its completion time is invalid, or the lifecycle transition is not allowed.");
    }

    public async Task AppendSampleAsync(
        ExplicitBenchmarkRunIntent intent,
        RunSample sample,
        CancellationToken cancellationToken = default)
    {
        RequireMatchingIntent(intent, sample.RunId);
        ValidateSample(sample);

        using var connection = _dbConnectionFactory.CreateConnection();
        connection.Open();

        var affectedRows = await connection.ExecuteAsync(new CommandDefinition(
            InsertSampleSql,
            new
            {
                sample.RunId,
                sample.SampleId,
                sample.ScenarioKey,
                sample.OperationKey,
                sample.Iteration,
                sample.Layer,
                sample.Phase,
                TimingBoundaryProvenance = EnumToken(sample.TimingBoundaryProvenance),
                sample.WallClockDuration,
                Status = EnumToken(sample.Execution.Status),
                FailureKind = NullableEnumToken(sample.Execution.Failure?.Kind),
                MeasurementUnitsJson = SerializeJson(sample.MeasurementUnits),
                SampleJson = SerializeJson(sample)
            },
            cancellationToken: cancellationToken));

        EnsureOneRowWasWritten(
            affectedRows,
            "The sample identity or measurement units do not match the explicit benchmark run.");
    }

    public async Task AppendOutputAsync(
        ExplicitBenchmarkRunIntent intent,
        CompactRunOutput output,
        CancellationToken cancellationToken = default)
    {
        RequireMatchingIntent(intent, output.RunId);
        ValidateOutput(output);

        using var connection = _dbConnectionFactory.CreateConnection();
        connection.Open();

        var affectedRows = await connection.ExecuteAsync(new CommandDefinition(
            InsertOutputSql,
            new
            {
                output.RunId,
                output.SampleId,
                output.ScenarioKey,
                output.OperationKey,
                output.AlgorithmSemanticIdentity,
                Status = EnumToken(output.Execution.Status),
                FailureKind = NullableEnumToken(output.Execution.Failure?.Kind),
                output.TotalResultCardinality,
                output.ResultDigest,
                GraphSlug = output.Identifiers.GraphSlug,
                ParameterDigest = output.CanonicalParameters.Digest,
                StrategyJson = SerializeJson(output.Strategy),
                OutputJson = SerializeJson(output)
            },
            cancellationToken: cancellationToken));

        EnsureOneRowWasWritten(
            affectedRows,
            "The compact output identity does not match the explicit benchmark run.");
    }

    public async Task<BenchmarkRunSnapshot?> GetSnapshotAsync(
        Guid runId,
        CancellationToken cancellationToken = default)
    {
        if (runId == Guid.Empty)
        {
            throw new ArgumentException("A benchmark run ID cannot be empty.", nameof(runId));
        }

        using var connection = _dbConnectionFactory.CreateConnection();
        connection.Open();

        var runRow = await connection.QuerySingleOrDefaultAsync<RunPayloadRow>(
            new CommandDefinition(
                SelectRunSql,
                new { RunId = runId },
                cancellationToken: cancellationToken));
        if (runRow is null)
        {
            return null;
        }

        var sampleRows = await connection.QueryAsync<PayloadRow>(new CommandDefinition(
            SelectSamplesSql,
            new { RunId = runId },
            cancellationToken: cancellationToken));
        var outputRows = await connection.QueryAsync<PayloadRow>(new CommandDefinition(
            SelectOutputsSql,
            new { RunId = runId },
            cancellationToken: cancellationToken));

        var manifest = Deserialize<RunManifest>(runRow.ManifestJson);
        ValidateManifest(manifest);
        if (manifest.RunId != runId)
        {
            throw new InvalidDataException("Stored benchmark manifest run ID does not match its row identity.");
        }

        var samples = sampleRows
            .Select(row => Deserialize<RunSample>(row.PayloadJson))
            .ToArray();
        foreach (var sample in samples)
        {
            ValidateSample(sample);
        }

        var outputs = outputRows
            .Select(row => Deserialize<CompactRunOutput>(row.PayloadJson))
            .ToArray();
        foreach (var output in outputs)
        {
            ValidateOutput(output);
        }

        return new BenchmarkRunSnapshot(manifest, samples, outputs);
    }

    private static void ValidateManifest(RunManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        if (manifest.RunId == Guid.Empty)
        {
            throw new ArgumentException("A benchmark manifest run ID cannot be empty.", nameof(manifest));
        }

        RequireText(manifest.Name, "Run name");
        RequireText(manifest.ProfileKey, "Benchmark profile key");
        RequireText(manifest.ScenarioKey, "Scenario key");
        RequireText(manifest.OperationKey, "Operation key");
        RequireText(manifest.Graph.Slug, "Graph slug");
        RequireText(manifest.EnvironmentProfile, "Environment profile");
        RequireText(manifest.BuildMode, "Build mode");
        ArgumentNullException.ThrowIfNull(manifest.SamplingPolicy);
        if (!RunSampleModeTokens.IsKnown(manifest.SamplingPolicy.SampleMode))
        {
            throw new ArgumentException(
                $"Unknown run-level sample mode '{manifest.SamplingPolicy.SampleMode}'.",
                nameof(manifest));
        }
        ValidateMeasurementUnits(manifest.MeasurementUnits, "manifest");
        _ = InsightOperationRegistry.Get(manifest.OperationKey);
        _ = SemanticIdentity.Parse(manifest.Algorithm.SemanticIdentity);
        if (!string.Equals(manifest.OperationKey, manifest.Algorithm.Key, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "Manifest algorithm key must match the operation key.",
                nameof(manifest));
        }

        InsightOperationRegistry.ValidateResultStrategySelection(
            manifest.OperationKey,
            manifest.Strategy,
            manifest.Execution.Status);
        ValidateLifecycle(manifest.Execution, manifest.CompletedAt);
        if (manifest.CompletedAt < manifest.StartedAt)
        {
            throw new ArgumentException(
                "Benchmark run completion time cannot precede its start time.",
                nameof(manifest));
        }
        ValidateCanonicalParameters(manifest.CanonicalParameters, "manifest");
        RequireSha256(manifest.Dataset.CorpusFingerprint, "Corpus fingerprint");
        RequireSha256(manifest.Dataset.TopologyFingerprint, "Topology fingerprint");
        RequireSha256(manifest.Dataset.InputFingerprint, "Input fingerprint");
        RequireSha256(manifest.Dataset.DatasetInputFingerprint, "Dataset/input fingerprint");
    }

    private static void ValidateSample(RunSample sample)
    {
        ArgumentNullException.ThrowIfNull(sample);
        if (sample.RunId == Guid.Empty || sample.SampleId == Guid.Empty)
        {
            throw new ArgumentException("Sample run and sample IDs cannot be empty.", nameof(sample));
        }

        RequireText(sample.ScenarioKey, "Sample scenario key");
        RequireText(sample.OperationKey, "Sample operation key");
        RequireText(sample.Layer, "Sample layer");
        RequireText(sample.Phase, "Sample phase");
        _ = InsightOperationRegistry.Get(sample.OperationKey);
        ArgumentOutOfRangeException.ThrowIfNegative(sample.Iteration);
        ArgumentOutOfRangeException.ThrowIfNegative(sample.WallClockDuration);
        if (!Enum.IsDefined(sample.TimingBoundaryProvenance))
        {
            throw new ArgumentOutOfRangeException(
                nameof(sample),
                sample.TimingBoundaryProvenance,
                "Unknown sample timing-boundary provenance.");
        }

        // Phase 4 writes shared canonical tokens, while pre-Phase 4 v1 rows
        // may carry other non-empty labels. Preserve those raw labels as
        // incompatible standalone buckets rather than making history unreadable.
        ArgumentNullException.ThrowIfNull(sample.Classification);
        RequireText(sample.Classification.IterationKind, "Sample iteration kind");
        RequireText(sample.Classification.Temperature, "Sample temperature");
        RequireText(sample.Classification.JitState, "Sample JIT state");
        RequireText(sample.Classification.CacheState, "Sample cache state");

        ValidateMeasurementUnits(sample.MeasurementUnits, "sample");
        ArgumentNullException.ThrowIfNull(sample.NodeCounts);
        RequireNonnegative(sample.NodeCounts.Requested, "Requested node count");
        RequireNonnegative(sample.NodeCounts.Canonical, "Canonical node count");
        RequireNonnegative(sample.NodeCounts.Synthetic, "Synthetic node count");
        RequireNonnegative(sample.NodeCounts.Rendered, "Rendered node count");
        ArgumentNullException.ThrowIfNull(sample.EdgeCounts);
        RequireNonnegative(sample.EdgeCounts.Requested, "Requested edge count");
        RequireNonnegative(sample.EdgeCounts.Rendered, "Rendered edge count");
        RequireNonnegative(sample.EdgeCounts.Density, "Edge density");
        ArgumentNullException.ThrowIfNull(sample.SearchCounts);
        RequireNonnegative(sample.SearchCounts.Matches, "Search match count");
        RequireNonnegative(
            sample.SearchCounts.CompleteRequiredAncestorUnion,
            "Complete required-ancestor-union count");
        RequireNonnegative(sample.ResultCardinality, "Result cardinality");
        ArgumentNullException.ThrowIfNull(sample.Transport);
        RequireNonnegative(sample.Transport.RequestBytes, "Request bytes");
        RequireNonnegative(sample.Transport.ResponseBytes, "Response bytes");
        RequireNonnegative(sample.Transport.TimeToFirstByte, "Time to first byte");
        RequireNonnegative(sample.Transport.FullTransferDuration, "Full transfer duration");
        ArgumentNullException.ThrowIfNull(sample.Resources);
        RequireNonnegative(sample.Resources.AllocatedBytes, "Allocated bytes");
        RequireNonnegative(sample.Resources.Generation0Collections, "Generation 0 collections");
        RequireNonnegative(sample.Resources.Generation1Collections, "Generation 1 collections");
        RequireNonnegative(sample.Resources.Generation2Collections, "Generation 2 collections");
        RequireNonnegative(sample.Resources.CpuTime, "CPU time");
        RequireText(sample.Resources.CpuTimeUnit, "Sample CPU-time unit");

        ValidateOperationCounters(sample.OperationCounters);
    }

    private static void ValidateOperationCounters(SampleOperationCounters? counters)
    {
        if (counters is null)
        {
            return;
        }

        ArgumentOutOfRangeException.ThrowIfNegative(counters.CandidateCount ?? 0);
        ArgumentOutOfRangeException.ThrowIfNegative(counters.VisitedNodeCount ?? 0);
        ArgumentOutOfRangeException.ThrowIfNegative(counters.VisitedEdgeCount ?? 0);
        ArgumentOutOfRangeException.ThrowIfNegative(counters.AlgorithmIterationCount ?? 0);
        ArgumentOutOfRangeException.ThrowIfNegative(counters.CancellationCheckCount ?? 0);
    }

    private static void ValidateMeasurementUnits(MeasurementUnitContract units, string owner)
    {
        ArgumentNullException.ThrowIfNull(units);
        RequireText(units.WallClockDuration, $"{owner} wall-clock-duration unit");
        RequireText(units.CpuTime, $"{owner} CPU-time unit");
        RequireText(units.Memory, $"{owner} memory unit");
        RequireText(units.PayloadSize, $"{owner} payload-size unit");
        RequireText(units.Counts, $"{owner} count unit");
        RequireText(units.Density, $"{owner} density unit");
    }

    private static void RequireNonnegative(long? value, string fieldName)
    {
        if (value < 0)
        {
            throw new ArgumentOutOfRangeException(fieldName, value, $"{fieldName} cannot be negative.");
        }
    }

    private static void RequireNonnegative(int? value, string fieldName)
    {
        if (value < 0)
        {
            throw new ArgumentOutOfRangeException(fieldName, value, $"{fieldName} cannot be negative.");
        }
    }

    private static void RequireNonnegative(decimal? value, string fieldName)
    {
        if (value < 0)
        {
            throw new ArgumentOutOfRangeException(fieldName, value, $"{fieldName} cannot be negative.");
        }
    }

    private static void ValidateOutput(CompactRunOutput output)
    {
        ArgumentNullException.ThrowIfNull(output);
        if (output.RunId == Guid.Empty || output.SampleId == Guid.Empty)
        {
            throw new ArgumentException("Output run and sample IDs cannot be empty.", nameof(output));
        }

        _ = InsightOperationRegistry.Get(output.OperationKey);
        _ = SemanticIdentity.Parse(output.AlgorithmSemanticIdentity);
        ValidateCanonicalParameters(output.CanonicalParameters, "output");
        RequireSha256(output.ResultDigest, "Result digest");
    }

    private static void ValidateLifecycle(
        ExecutionOutcome execution,
        DateTimeOffset? completedAt)
    {
        var isTerminal = execution.Status is
            ExecutionStatus.Succeeded or
            ExecutionStatus.Failed or
            ExecutionStatus.TimedOut or
            ExecutionStatus.Cancelled or
            ExecutionStatus.Crashed or
            ExecutionStatus.Skipped;
        if (isTerminal != completedAt.HasValue)
        {
            throw new ArgumentException(
                "Terminal benchmark runs require a completion time and nonterminal runs forbid one.",
                nameof(completedAt));
        }
    }

    private static void ValidateCanonicalParameters(
        CanonicalParameters parameters,
        string owner)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        RequireSha256(parameters.Digest, $"{owner} canonical-parameter digest");
        var computedDigest = CanonicalJson.ComputeSha256(parameters.Value);
        if (!string.Equals(computedDigest, parameters.Digest, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"The {owner} canonical-parameter digest does not match its value.",
                nameof(parameters));
        }
    }

    private static void RequireMatchingIntent(
        ExplicitBenchmarkRunIntent? intent,
        Guid payloadRunId)
    {
        ArgumentNullException.ThrowIfNull(intent);
        if (intent.RunId != payloadRunId)
        {
            throw new InvalidOperationException(
                "The explicit benchmark-run intent does not match the payload run ID.");
        }
    }

    private static void RequireText(string? value, string fieldName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException($"{fieldName} cannot be empty.", fieldName);
        }
    }

    private static void RequireSha256(string? value, string fieldName)
    {
        if (value is null || !Sha256DigestPattern.IsMatch(value))
        {
            throw new ArgumentException(
                $"{fieldName} must be a lowercase canonical SHA-256 digest.",
                fieldName);
        }
    }

    // Persist strict typed JSON rather than canonical digest bytes. Canonical
    // JSON may choose an exponent spelling for mathematically integral values
    // (for example 1000 -> 1e3), which System.Text.Json intentionally rejects
    // when reading an Int32/Int64 contract property. PostgreSQL jsonb provides
    // logical JSON fidelity; canonicalization is applied only when computing an
    // export or digest after the typed payload has been read.
    private static string SerializeJson<T>(T value)
        => JsonSerializer.Serialize(value, SerializerOptions);

    private static T Deserialize<T>(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            throw new InvalidDataException($"Stored {typeof(T).Name} JSON is empty.");
        }

        return JsonSerializer.Deserialize<T>(json, SerializerOptions)
            ?? throw new InvalidDataException($"Stored {typeof(T).Name} JSON deserialized to null.");
    }

    private static string EnumToken<TEnum>(TEnum value)
        where TEnum : struct, Enum
    {
        var element = JsonSerializer.SerializeToElement(value, SerializerOptions);
        return element.GetString()
            ?? throw new InvalidOperationException($"Could not serialize {typeof(TEnum).Name}.");
    }

    private static string? NullableEnumToken<TEnum>(TEnum? value)
        where TEnum : struct, Enum
        => value.HasValue ? EnumToken(value.Value) : null;

    private static void EnsureOneRowWasWritten(int affectedRows, string message)
    {
        if (affectedRows != 1)
        {
            throw new InvalidOperationException(message);
        }
    }

    private sealed class RunPayloadRow
    {
        public string ManifestJson { get; set; } = string.Empty;
    }

    private sealed class PayloadRow
    {
        public string PayloadJson { get; set; } = string.Empty;
    }
}
