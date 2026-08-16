using System.Text.Json;
using Backend.Insights.Benchmarking;
using Backend.Insights.Contracts;
using Backend.Insights.Measurement;
using Backend.Insights.Persistence;
using Backend.Insights.Workers;
using Backend.Seeding;

namespace backend.Tests.Insights.Benchmarking;

[TestClass]
public sealed class BenchmarkProfileExecutionTests
{
    [TestMethod]
    public void ProfileCatalog_DefinesValidatedQuickStandardColdAndAuthoritativePolicies()
    {
        CollectionAssert.AreEqual(
            new[]
            {
                BenchmarkProfiles.QuickKey,
                BenchmarkProfiles.StandardKey,
                BenchmarkProfiles.ColdKey,
                BenchmarkProfiles.AuthoritativeKey
            },
            BenchmarkProfiles.All.Select(profile => profile.Key).ToArray());

        Assert.AreEqual(1, BenchmarkProfiles.Standard.WarmupIterations);
        Assert.AreEqual(3, BenchmarkProfiles.Standard.MeasuredIterations);
        Assert.AreEqual(RunSampleModeTokens.Warm, BenchmarkProfiles.Standard.SampleMode);
        Assert.IsTrue(BenchmarkProfiles.Standard.ExecutionEnabled);

        Assert.AreEqual(0, BenchmarkProfiles.Cold.WarmupIterations);
        Assert.AreEqual(1, BenchmarkProfiles.Cold.MeasuredIterations);
        Assert.AreEqual(RunSampleModeTokens.Cold, BenchmarkProfiles.Cold.SampleMode);
        Assert.IsTrue(BenchmarkProfiles.Cold.RequiresFreshChildProcess);
        StringAssert.Contains(BenchmarkProfiles.Cold.ResetDisclosure, "fresh Node and Chromium");
        StringAssert.Contains(
            BenchmarkProfiles.Cold.ResetDisclosure,
            "static production-profiling Storybook HTTP server");
        StringAssert.Contains(BenchmarkProfiles.Cold.ResetDisclosure, "serving/cache state");
        StringAssert.Contains(BenchmarkProfiles.Cold.ResetDisclosure, "PostgreSQL");
        StringAssert.Contains(BenchmarkProfiles.Cold.ResetDisclosure, "not restarted or cleared");

        Assert.IsFalse(BenchmarkProfiles.Authoritative.ExecutionEnabled);
        Assert.AreEqual(0, BenchmarkProfiles.Authoritative.WarmupIterations);
        Assert.AreEqual(0, BenchmarkProfiles.Authoritative.MeasuredIterations);
        StringAssert.Contains(BenchmarkProfiles.Authoritative.Description, "Phase 6");
        Assert.AreEqual(
            BenchmarkProfiles.Standard.SamplePolicy,
            BenchmarkProfiles.Standard.ToSamplingPolicy().SamplePolicy);
    }

    [TestMethod]
    public async Task AuthoritativeProfile_RefusesBeforeScenarioResolutionOrExecution()
    {
        var executor = new RecordingExecutor();
        var runner = Runner(executor);

        var exception = await Assert.ThrowsExceptionAsync<InvalidOperationException>(() =>
            runner.RunAsync(new BenchmarkRunSelection(BenchmarkProfiles.AuthoritativeKey)));

        StringAssert.Contains(exception.Message, "configuration-and-validation-only");
        StringAssert.Contains(exception.Message, "Phase 6");
        Assert.AreEqual(0, executor.CallCount);
    }

