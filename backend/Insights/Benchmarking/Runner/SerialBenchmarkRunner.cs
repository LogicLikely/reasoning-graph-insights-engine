using System.Runtime.InteropServices;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Backend.Insights.Contracts;
using Backend.Insights.Export;
using Backend.Insights.Measurement;
using Backend.Insights.Persistence;
using Backend.Insights.Workers;

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
        var profile = BenchmarkProfiles.RequireExecutable(selection.ProfileKey);
        var scenarios = ResolveScenarios(selection);
        foreach (var scenario in scenarios)
        {
            BenchmarkProfiles.ValidateScenarioExecution(profile, scenario);
        }

        var results = new List<BenchmarkSingleRunResult>(scenarios.Count);
        foreach (var scenario in scenarios)
        {
            if (cancellationToken.IsCancellationRequested && results.Count > 0)
            {
                break;
            }

            cancellationToken.ThrowIfCancellationRequested();
            results.Add(await RunOneAsync(profile, scenario, selection, cancellationToken));
            if (cancellationToken.IsCancellationRequested)
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
            BenchmarkScenarioPreparationResult? scenarioPreparation = null;
            IReadOnlyList<RunSample> preparationSamples = [];
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

            if (operation is not null &&
                preparationFailure is null &&
                _executor is IBenchmarkScenarioPreparer preparer)
            {
                try
                {
                    scenarioPreparation = await preparer.PrepareAsync(
                        operation,
                        scenario,
                        fixture,
                        selection.Timeout ?? profile.DefaultTimeout,
                        cancellationToken);
                    operation = scenarioPreparation.Operation;
                    strategy = operation.Strategy;
                    targetNodeId = operation.TargetNodeId;
                    preparationSamples = scenarioPreparation.SetupSamples;
                    if (scenarioPreparation.Execution.Status != ExecutionStatus.Succeeded)
                    {
                        preparationFailure = scenarioPreparation.Execution;
                    }
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    preparationFailure = BenchmarkOperationExecutor.Failure(
                        ExecutionStatus.Cancelled,
                        FailureKind.Cancellation,
                        "benchmark-scenario-setup-cancelled",
                        "Benchmark scenario setup was cancelled.");
                }
                catch (Exception exception)
                {
                    preparationFailure = BenchmarkOperationExecutor.Failure(
                        ExecutionStatus.Failed,
                        FailureKind.Execution,
                        "benchmark-scenario-setup-failed",
                        "Benchmark scenario setup failed unexpectedly.",
                        exception.GetType().FullName);
                }
            }

            var queuedManifest = CreateManifest(
                profile, scenario, fixture, runId, startedAt, strategy, targetNodeId,
                selection.Timeout ?? profile.DefaultTimeout,
                new ExecutionOutcome(ExecutionStatus.Queued), null,
                _sourceRevision,
                scenarioPreparation);
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
                samples = [
                    CreateFixtureSample(runId, fixtureSampleId, scenario, fixture, fixtureDuration),
                    .. preparationSamples
                ];
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
                samples = [
                    CreateFixtureSample(runId, fixtureSampleId, scenario, fixture, fixtureDuration),
                    .. preparationSamples
                ];
                outputs = [];
            }
            else
            {
                if (persist)
                {
                    await ObservePersistenceAsync(() => _repository!.UpdateLifecycleAsync(
                        intent, new ExecutionOutcome(ExecutionStatus.Running), null, CancellationToken.None));
                }

                var iterationSamples = new List<RunSample>();
                var iterationOutputs = new List<CompactRunOutput>();
                var iterationOutcomes = new List<ExecutionOutcome>(
                    profile.WarmupIterations + profile.MeasuredIterations);
                var iterationSequence = 0;
                foreach (var iteration in EnumerateIterations(profile))
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        iterationOutcomes.Add(BenchmarkOperationExecutor.Failure(
                            ExecutionStatus.Cancelled,
                            FailureKind.Cancellation,
                            "benchmark-runner-cancelled",
                            "The benchmark runner was cancelled before the next profile iteration."));
                        break;
                    }

                    // The first iteration retains the request/preparation
                    // correlation ID. Every later profile iteration receives
                    // a distinct ID while one-time setup remains traceable to
                    // the first operation it prepared.
                    var iterationSampleId = iterationSequence++ == 0
                        ? operation!.Request.SampleId
                        : _identitySource.NewSampleId();
                    var iterationOperation = WithSampleIdentity(operation!, runId, iterationSampleId);
                    var iterationStarted = Stopwatch.GetTimestamp();
                    BenchmarkOperationExecutionResult execution;
                    try
                    {
                        execution = await _executor.ExecuteAsync(
                            iterationOperation,
                            scenario,
                            fixture,
                            profile,
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
                    catch (OperationCanceledException exception)
                    {
                        execution = new BenchmarkOperationExecutionResult(
                            BenchmarkOperationExecutor.Failure(
                                ExecutionStatus.Failed,
                                FailureKind.Execution,
                                "benchmark-executor-unexpected-cancellation",
                                "The benchmark executor cancelled without a caller cancellation or structured timeout.",
                                exception.GetType().FullName),
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

                    var normalizedSamples = execution.Samples
                        .Select(sample => RetagIterationSample(
                            sample,
                            runId,
                            iterationSampleId,
                            iteration.Index,
                            iteration.Kind,
                            profile,
                            scenario))
                        .ToList();
                    if (normalizedSamples.Count == 0)
                    {
                        normalizedSamples.Add(CreateIterationOutcomeSample(
                            runId,
                            iterationSampleId,
                            scenario,
                            fixture,
                            execution.Execution,
                            ElapsedMilliseconds(iterationStarted),
                            iteration.Index,
                            IterationClassificationFor(
                                profile,
                                scenario,
                                InsightMeasurementLayers.BenchmarkOrchestration,
                                iteration.Kind)));
                    }

                    iterationSamples.AddRange(normalizedSamples);
                    iterationOutputs.AddRange(execution.Outputs.Select(output =>
                        WithOutputIdentity(output, runId, iterationSampleId)));
                    iterationOutcomes.Add(NormalizeTerminalOutcome(execution.Execution));

                    // A failed, timed-out, crashed, skipped, or internally-cancelled
                    // iteration does not erase prior evidence and does not suppress
                    // later iterations. Only the caller's cancellation stops work.
                    if (cancellationToken.IsCancellationRequested)
                    {
                        break;
                    }
                }

                terminal = AggregateTerminalOutcome(iterationOutcomes);
                samples = [
                    CreateFixtureSample(runId, fixtureSampleId, scenario, fixture, fixtureDuration),
                    .. preparationSamples,
                    .. iterationSamples
                ];
                outputs = iterationOutputs.AsReadOnly();

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
        SourceRevision sourceRevision,
        BenchmarkScenarioPreparationResult? preparation)
    {
        var spec = fixture.Specification;
        var identity = fixture.Identity;
        return new RunManifest(
            runId,
            scenario.Key,
            execution,
            startedAt,
            completedAt,
            preparation?.RunnerType ?? RunnerType.CommandLine,
            scenario.Key,
            scenario.OperationKey,
            preparation?.GraphIdentity ??
                new GraphRunIdentity(spec.Slug, spec.GraphId.ToString(), spec.Shape,
                    fixture.NodeCount, fixture.EdgeCount, spec.MaximumDepth),
            preparation?.DatasetIdentity ??
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
            preparation?.Dependencies ??
                new DependencyVersions(
                    Environment.Version.ToString(), "not-used", "not-used", "not-used", "not-measured",
                    new Dictionary<string, string> { ["runtime"] = RuntimeInformation.FrameworkDescription }),
            new HostEnvironment(
                RuntimeInformation.OSDescription,
                RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant(),
                "unavailable",
                Math.Max(1, Environment.ProcessorCount),
                Math.Max(1, GC.GetGCMemoryInfo().TotalAvailableMemoryBytes)),
            preparation?.EnvironmentProfile ?? "local-uncontrolled",
            profile.ToSamplingPolicy(),
            new TimeoutCancellationPolicy(timeout, "cooperative-then-hard-timeout", scenario.RequiresIsolation),
            BenchmarkOperationExecutor.StandardUnits,
            profile.Key);
    }

    private static IEnumerable<ProfileIteration> EnumerateIterations(
        BenchmarkProfileDefinition profile)
    {
        for (var index = 0; index < profile.WarmupIterations; index++)
        {
            yield return new ProfileIteration(index, IterationClassificationTokens.Warmup);
        }

        for (var index = 0; index < profile.MeasuredIterations; index++)
        {
            yield return new ProfileIteration(index, IterationClassificationTokens.Measured);
        }
    }

    private static PreparedBenchmarkOperation WithSampleIdentity(
        PreparedBenchmarkOperation operation,
        Guid runId,
        Guid sampleId)
    {
        var request = operation.Request;
        return operation with
        {
            Request = new WorkerRequestFrame(
                request.ProtocolIdentity,
                request.ProtocolVersion,
                request.MessageType,
                runId,
                sampleId,
                request.OperationKey,
                request.AlgorithmSemanticIdentity,
                request.CanonicalParameters,
                request.Input)
        };
    }

    private static RunSample RetagIterationSample(
        RunSample sample,
        Guid runId,
        Guid sampleId,
        int iteration,
        string iterationKind,
        BenchmarkProfileDefinition profile,
        BenchmarkScenarioDefinition scenario)
    {
        ArgumentNullException.ThrowIfNull(sample);
        var classification = sample.Classification.IterationKind == IterationClassificationTokens.Setup
            ? SetupClassificationFor(sample, scenario)
            : IterationClassificationFor(profile, scenario, sample.Layer, iterationKind, sample.Phase);
        return sample with
        {
            RunId = runId,
            SampleId = sampleId,
            ScenarioKey = scenario.Key,
            OperationKey = scenario.OperationKey,
            Iteration = iteration,
            Classification = classification
        };
    }

    private static IterationClassification SetupClassificationFor(
        RunSample sample,
        BenchmarkScenarioDefinition scenario)
    {
        // Result-render fixture generation happens synchronously in the
        // long-lived runner before the fresh browser process is launched. It
        // remains setup work, but must never inherit a cold-browser label.
        if (scenario.ExecutionTarget == BenchmarkScenarioExecutionTarget.Browser &&
            scenario.BrowserJourney?.Action == BrowserJourneyActions.ResultRender &&
            sample.Layer == InsightMeasurementLayers.BenchmarkOrchestration &&
            sample.Phase == InsightMeasurementPhases.OperationExecution)
        {
            return new IterationClassification(
                IterationClassificationTokens.Setup,
                IterationClassificationTokens.Warm,
                BenchmarkProcessStateTokens.SharedRunnerProcessNotReset,
                BenchmarkProcessStateTokens.SharedRunnerCacheNotReset);
        }

        return sample.Classification;
    }

    private static IterationClassification IterationClassificationFor(
        BenchmarkProfileDefinition profile,
        BenchmarkScenarioDefinition scenario,
        string layer,
        string iterationKind,
        string? phase = null)
    {
        if (phase == InsightMeasurementPhases.ExactGreedyQualityComparison)
        {
            return new IterationClassification(
                iterationKind,
                IterationClassificationTokens.Warm,
                BenchmarkProcessStateTokens.SharedRunnerProcessNotReset,
                BenchmarkProcessStateTokens.SharedRunnerCacheNotReset);
        }

        if (scenario.ExecutionTarget == BenchmarkScenarioExecutionTarget.Browser)
        {
            if (layer is InsightMeasurementLayers.PostgreSqlRepository or
                InsightMeasurementLayers.BackendServiceApi)
            {
                return new IterationClassification(
                    iterationKind,
                    IterationClassificationTokens.Warm,
                    BenchmarkProcessStateTokens.SharedServiceProcessNotReset,
                    BenchmarkProcessStateTokens.SharedServiceCacheNotReset);
            }

            var cacheState = scenario.BrowserJourney?.Action == BrowserJourneyActions.ResultRender
                ? BenchmarkProcessStateTokens.FreshBrowserCacheOsNotReset
                : BenchmarkProcessStateTokens.FreshBrowserCacheSharedServicesNotReset;
            return new IterationClassification(
                iterationKind,
                IterationClassificationTokens.Cold,
                BenchmarkProcessStateTokens.FreshBrowserProcess,
                cacheState);
        }

        if (scenario.RequiresIsolation)
        {
            return new IterationClassification(
                iterationKind,
                IterationClassificationTokens.Cold,
                BenchmarkProcessStateTokens.FreshIsolatedWorkerProcess,
                BenchmarkProcessStateTokens.FreshWorkerCacheOsNotReset);
        }

        if (scenario.ExecutionTarget is BenchmarkScenarioExecutionTarget.RestDatabaseLoaded or
            BenchmarkScenarioExecutionTarget.RestSuppliedGraph)
        {
            return new IterationClassification(
                iterationKind,
                IterationClassificationTokens.Warm,
                BenchmarkProcessStateTokens.SharedServiceProcessNotReset,
                BenchmarkProcessStateTokens.SharedServiceCacheNotReset);
        }

        if (iterationKind == IterationClassificationTokens.Warmup)
        {
            return new IterationClassification(
                iterationKind,
                IterationClassificationTokens.Warm,
                IterationClassificationTokens.PreJit,
                BenchmarkProcessStateTokens.CacheStateNotControlled);
        }

        if (profile.WarmupIterations > 0)
        {
            return new IterationClassification(
                iterationKind,
                IterationClassificationTokens.Warm,
                IterationClassificationTokens.PostJit,
                IterationClassificationTokens.WarmCache);
        }

        return new IterationClassification(
            iterationKind,
            IterationClassificationTokens.Warm,
            BenchmarkProcessStateTokens.JitStateNotControlled,
            BenchmarkProcessStateTokens.CacheStateNotControlled);
    }

    private static RunSample CreateIterationOutcomeSample(
        Guid runId,
        Guid sampleId,
        BenchmarkScenarioDefinition scenario,
        DeterministicStressGraphFixture fixture,
        ExecutionOutcome execution,
        decimal duration,
        int iteration,
        IterationClassification classification) => new(
            runId,
            sampleId,
            scenario.Key,
            scenario.OperationKey,
            InsightMeasurementLayers.BenchmarkOrchestration,
            InsightMeasurementPhases.OperationExecution,
            duration,
            iteration,
            classification,
            new SampleNodeCounts(fixture.NodeCount, fixture.NodeCount, fixture.NodeCount, null),
            new SampleEdgeCounts(
                fixture.EdgeCount,
                null,
                fixture.NodeCount == 0 ? null : (decimal)fixture.EdgeCount / fixture.NodeCount),
            new SampleSearchCounts(null, null),
            null,
            new SampleTransportMeasurements(null, null, null, null),
            new RuntimeResourceMeasurements(null, null, null, null, null, "ms", null),
            execution,
            BenchmarkOperationExecutor.StandardUnits,
            TimingBoundaryProvenance.ExternallyObserved,
            null);

    private static CompactRunOutput WithOutputIdentity(
        CompactRunOutput output,
        Guid runId,
        Guid sampleId) => new(
            runId,
            sampleId,
            output.ScenarioKey,
            output.OperationKey,
            output.AlgorithmSemanticIdentity,
            output.Strategy,
            output.Identifiers,
            output.CanonicalParameters,
            output.Execution,
            output.Summary,
            output.Distribution,
            output.TotalResultCardinality,
            output.Items,
            output.ResultDigest,
            output.FullResultArtifactReference,
            output.OrderedPaths);

    private static ExecutionOutcome NormalizeTerminalOutcome(ExecutionOutcome outcome) =>
        outcome.Status is ExecutionStatus.Queued or ExecutionStatus.Running
            ? BenchmarkOperationExecutor.Failure(
                ExecutionStatus.Failed,
                FailureKind.Execution,
                "benchmark-executor-nonterminal-outcome",
                "The benchmark executor returned a non-terminal iteration outcome.")
            : outcome;

    private static ExecutionOutcome AggregateTerminalOutcome(
        IReadOnlyList<ExecutionOutcome> outcomes)
    {
        if (outcomes.Count == 0)
        {
            return BenchmarkOperationExecutor.Failure(
                ExecutionStatus.Failed,
                FailureKind.Execution,
                "benchmark-profile-no-iterations",
                "The executable benchmark profile produced no iteration outcome.");
        }

        var selected = NormalizeTerminalOutcome(outcomes[0]);
        for (var index = 1; index < outcomes.Count; index++)
        {
            var candidate = NormalizeTerminalOutcome(outcomes[index]);
            if (TerminalSeverity(candidate.Status) > TerminalSeverity(selected.Status))
            {
                selected = candidate;
            }
        }

        return selected;
    }

    private static int TerminalSeverity(ExecutionStatus status) => status switch
    {
        ExecutionStatus.Cancelled => 6,
        ExecutionStatus.Crashed => 5,
        ExecutionStatus.TimedOut => 4,
        ExecutionStatus.Failed => 3,
        ExecutionStatus.Skipped => 2,
        ExecutionStatus.Succeeded => 1,
        _ => 0
    };

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
        if (scenario.ExecutionTarget == BenchmarkScenarioExecutionTarget.Browser)
        {
            throw new ArgumentException(
                $"Browser scenario '{scenario.Key}' uses a registry-locked dataset, parameters, strategy, and journey. " +
                "Dataset, parameter, and strategy overrides are refused so manifest, request, browser journey, and " +
                "materialization-safety evidence cannot drift. Select a registered browser scenario instead.",
                nameof(selection));
        }

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
        var isDeepDataset = string.Equals(datasetShape, "deep", StringComparison.Ordinal);
        var requiresIsolation = scenario.RequiresIsolation ||
            string.Equals(scenario.OperationKey, OperationKeys.PathSinglePair, StringComparison.Ordinal) ||
            (string.Equals(scenario.OperationKey, OperationKeys.CounterCriticalSet, StringComparison.Ordinal) &&
             requestedStrategy is OperationStrategyNames.Exact or OperationStrategyNames.Auto) ||
            (isDeepDataset && RequiresDeepShapeIsolation(scenario.OperationKey));

        return new BenchmarkScenarioDefinition(
            scenario.Key,
            scenario.Description,
            scenario.ProfileKey,
            scenario.OperationKey,
            datasetId,
            parameters,
            requestedStrategy,
            requiresIsolation,
            scenario.SkipReason,
            scenario.ExecutionTarget,
            scenario.BrowserJourney,
            scenario.MeasureQualityComparison);
    }

    private static bool RequiresDeepShapeIsolation(string operationKey) => operationKey is
        OperationKeys.PathStrongest or
        OperationKeys.EvidenceImpactRanking or
        OperationKeys.CounterCriticalSet or
        OperationKeys.NodeRobustness or
        OperationKeys.LikelihoodRecalculate;

    private static string? ReadRequestedStrategy(JsonElement parameters) =>
        parameters.ValueKind == JsonValueKind.Object &&
        parameters.TryGetProperty("requestedStrategy", out var value) &&
        value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private readonly record struct ProfileIteration(int Index, string Kind);

    private static class BenchmarkProcessStateTokens
    {
        public const string FreshBrowserProcess = "fresh-browser-process";
        public const string FreshIsolatedWorkerProcess = "fresh-isolated-worker-process";
        public const string SharedRunnerProcessNotReset = "shared-runner-process-not-reset";
        public const string SharedServiceProcessNotReset = "shared-service-process-not-reset";
        public const string JitStateNotControlled = "jit-state-not-controlled";
        public const string FreshBrowserCacheOsNotReset = "fresh-browser-cache-os-not-reset";
        public const string FreshBrowserCacheSharedServicesNotReset =
            "fresh-browser-cache-shared-services-not-reset";
        public const string FreshWorkerCacheOsNotReset = "fresh-worker-cache-os-not-reset";
        public const string SharedRunnerCacheNotReset = "shared-runner-cache-not-reset";
        public const string SharedServiceCacheNotReset = "shared-service-cache-not-reset";
        public const string CacheStateNotControlled = "cache-state-not-controlled";
    }
}
