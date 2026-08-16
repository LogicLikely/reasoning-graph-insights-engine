using System.Runtime.InteropServices;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Backend.Insights.Contracts;
using Backend.Insights.Export;
using Backend.Insights.Measurement;
using Backend.Insights.Persistence;

namespace Backend.Insights.Benchmarking;

public interface ISerialBenchmarkRunner
{
    Task<BenchmarkProfileRunResult> RunAsync(
        BenchmarkRunSelection selection,
        CancellationToken cancellationToken = default);
}

public sealed class SerialBenchmarkRunner : ISerialBenchmarkRunner
{
    private readonly SemaphoreSlim _serialGate = new(1, 1);
    private readonly IBenchmarkOperationExecutor _executor;
    private readonly IBenchmarkRunRepository? _repository;
    private readonly RunExportService _exportService;
    private readonly IBenchmarkIdentitySource _identitySource;
    private readonly SourceRevision _sourceRevision;

    public SerialBenchmarkRunner(
        IBenchmarkOperationExecutor executor,
        IBenchmarkRunRepository? repository = null,
        RunExportService? exportService = null,
        IBenchmarkIdentitySource? identitySource = null,
        ISourceRevisionProvider? sourceRevisionProvider = null)
    {
        _executor = executor ?? throw new ArgumentNullException(nameof(executor));
        _repository = repository;
        _exportService = exportService ?? new RunExportService();
        _identitySource = identitySource ?? new SystemBenchmarkIdentitySource();
        // Resolve once, before any run/setup boundary, so every case in a
        // serial profile has the same honest source identity.
        _sourceRevision = (sourceRevisionProvider ?? new GitSourceRevisionProvider())
            .GetSourceRevision();
    }

    public async Task<BenchmarkProfileRunResult> RunAsync(
        BenchmarkRunSelection selection,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(selection);
        var profile = BenchmarkProfiles.Get(selection.ProfileKey);
        var scenarios = ResolveScenarios(selection);
        var results = new List<BenchmarkSingleRunResult>(scenarios.Count);
        foreach (var scenario in scenarios)
        {
            if (cancellationToken.IsCancellationRequested && results.Count > 0)
            {
                break;
            }

            cancellationToken.ThrowIfCancellationRequested();
            results.Add(await RunOneAsync(profile, scenario, selection, cancellationToken));
            if (results[^1].Manifest.Execution.Status == ExecutionStatus.Cancelled)
            {
                break;
            }
        }

        return new BenchmarkProfileRunResult(profile, results.AsReadOnly());
    }