    [TestMethod]
    public async Task StandardProfile_PersistsAndExportsOneWarmupAndThreeMeasuredIterations()
    {
        var executor = new RecordingExecutor();
        var repository = new MemoryBenchmarkRunRepository();
        var run = (await Runner(executor, repository).RunAsync(new BenchmarkRunSelection(
            BenchmarkProfiles.StandardKey,
            ScenarioKey: "standard.strongest.balanced-1k",
            Persist: true))).Runs.Single();

        Assert.AreEqual(4, executor.CallCount);
        Assert.AreEqual(4, executor.SeenSampleIds.Distinct().Count());
        Assert.AreEqual(4, run.Outputs.Count);
        Assert.AreEqual(4, run.Outputs.Select(output => output.SampleId).Distinct().Count());
        Assert.IsTrue(run.WasPersisted);
        Assert.IsTrue(run.WasReloaded);
        Assert.AreEqual(4, run.Export.Outputs.Count);
        Assert.AreEqual(4, run.DeserializedExport.Outputs.Count);

        var rows = IterationRows(run);
        Assert.AreEqual(4, rows.Length);
        var warmup = rows.Single(sample =>
            sample.Classification.IterationKind == IterationClassificationTokens.Warmup);
        Assert.AreEqual(0, warmup.Iteration);
        Assert.AreEqual(IterationClassificationTokens.Warm, warmup.Classification.Temperature);
        Assert.AreEqual(IterationClassificationTokens.PreJit, warmup.Classification.JitState);
        Assert.AreEqual("cache-state-not-controlled", warmup.Classification.CacheState);
        var measured = rows.Where(sample =>
                sample.Classification.IterationKind == IterationClassificationTokens.Measured)
            .ToArray();
        CollectionAssert.AreEqual(
            new[] { 0, 1, 2 },
            measured.Select(sample => sample.Iteration)
                .OrderBy(value => value)
                .ToArray());
        Assert.IsTrue(measured.All(sample =>
            sample.Classification.Temperature == IterationClassificationTokens.Warm &&
            sample.Classification.JitState == IterationClassificationTokens.PostJit &&
            sample.Classification.CacheState == IterationClassificationTokens.WarmCache));
        Assert.AreEqual(RunSampleModeTokens.Warm, run.Manifest.SamplingPolicy.SampleMode);
        Assert.AreEqual("one-recorded-warmup-iteration", run.Manifest.SamplingPolicy.WarmupPolicy);
        Assert.AreEqual("three-recorded-measured-iterations", run.Manifest.SamplingPolicy.SamplePolicy);
        Assert.IsTrue(run.Outputs.All(output =>
            run.Samples.Any(sample => sample.SampleId == output.SampleId)));
    }

    [TestMethod]
    public async Task StandardIsolatedScenario_LabelsEveryFreshWorkerIterationHonestly()
    {
        var executor = new RecordingExecutor();
        var run = (await Runner(executor).RunAsync(new BenchmarkRunSelection(
            BenchmarkProfiles.StandardKey,
            ScenarioKey: "standard.counter.exact.balanced-1k"))).Runs.Single();

        var rows = IterationRows(run);
        Assert.AreEqual(4, rows.Length);
        Assert.IsTrue(rows.All(sample =>
            sample.Classification.Temperature == IterationClassificationTokens.Cold &&
            sample.Classification.JitState == "fresh-isolated-worker-process" &&
            sample.Classification.CacheState == "fresh-worker-cache-os-not-reset"));
        Assert.AreEqual(1, rows.Count(sample =>
            sample.Classification.IterationKind == IterationClassificationTokens.Warmup));
        Assert.AreEqual(3, rows.Count(sample =>
            sample.Classification.IterationKind == IterationClassificationTokens.Measured));
    }

    [TestMethod]
    public async Task StandardProfile_RetainsPriorOutputsAndRunsEveryIterationAfterFailures()
    {
        var executor = new SequencedExecutor([
            ExecutionStatus.Succeeded,
            ExecutionStatus.Failed,
            ExecutionStatus.Succeeded,
            ExecutionStatus.TimedOut
        ]);
        var run = (await Runner(executor).RunAsync(new BenchmarkRunSelection(
            BenchmarkProfiles.StandardKey,
            ScenarioKey: "standard.strongest.balanced-1k"))).Runs.Single();

        Assert.AreEqual(4, executor.CallCount);
        Assert.AreEqual(2, run.Outputs.Count);
        Assert.AreEqual(ExecutionStatus.TimedOut, run.Manifest.Execution.Status);
        Assert.AreEqual(FailureKind.Timeout, run.Manifest.Execution.Failure?.Kind);
        CollectionAssert.IsSubsetOf(
            new[] { ExecutionStatus.Failed, ExecutionStatus.TimedOut },
            IterationRows(run).Select(sample => sample.Execution.Status).ToArray());
        Assert.AreEqual(run.Export.Digests.SamplesDigest, run.DeserializedExport.Digests.SamplesDigest);
        Assert.AreEqual(run.Export.Digests.OutputsDigest, run.DeserializedExport.Digests.OutputsDigest);
    }

    [TestMethod]
    public void ColdProfile_RejectsUnresetRestAndGraphBrowserExecution()
    {
        var rest = BenchmarkScenarioRegistry.Get("quick.graph-fetch.balanced-1k.rest");
        var graphBrowser = new BenchmarkScenarioDefinition(
            "cold.test.graph-browser",
            "Unreset graph browser test.",
            BenchmarkProfiles.ColdKey,
            OperationKeys.GraphFetch,
            StressGraphSeedIds.Balanced1K,
            JsonSerializer.SerializeToElement(new { }),
            null,
            requiresIsolation: false,
            executionTarget: BenchmarkScenarioExecutionTarget.Browser,
            browserJourney: new BrowserJourneyDefinition(BrowserJourneyActions.Collapsed));

        Assert.ThrowsException<InvalidOperationException>(() =>
            BenchmarkProfiles.ValidateScenarioExecution(BenchmarkProfiles.Cold, rest));
        Assert.ThrowsException<InvalidOperationException>(() =>
            BenchmarkProfiles.ValidateScenarioExecution(BenchmarkProfiles.Cold, graphBrowser));
        Assert.IsTrue(BenchmarkScenarioRegistry.Get(
            "cold.graph-fetch.balanced-1k.rest.skipped").IsSkipped);
        Assert.IsTrue(BenchmarkScenarioRegistry.Get(
            "cold.browser.collapsed.balanced-1k.skipped").IsSkipped);
    }

    [TestMethod]
    public async Task ColdIsolatedProfile_LabelsOnlyFreshWorkerScopeAsCold()
    {
        var executor = new RecordingExecutor();
        var run = (await Runner(executor).RunAsync(new BenchmarkRunSelection(
            BenchmarkProfiles.ColdKey,
            ScenarioKey: "cold.strongest.balanced-1k.isolated-worker"))).Runs.Single();

        var measured = IterationRows(run).Single();
        Assert.AreEqual(IterationClassificationTokens.Measured, measured.Classification.IterationKind);
        Assert.AreEqual(IterationClassificationTokens.Cold, measured.Classification.Temperature);
        Assert.AreEqual("fresh-isolated-worker-process", measured.Classification.JitState);
        Assert.AreEqual("fresh-worker-cache-os-not-reset", measured.Classification.CacheState);
        Assert.AreEqual(RunSampleModeTokens.Cold, run.Manifest.SamplingPolicy.SampleMode);
        Assert.AreEqual(1, executor.CallCount);
    }

    [TestMethod]
    public async Task ColdResultRender_SeparatesSharedRunnerSetupFromFreshBrowserMeasurement()
    {
        var executor = new RecordingExecutor(includeRunnerSetup: true);
        var run = (await Runner(executor).RunAsync(new BenchmarkRunSelection(
            BenchmarkProfiles.ColdKey,
            ScenarioKey: "cold.browser.result-render.strongest.balanced-1k"))).Runs.Single();
        var outputSampleId = run.Outputs.Single().SampleId;

        var setup = run.Samples.Single(sample =>
            sample.SampleId == outputSampleId &&
            sample.Phase == InsightMeasurementPhases.OperationExecution &&
            sample.Classification.IterationKind == IterationClassificationTokens.Setup);
        Assert.AreEqual(IterationClassificationTokens.Warm, setup.Classification.Temperature);
        Assert.AreEqual("shared-runner-process-not-reset", setup.Classification.JitState);
        Assert.AreEqual("shared-runner-cache-not-reset", setup.Classification.CacheState);

        var measured = run.Samples.Single(sample =>
            sample.SampleId == outputSampleId &&
            sample.Phase == InsightMeasurementPhases.OperationExecution &&
            sample.Classification.IterationKind == IterationClassificationTokens.Measured);
        Assert.AreEqual(IterationClassificationTokens.Cold, measured.Classification.Temperature);
        Assert.AreEqual("fresh-browser-process", measured.Classification.JitState);
        Assert.AreEqual("fresh-browser-cache-os-not-reset", measured.Classification.CacheState);
    }