    private async Task<BenchmarkSingleRunResult> RunOneAsync(
        BenchmarkProfileDefinition profile,
        BenchmarkScenarioDefinition scenario,
        BenchmarkRunSelection selection,
        CancellationToken cancellationToken)
    {
        await _serialGate.WaitAsync(cancellationToken);
        try
        {
            var startedAt = _identitySource.UtcNow();
            var runId = _identitySource.NewRunId();
            var fixtureSampleId = _identitySource.NewSampleId();
            var operationSampleId = _identitySource.NewSampleId();
            var persistenceSampleId = _identitySource.NewSampleId();
            var exportSampleId = _identitySource.NewSampleId();
            var fixtureStarted = Stopwatch.GetTimestamp();
            var fixture = DeterministicStressGraphFixtureFactory.Create(scenario.DatasetId, cancellationToken);
            PreparedBenchmarkOperation? operation = null;
            ExecutionOutcome? preparationFailure = null;
            StrategySelection strategy;
            string? targetNodeId;
            if (scenario.IsSkipped)
            {
                strategy = new StrategySelection(scenario.RequestedStrategy, null);
                targetNodeId = null;
            }
            else
            {
                try
                {
                    operation = BenchmarkOperationRequestFactory.Create(
                        scenario, fixture, scenario.Parameters, runId, operationSampleId, cancellationToken);
                    strategy = operation.Strategy;
                    targetNodeId = operation.TargetNodeId;
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    strategy = new StrategySelection(scenario.RequestedStrategy, null);
                    targetNodeId = null;
                    preparationFailure = BenchmarkOperationExecutor.Failure(
                        ExecutionStatus.Cancelled,
                        FailureKind.Cancellation,
                        "benchmark-preparation-cancelled",
                        "Benchmark request preparation was cancelled.");
                }
                catch (Exception exception) when (
                    exception is ArgumentException or InvalidOperationException or JsonException)
                {
                    strategy = new StrategySelection(scenario.RequestedStrategy, null);
                    targetNodeId = null;
                    preparationFailure = ExecutionOutcome.ValidationFailed([
                        new ValidationFailure(
                            "parameters",
                            "benchmark-preparation-invalid",
                            "Benchmark request preparation failed validation.")
                    ]);
                }
            }
            var fixtureDuration = ElapsedMilliseconds(fixtureStarted);

            var queuedManifest = CreateManifest(
                profile, scenario, fixture, runId, startedAt, strategy, targetNodeId,
                selection.Timeout ?? profile.DefaultTimeout,
                new ExecutionOutcome(ExecutionStatus.Queued), null,
                _sourceRevision);
            var intent = ExplicitBenchmarkRunIntent.ForRun(runId);
            var persist = selection.Persist;
            if (persist && _repository is null)
            {
                throw new InvalidOperationException("Persistence was requested but no benchmark repository is configured.");
            }

            decimal persistenceDuration = 0;
            async Task ObservePersistenceAsync(Func<Task> action)
            {
                var persistenceStarted = Stopwatch.GetTimestamp();
                try
                {
                    await action();
                }
                finally
                {
                    persistenceDuration += ElapsedMilliseconds(persistenceStarted);
                }
            }

            if (persist)
            {
                await ObservePersistenceAsync(() => _repository!.CreateRunAsync(
                    intent,
                    queuedManifest,
                    CancellationToken.None));
            }

            ExecutionOutcome terminal;
            IReadOnlyList<RunSample> samples;
            IReadOnlyList<CompactRunOutput> outputs;
            if (scenario.SkipReason is not null)
            {
                terminal = BenchmarkOperationExecutor.Failure(
                    ExecutionStatus.Skipped,
                    FailureKind.Skip,
                    scenario.SkipReason.Code,
                    scenario.SkipReason.Message);
                samples = [CreateFixtureSample(runId, fixtureSampleId, scenario, fixture, fixtureDuration)];
                outputs = [];
            }
            else if (preparationFailure is not null)
            {
                if (persist)
                {
                    await ObservePersistenceAsync(() => _repository!.UpdateLifecycleAsync(
                        intent, new ExecutionOutcome(ExecutionStatus.Running), null, CancellationToken.None));
                }

                terminal = preparationFailure;
                samples = [CreateFixtureSample(runId, fixtureSampleId, scenario, fixture, fixtureDuration)];
                outputs = [];
            }
            else
            {
                if (persist)
                {
                    await ObservePersistenceAsync(() => _repository!.UpdateLifecycleAsync(
                        intent, new ExecutionOutcome(ExecutionStatus.Running), null, CancellationToken.None));
                }

                BenchmarkOperationExecutionResult execution;
                try
                {
                    execution = await _executor.ExecuteAsync(
                        operation!, scenario, fixture, profile,
                        selection.Timeout ?? profile.DefaultTimeout,
                        cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    execution = new BenchmarkOperationExecutionResult(
                        BenchmarkOperationExecutor.Failure(
                            ExecutionStatus.Cancelled,
                            FailureKind.Cancellation,
                            "benchmark-runner-cancelled",
                            "The benchmark runner was cancelled."),
                        [],
                        []);
                }
                catch (Exception exception)
                {
                    execution = new BenchmarkOperationExecutionResult(
                        BenchmarkOperationExecutor.Failure(
                            ExecutionStatus.Failed,
                            FailureKind.Execution,
                            "benchmark-executor-failed",
                            "The benchmark executor failed unexpectedly.",
                            exception.GetType().FullName),
                        [],
                        []);
                }
                terminal = execution.Execution;
                samples = [CreateFixtureSample(runId, fixtureSampleId, scenario, fixture, fixtureDuration), .. execution.Samples];
                outputs = execution.Outputs;

            }

            var completedAt = _identitySource.UtcNow();
            if (persist)
            {
                try
                {
                    foreach (var sample in samples)
                    {
                        await ObservePersistenceAsync(() =>
                            _repository!.AppendSampleAsync(intent, sample, CancellationToken.None));
                    }

                    foreach (var output in outputs)
                    {
                        await ObservePersistenceAsync(() =>
                            _repository!.AppendOutputAsync(intent, output, CancellationToken.None));
                    }

                    var persistenceSample = CreateOrchestrationSample(
                        runId,
                        persistenceSampleId,
                        scenario,
                        fixture,
                        InsightMeasurementPhases.Persistence,
                        persistenceDuration,
                        TimingBoundaryProvenance.DirectlyInstrumented);
                    await _repository!.AppendSampleAsync(intent, persistenceSample, CancellationToken.None);
                }
                catch (Exception exception)
                {
                    terminal = BenchmarkOperationExecutor.Failure(
                        ExecutionStatus.Failed,
                        FailureKind.Execution,
                        "benchmark-persistence-failed",
                        "Benchmark evidence persistence failed.",
                        exception.GetType().FullName);
                }

                // Terminal evidence must survive a caller cancellation observed by the operation.
                await _repository!.UpdateLifecycleAsync(intent, terminal, completedAt, CancellationToken.None);
            }

            var terminalManifest = queuedManifest with { Execution = terminal, CompletedAt = completedAt };
            var wasReloaded = false;
            if (persist)
            {
                var snapshot = await _repository!.GetSnapshotAsync(runId, CancellationToken.None)
                    ?? throw new InvalidOperationException("Persisted benchmark run could not be read back.");
                terminalManifest = snapshot.Manifest;
                samples = snapshot.Samples;
                outputs = snapshot.Outputs;
                wasReloaded = true;
            }

            var exportStarted = Stopwatch.GetTimestamp();
            var measuredExport = _exportService.Create(terminalManifest, samples, outputs);
            var measuredJson = _exportService.Serialize(measuredExport);
            _ = _exportService.DeserializeAndValidate(measuredJson);
            var exportDuration = ElapsedMilliseconds(exportStarted);
            var exportValidationSample = CreateOrchestrationSample(
                runId,
                exportSampleId,
                scenario,
                fixture,
                InsightMeasurementPhases.ExportValidation,
                exportDuration,
                TimingBoundaryProvenance.DirectlyInstrumented);
            samples = [.. samples, exportValidationSample];
            if (persist)
            {
                // The append happens after the export-validation boundary closes,
                // so repository latency is never mislabeled as export work. Reload
                // once more so the returned persisted run is exactly reproducible
                // from its durable snapshot, including this final raw phase sample.
                await _repository!.AppendSampleAsync(
                    intent,
                    exportValidationSample,
                    CancellationToken.None);
                var snapshot = await _repository.GetSnapshotAsync(runId, CancellationToken.None)
                    ?? throw new InvalidOperationException(
                        "Persisted benchmark run could not be read back after export validation.");
                terminalManifest = snapshot.Manifest;
                samples = snapshot.Samples;
                outputs = snapshot.Outputs;
            }

            var export = _exportService.Create(terminalManifest, samples, outputs);
            var json = _exportService.Serialize(export);
            var deserialized = _exportService.DeserializeAndValidate(json);
            return new BenchmarkSingleRunResult(
                scenario, terminalManifest, samples, outputs, export, json, deserialized,
                persist, wasReloaded);
        }
        finally
        {
            _serialGate.Release();
        }
    }

    private static RunManifest CreateManifest(
        BenchmarkProfileDefinition profile,
        BenchmarkScenarioDefinition scenario,
        DeterministicStressGraphFixture fixture,
        Guid runId,
        DateTimeOffset startedAt,
        StrategySelection strategy,
        string? targetNodeId,
        TimeSpan timeout,
        ExecutionOutcome execution,
        DateTimeOffset? completedAt,
        SourceRevision sourceRevision)
    {
        var spec = fixture.Specification;
        var identity = fixture.Identity;
        return new RunManifest(
            runId,
            scenario.Key,
            execution,
            startedAt,
            completedAt,
            RunnerType.CommandLine,
            scenario.Key,
            scenario.OperationKey,
            new GraphRunIdentity(spec.Slug, spec.GraphId.ToString(), spec.Shape,
                fixture.NodeCount, fixture.EdgeCount, spec.MaximumDepth),
            new DatasetRunIdentity(identity.GeneratorVersion, identity.CorpusId,
                identity.CorpusFingerprint, identity.TopologyFingerprint,
                identity.InputFingerprint, identity.DatasetInputFingerprint),
            new AlgorithmRunIdentity(
                scenario.OperationKey,
                InsightOperationRegistry.Get(scenario.OperationKey).SemanticIdentity),
            strategy,
            new CanonicalParameters(scenario.Parameters, CanonicalJson.ComputeSha256(scenario.Parameters)),
            new RunTargets(targetNodeId is null ? [] : [targetNodeId], []),
            sourceRevision,
#if DEBUG
            "Debug",
            "debug",
#else
            "Release",
            "release",
#endif
            new DependencyVersions(
                Environment.Version.ToString(), "not-used", "not-used", "not-used", "not-measured",
                new Dictionary<string, string> { ["runtime"] = RuntimeInformation.FrameworkDescription }),
            new HostEnvironment(
                RuntimeInformation.OSDescription,
                RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant(),
                "unavailable",
                Math.Max(1, Environment.ProcessorCount),
                Math.Max(1, GC.GetGCMemoryInfo().TotalAvailableMemoryBytes)),
            "local-uncontrolled",
            new WarmupSampleCachePolicy(
                profile.WarmupIterations, profile.MeasuredIterations,
                "profile-defined", "raw-samples", "recorded", "warm"),
            new TimeoutCancellationPolicy(timeout, "cooperative-then-hard-timeout", scenario.RequiresIsolation),
            BenchmarkOperationExecutor.StandardUnits);
    }

    private static RunSample CreateFixtureSample(
        Guid runId,
        Guid sampleId,
        BenchmarkScenarioDefinition scenario,
        DeterministicStressGraphFixture fixture,
        decimal duration) => CreateOrchestrationSample(
            runId,
            sampleId,
            scenario,
            fixture,
            InsightMeasurementPhases.FixtureConstruction,
            duration,
            TimingBoundaryProvenance.DirectlyInstrumented,
            IterationClassificationTokens.Cold,
            IterationClassificationTokens.PreJit,
            IterationClassificationTokens.ColdCache);

    private static RunSample CreateOrchestrationSample(
        Guid runId,
        Guid sampleId,
        BenchmarkScenarioDefinition scenario,
        DeterministicStressGraphFixture fixture,
        string phase,
        decimal duration,
        TimingBoundaryProvenance provenance,
        string temperature = IterationClassificationTokens.Warm,
        string jitState = IterationClassificationTokens.PostJit,
        string cacheState = IterationClassificationTokens.WarmCache) => new(
            runId,
            sampleId,
            scenario.Key,
            scenario.OperationKey,
            InsightMeasurementLayers.BenchmarkOrchestration,
            phase,
            duration,
            0,
            new IterationClassification(
                IterationClassificationTokens.Setup,
                temperature,
                jitState,
                cacheState),
            new SampleNodeCounts(fixture.NodeCount, fixture.NodeCount, fixture.NodeCount, null),
            new SampleEdgeCounts(fixture.EdgeCount, null,
                fixture.NodeCount == 0 ? null : (decimal)fixture.EdgeCount / fixture.NodeCount),
            new SampleSearchCounts(null, null),
            null,
            new SampleTransportMeasurements(null, null, null, null),
            new RuntimeResourceMeasurements(null, null, null, null, null, "ms", null),
            new ExecutionOutcome(ExecutionStatus.Succeeded),
            BenchmarkOperationExecutor.StandardUnits,
            provenance,
            null);

    private static decimal ElapsedMilliseconds(long started) =>
        (Stopwatch.GetTimestamp() - started) * 1000m / Stopwatch.Frequency;

    private static IReadOnlyList<BenchmarkScenarioDefinition> ResolveScenarios(BenchmarkRunSelection selection)
    {
        IEnumerable<BenchmarkScenarioDefinition> query = BenchmarkScenarioRegistry.ForProfile(selection.ProfileKey);
        if (selection.ScenarioKey is not null)
        {
            query = query.Where(value => string.Equals(value.Key, selection.ScenarioKey, StringComparison.Ordinal));
        }

        if (selection.OperationKey is not null)
        {
            _ = InsightOperationRegistry.Get(selection.OperationKey);
            query = query.Where(value => string.Equals(value.OperationKey, selection.OperationKey, StringComparison.Ordinal));
        }

        var resolved = query.ToArray();
        if (resolved.Length == 0)
        {
            throw new ArgumentException("No benchmark scenario matches the requested selection.", nameof(selection));
        }

        if (selection.DatasetId is null && selection.Parameters is null && selection.Strategy is null)
        {
            return resolved;
        }

        return resolved.Select(scenario => Override(scenario, selection)).ToArray();
    }

    private static BenchmarkScenarioDefinition Override(
        BenchmarkScenarioDefinition scenario,
        BenchmarkRunSelection selection)
    {
        var parameters = selection.Parameters?.Clone() ?? scenario.Parameters.Clone();
        if (selection.Strategy is not null)
        {
            var node = JsonNode.Parse(parameters.GetRawText())!.AsObject();
            node["requestedStrategy"] = selection.Strategy;
            parameters = JsonSerializer.SerializeToElement(node);
        }

        var requestedStrategy = selection.Strategy ?? ReadRequestedStrategy(parameters) ?? scenario.RequestedStrategy;
        var datasetId = selection.DatasetId ?? scenario.DatasetId;
        var datasetShape = Backend.Seeding.StressGraphSeedCatalog.Resolve([datasetId]).Single().Shape;
        var requiresIsolation = scenario.RequiresIsolation ||
            string.Equals(scenario.OperationKey, OperationKeys.PathSinglePair, StringComparison.Ordinal) ||
            (string.Equals(scenario.OperationKey, OperationKeys.CounterCriticalSet, StringComparison.Ordinal) &&
             requestedStrategy is OperationStrategyNames.Exact or OperationStrategyNames.Auto) ||
            (string.Equals(scenario.OperationKey, OperationKeys.NodeRobustness, StringComparison.Ordinal) &&
             string.Equals(datasetShape, "deep", StringComparison.Ordinal));

        return new BenchmarkScenarioDefinition(
            scenario.Key,
            scenario.Description,
            scenario.ProfileKey,
            scenario.OperationKey,
            datasetId,
            parameters,
            requestedStrategy,
            requiresIsolation,
            scenario.SkipReason);
    }

    private static string? ReadRequestedStrategy(JsonElement parameters) =>
        parameters.ValueKind == JsonValueKind.Object &&
        parameters.TryGetProperty("requestedStrategy", out var value) &&
        value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}