    [DataTestMethod]
    [DataRow("standard.strongest.balanced-1k")]
    [DataRow("standard.evidence.wide-1k")]
    [DataRow("standard.counter.greedy.balanced-1k")]
    [DataRow("standard.likelihood.balanced-1k")]
    public async Task DatasetOverride_ToDeep_RecomputesIsolationForOtherwiseSafeInProcessOperations(
        string scenarioKey)
    {
        var runner = Runner(new NoOpExecutor());

        var ordinary = (await runner.RunAsync(new BenchmarkRunSelection(
            BenchmarkProfiles.StandardKey,
            ScenarioKey: scenarioKey))).Runs.Single();
        var deep = (await runner.RunAsync(new BenchmarkRunSelection(
            BenchmarkProfiles.StandardKey,
            ScenarioKey: scenarioKey,
            DatasetId: StressGraphSeedIds.Deep1K))).Runs.Single();

        Assert.IsFalse(
            ordinary.Scenario.RequiresIsolation,
            $"The registered non-deep scenario '{scenarioKey}' should remain in-process.");
        Assert.IsFalse(ordinary.Manifest.ExecutionPolicy.IsolatedWorker);
        Assert.IsTrue(
            deep.Scenario.RequiresIsolation,
            $"A deep dataset override for '{scenarioKey}' must execute in an isolated worker.");
        Assert.IsTrue(deep.Manifest.ExecutionPolicy.IsolatedWorker);
        Assert.AreEqual(StressGraphSeedIds.Deep1K, deep.Scenario.DatasetId);
    }

    [TestMethod]
    public async Task Override_PreservesExistingIsolationRulesAndQualityFlag()
    {
        var runner = Runner(new NoOpExecutor());

        var exact = (await runner.RunAsync(new BenchmarkRunSelection(
            BenchmarkProfiles.StandardKey,
            ScenarioKey: "standard.counter.exact.balanced-1k",
            DatasetId: StressGraphSeedIds.SharedDiamond1K))).Runs.Single().Scenario;
        var auto = (await runner.RunAsync(new BenchmarkRunSelection(
            BenchmarkProfiles.StandardKey,
            ScenarioKey: "standard.counter.auto-exact.balanced-1k",
            DatasetId: StressGraphSeedIds.SharedDiamond1K))).Runs.Single().Scenario;
        var robustness = (await runner.RunAsync(new BenchmarkRunSelection(
            BenchmarkProfiles.StandardKey,
            ScenarioKey: "standard.robustness.balanced-1k",
            DatasetId: StressGraphSeedIds.SharedDiamond1K))).Runs.Single().Scenario;

        Assert.IsTrue(exact.RequiresIsolation);
        Assert.IsTrue(exact.MeasureQualityComparison);
        Assert.IsTrue(auto.RequiresIsolation);
        Assert.IsTrue(robustness.RequiresIsolation);
    }

    [TestMethod]
    public async Task BrowserScenario_AllowsTimeoutButRefusesDatasetParameterAndStrategyOverrides()
    {
        var executor = new NoOpExecutor();
        var runner = Runner(executor);
        var allowed = (await runner.RunAsync(new BenchmarkRunSelection(
            BenchmarkProfiles.QuickKey,
            ScenarioKey: "quick.browser.search.compact.balanced-1k",
            Timeout: TimeSpan.FromSeconds(1)))).Runs.Single();

        Assert.AreEqual(BrowserJourneyActions.Search, allowed.Scenario.BrowserJourney?.Action);
        Assert.AreEqual("n-00015", allowed.Scenario.BrowserJourney?.SearchQuery);
        Assert.AreEqual(TimeSpan.FromSeconds(1), allowed.Manifest.ExecutionPolicy.Timeout);
        Assert.AreEqual(1, executor.CallCount);

        var datasetException = await Assert.ThrowsExceptionAsync<ArgumentException>(() =>
            runner.RunAsync(new BenchmarkRunSelection(
                BenchmarkProfiles.QuickKey,
                ScenarioKey: "quick.browser.result-render.strongest.balanced-1k",
                DatasetId: StressGraphSeedIds.Deep1K)));
        var parametersException = await Assert.ThrowsExceptionAsync<ArgumentException>(() =>
            runner.RunAsync(new BenchmarkRunSelection(
                BenchmarkProfiles.QuickKey,
                ScenarioKey: "quick.browser.search.compact.balanced-1k",
                Parameters: JsonSerializer.SerializeToElement(new { query = "n-00031" }))));
        var strategyException = await Assert.ThrowsExceptionAsync<ArgumentException>(() =>
            runner.RunAsync(new BenchmarkRunSelection(
                BenchmarkProfiles.QuickKey,
                ScenarioKey: "quick.browser.search.compact.balanced-1k",
                Strategy: OperationStrategyNames.Greedy)));

        StringAssert.Contains(datasetException.Message, "registry-locked");
        StringAssert.Contains(parametersException.Message, "registry-locked");
        StringAssert.Contains(strategyException.Message, "registry-locked");
        Assert.AreEqual(1, executor.CallCount);
    }

    private static SerialBenchmarkRunner Runner(
        IBenchmarkOperationExecutor executor,
        IBenchmarkRunRepository? repository = null) => new(
            executor,
            repository,
            sourceRevisionProvider: new FixedSourceRevisionProvider());

    private static RunSample[] IterationRows(BenchmarkSingleRunResult run) => run.Samples
        .Where(sample =>
            sample.Phase == InsightMeasurementPhases.OperationExecution &&
            sample.Classification.IterationKind != IterationClassificationTokens.Setup)
        .ToArray();

    private sealed class RecordingExecutor : IBenchmarkOperationExecutor
    {
        private readonly bool _includeRunnerSetup;
        private readonly AnalysisWorkerDispatcher _dispatcher = new();

        public RecordingExecutor(bool includeRunnerSetup = false) =>
            _includeRunnerSetup = includeRunnerSetup;

        public int CallCount { get; private set; }
        public List<Guid> SeenSampleIds { get; } = [];

        public Task<BenchmarkOperationExecutionResult> ExecuteAsync(
            PreparedBenchmarkOperation operation,
            BenchmarkScenarioDefinition scenario,
            DeterministicStressGraphFixture fixture,
            BenchmarkProfileDefinition profile,
            TimeSpan timeout,
            CancellationToken cancellationToken)
        {
            CallCount++;
            SeenSampleIds.Add(operation.Request.SampleId);
            var output = _dispatcher.Dispatch(operation.Request, cancellationToken);
            var execution = new ExecutionOutcome(ExecutionStatus.Succeeded);
            var samples = new List<RunSample>();
            if (_includeRunnerSetup)
            {
                samples.Add(Sample(
                    operation,
                    scenario,
                    fixture,
                    execution,
                    IterationClassificationTokens.Setup));
            }

            samples.Add(Sample(
                operation,
                scenario,
                fixture,
                execution,
                IterationClassificationTokens.Measured));
            return Task.FromResult(new BenchmarkOperationExecutionResult(execution, samples, [output]));
        }
    }

    private sealed class NoOpExecutor : IBenchmarkOperationExecutor
    {
        public int CallCount { get; private set; }

        public Task<BenchmarkOperationExecutionResult> ExecuteAsync(
            PreparedBenchmarkOperation operation,
            BenchmarkScenarioDefinition scenario,
            DeterministicStressGraphFixture fixture,
            BenchmarkProfileDefinition profile,
            TimeSpan timeout,
            CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult(new BenchmarkOperationExecutionResult(
                new ExecutionOutcome(ExecutionStatus.Succeeded),
                [],
                []));
        }
    }

    private sealed class SequencedExecutor : IBenchmarkOperationExecutor
    {
        private readonly IReadOnlyList<ExecutionStatus> _statuses;
        private readonly AnalysisWorkerDispatcher _dispatcher = new();

        public SequencedExecutor(IReadOnlyList<ExecutionStatus> statuses) => _statuses = statuses;

        public int CallCount { get; private set; }

        public Task<BenchmarkOperationExecutionResult> ExecuteAsync(
            PreparedBenchmarkOperation operation,
            BenchmarkScenarioDefinition scenario,
            DeterministicStressGraphFixture fixture,
            BenchmarkProfileDefinition profile,
            TimeSpan timeout,
            CancellationToken cancellationToken)
        {
            var status = _statuses[CallCount++];
            var execution = status switch
            {
                ExecutionStatus.Succeeded => new ExecutionOutcome(ExecutionStatus.Succeeded),
                ExecutionStatus.Failed => BenchmarkOperationExecutor.Failure(
                    status, FailureKind.Execution, "simulated-failure", "Simulated iteration failure."),
                ExecutionStatus.TimedOut => BenchmarkOperationExecutor.Failure(
                    status, FailureKind.Timeout, "simulated-timeout", "Simulated iteration timeout."),
                _ => throw new AssertFailedException($"Unsupported simulated status {status}.")
            };
            var samples = new[]
            {
                Sample(
                    operation,
                    scenario,
                    fixture,
                    execution,
                    IterationClassificationTokens.Measured)
            };
            var outputs = status == ExecutionStatus.Succeeded
                ? new[] { _dispatcher.Dispatch(operation.Request, cancellationToken) }
                : Array.Empty<CompactRunOutput>();
            return Task.FromResult(new BenchmarkOperationExecutionResult(execution, samples, outputs));
        }
    }

    private static RunSample Sample(
        PreparedBenchmarkOperation operation,
        BenchmarkScenarioDefinition scenario,
        DeterministicStressGraphFixture fixture,
        ExecutionOutcome execution,
        string iterationKind) => new(
            operation.Request.RunId,
            operation.Request.SampleId,
            scenario.Key,
            scenario.OperationKey,
            InsightMeasurementLayers.BenchmarkOrchestration,
            InsightMeasurementPhases.OperationExecution,
            1m,
            99,
            new IterationClassification(
                iterationKind,
                IterationClassificationTokens.Warm,
                IterationClassificationTokens.PostJit,
                IterationClassificationTokens.WarmCache),
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
            TimingBoundaryProvenance.DirectlyInstrumented,
            null);

    private sealed class FixedSourceRevisionProvider : ISourceRevisionProvider
    {
        public SourceRevision GetSourceRevision() => new("abcdef1", true);
    }

    private sealed class MemoryBenchmarkRunRepository : IBenchmarkRunRepository
    {
        private RunManifest? _manifest;
        private readonly List<RunSample> _samples = [];
        private readonly List<CompactRunOutput> _outputs = [];

        public Task CreateRunAsync(
            ExplicitBenchmarkRunIntent intent,
            RunManifest manifest,
            CancellationToken cancellationToken = default)
        {
            Assert.AreEqual(intent.RunId, manifest.RunId);
            _manifest = manifest;
            return Task.CompletedTask;
        }

        public Task UpdateLifecycleAsync(
            ExplicitBenchmarkRunIntent intent,
            ExecutionOutcome execution,
            DateTimeOffset? completedAt,
            CancellationToken cancellationToken = default)
        {
            Assert.IsNotNull(_manifest);
            Assert.AreEqual(intent.RunId, _manifest.RunId);
            _manifest = _manifest with { Execution = execution, CompletedAt = completedAt };
            return Task.CompletedTask;
        }

        public Task AppendSampleAsync(
            ExplicitBenchmarkRunIntent intent,
            RunSample sample,
            CancellationToken cancellationToken = default)
        {
            Assert.AreEqual(intent.RunId, sample.RunId);
            _samples.Add(sample);
            return Task.CompletedTask;
        }

        public Task AppendOutputAsync(
            ExplicitBenchmarkRunIntent intent,
            CompactRunOutput output,
            CancellationToken cancellationToken = default)
        {
            Assert.AreEqual(intent.RunId, output.RunId);
            _outputs.Add(output);
            return Task.CompletedTask;
        }

        public Task<BenchmarkRunSnapshot?> GetSnapshotAsync(
            Guid runId,
            CancellationToken cancellationToken = default)
        {
            Assert.IsNotNull(_manifest);
            Assert.AreEqual(runId, _manifest.RunId);
            return Task.FromResult<BenchmarkRunSnapshot?>(
                new BenchmarkRunSnapshot(_manifest, _samples, _outputs));
        }
    }
}
