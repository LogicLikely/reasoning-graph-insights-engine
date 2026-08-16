using Backend.Insights.Benchmarking;
using Backend.Insights.Contracts;
using Backend.Insights.Measurement;
using Backend.Insights.Workers;
using System.Text.Json;

namespace backend.Tests.Insights.Benchmarking;

[TestClass]
public sealed class SerialBenchmarkRunnerTests
{
    [TestMethod]
    [Timeout(30_000)]
    public async Task EquivalentQuickRuns_PreserveDatasetParameterAndResultDigests()
    {
        var runner = new SerialBenchmarkRunner(new BenchmarkOperationExecutor());
        var selection = new BenchmarkRunSelection(
            BenchmarkProfiles.QuickKey,
            ScenarioKey: "quick.strongest.balanced-1k");

        var first = (await runner.RunAsync(selection)).Runs.Single();
        var second = (await runner.RunAsync(selection)).Runs.Single();

        Assert.AreNotEqual(first.Manifest.RunId, second.Manifest.RunId);
        Assert.AreEqual(
            first.Manifest.Dataset.DatasetInputFingerprint,
            second.Manifest.Dataset.DatasetInputFingerprint);
        Assert.AreEqual(
            first.Manifest.CanonicalParameters.Digest,
            second.Manifest.CanonicalParameters.Digest);
        Assert.AreEqual(first.Outputs.Single().ResultDigest, second.Outputs.Single().ResultDigest);
        Assert.AreEqual(first.Export.Digests.OutputsDigest, first.DeserializedExport.Digests.OutputsDigest);
    }

    [TestMethod]
    public void QuickBrowserScenarios_AreConcreteAndKeepUnsafeExpansionNonExecutable()
    {
        var searches = BenchmarkScenarioRegistry.ForProfile(BenchmarkProfiles.QuickKey)
            .Where(scenario => scenario.OperationKey == OperationKeys.GraphSearch)
            .ToArray();

        Assert.AreEqual(2, searches.Length);
        Assert.IsTrue(searches.All(scenario =>
            scenario.ExecutionTarget == BenchmarkScenarioExecutionTarget.Browser &&
            scenario.BrowserJourney?.Action == BrowserJourneyActions.Search &&
            scenario.SkipReason is null));
        Assert.IsFalse(BenchmarkScenarioRegistry.All.Any(scenario =>
            scenario.Key == "quick.graph-search.deferred"));
        Assert.IsFalse(BenchmarkScenarioRegistry.All.Any(scenario =>
            scenario.BrowserJourney?.Action == BrowserJourneyActions.FullExpansion &&
            !scenario.IsSkipped &&
            DeterministicStressGraphFixtureFactory.Create(scenario.DatasetId).NodeCount > 1_000));
    }

    [TestMethod]
    [Timeout(15_000)]
    public async Task ConcurrentCalls_OnOneRunner_NeverExecuteMoreThanOneOperation()
    {
        var executor = new TrackingExecutor(TimeSpan.FromMilliseconds(150));
        var runner = new SerialBenchmarkRunner(executor);
        var selection = new BenchmarkRunSelection(
            BenchmarkProfiles.QuickKey,
            ScenarioKey: "quick.strongest.balanced-1k");

        await Task.WhenAll(runner.RunAsync(selection), runner.RunAsync(selection));

        Assert.AreEqual(1, executor.MaximumConcurrency);
    }

    [TestMethod]
    public async Task CallerCancellation_IsReturnedAsATerminalPortableRun()
    {
        using var cancellation = new CancellationTokenSource();
        var runner = new SerialBenchmarkRunner(new CancellationExecutor(cancellation));

        var result = (await runner.RunAsync(new BenchmarkRunSelection(
            BenchmarkProfiles.QuickKey,
            ScenarioKey: "quick.strongest.balanced-1k"), cancellation.Token)).Runs.Single();

        Assert.AreEqual(ExecutionStatus.Cancelled, result.Manifest.Execution.Status);
        Assert.AreEqual(FailureKind.Cancellation, result.Manifest.Execution.Failure?.Kind);
        Assert.AreEqual(result.Export.Digests.ManifestDigest,
            result.DeserializedExport.Digests.ManifestDigest);
    }

    [DataTestMethod]
    [DataRow(ExecutionStatus.TimedOut, FailureKind.Timeout)]
    [DataRow(ExecutionStatus.Crashed, FailureKind.Crash)]
    public async Task TerminalWorkerFailures_RemainDistinctAndExportable(
        ExecutionStatus status,
        FailureKind kind)
    {
        var runner = new SerialBenchmarkRunner(new TerminalExecutor(status, kind));

        var result = (await runner.RunAsync(new BenchmarkRunSelection(
            BenchmarkProfiles.QuickKey,
            ScenarioKey: "quick.strongest.balanced-1k"))).Runs.Single();

        Assert.AreEqual(status, result.Manifest.Execution.Status);
        Assert.AreEqual(kind, result.Manifest.Execution.Failure?.Kind);
        Assert.AreEqual(result.Export.Digests.ManifestDigest,
            result.DeserializedExport.Digests.ManifestDigest);
    }

    [TestMethod]
    public void ScenarioRegistry_RequiresIsolationForHazardsAndLimitsExact()
    {
        var singlePair = BenchmarkScenarioRegistry.Get(
            "quick.single-pair.deep-1k.maximum");
        Assert.IsTrue(singlePair.RequiresIsolation);
        Assert.AreEqual("n-00064", singlePair.Parameters.GetProperty("startNodeId").GetString());
        Assert.IsTrue(BenchmarkScenarioRegistry.Get(
            "quick.robustness.deep-1k").RequiresIsolation);
        var exact = BenchmarkScenarioRegistry.Get("quick.counter.exact.balanced-1k");
        Assert.AreEqual(8, exact.Parameters.GetProperty("candidateLimit").GetInt32());
    }

    [TestMethod]
    public async Task UnsafeExactOverrideWithoutCandidateLimit_IsValidationFailureAndNeverExecutes()
    {
        var executor = new NeverExecutor();
        var runner = new SerialBenchmarkRunner(executor);
        var parameters = JsonSerializer.SerializeToElement(new
        {
            targetNodeId = "n-00015",
            requestedStrategy = OperationStrategyNames.Exact,
            thresholdLogOdds = -1m,
            autoCandidateCutoff = (int?)null
        });

        var result = (await runner.RunAsync(new BenchmarkRunSelection(
            BenchmarkProfiles.QuickKey,
            ScenarioKey: "quick.counter.greedy.balanced-1k",
            Parameters: parameters,
            Strategy: OperationStrategyNames.Exact))).Runs.Single();

        Assert.AreEqual(ExecutionStatus.Failed, result.Manifest.Execution.Status);
        Assert.AreEqual(FailureKind.Validation, result.Manifest.Execution.Failure?.Kind);
        Assert.IsTrue(result.Manifest.ExecutionPolicy.IsolatedWorker);
        Assert.AreEqual(0, executor.CallCount);
    }

    [TestMethod]
    public async Task IsolatedCommandResolutionFailure_IsStructuredAndExportable()
    {
        var executor = new BenchmarkOperationExecutor(
            workerCommandProvider: new ThrowingCommandProvider());
        var runner = new SerialBenchmarkRunner(executor);

        var result = (await runner.RunAsync(new BenchmarkRunSelection(
            BenchmarkProfiles.QuickKey,
            ScenarioKey: "quick.single-pair.deep-1k.maximum"))).Runs.Single();

        Assert.AreEqual(ExecutionStatus.Failed, result.Manifest.Execution.Status);
        Assert.AreEqual("benchmark-worker-supervision-failed", result.Manifest.Execution.Failure?.Code);
        Assert.IsTrue(result.Samples.Any(sample =>
            sample.Phase == InsightMeasurementPhases.WorkerSupervision));
        Assert.AreEqual(result.Export.Digests.SamplesDigest,
            result.DeserializedExport.Digests.SamplesDigest);
    }

    private sealed class TrackingExecutor : IBenchmarkOperationExecutor
    {
        private readonly TimeSpan _delay;
        private readonly BenchmarkOperationExecutor _inner = new();
        private int _current;
        private int _maximum;

        public TrackingExecutor(TimeSpan delay) => _delay = delay;

        public int MaximumConcurrency => Volatile.Read(ref _maximum);

        public async Task<BenchmarkOperationExecutionResult> ExecuteAsync(
            PreparedBenchmarkOperation operation,
            BenchmarkScenarioDefinition scenario,
            DeterministicStressGraphFixture fixture,
            BenchmarkProfileDefinition profile,
            TimeSpan timeout,
            CancellationToken cancellationToken)
        {
            var current = Interlocked.Increment(ref _current);
            InterlockedExtensions.Max(ref _maximum, current);
            try
            {
                await Task.Delay(_delay, cancellationToken);
                return await _inner.ExecuteAsync(
                    operation, scenario, fixture, profile, timeout, cancellationToken);
            }
            finally
            {
                Interlocked.Decrement(ref _current);
            }
        }
    }

    private sealed class TerminalExecutor : IBenchmarkOperationExecutor
    {
        private readonly ExecutionStatus _status;
        private readonly FailureKind _kind;

        public TerminalExecutor(ExecutionStatus status, FailureKind kind)
        {
            _status = status;
            _kind = kind;
        }

        public Task<BenchmarkOperationExecutionResult> ExecuteAsync(
            PreparedBenchmarkOperation operation,
            BenchmarkScenarioDefinition scenario,
            DeterministicStressGraphFixture fixture,
            BenchmarkProfileDefinition profile,
            TimeSpan timeout,
            CancellationToken cancellationToken)
        {
            var outcome = BenchmarkOperationExecutor.Failure(
                _status, _kind, $"simulated-{_status}", "Simulated worker outcome.");
            return Task.FromResult(new BenchmarkOperationExecutionResult(outcome, [], []));
        }
    }

    private sealed class CancellationExecutor : IBenchmarkOperationExecutor
    {
        private readonly CancellationTokenSource _source;

        public CancellationExecutor(CancellationTokenSource source) => _source = source;

        public Task<BenchmarkOperationExecutionResult> ExecuteAsync(
            PreparedBenchmarkOperation operation,
            BenchmarkScenarioDefinition scenario,
            DeterministicStressGraphFixture fixture,
            BenchmarkProfileDefinition profile,
            TimeSpan timeout,
            CancellationToken cancellationToken)
        {
            _source.Cancel();
            var outcome = BenchmarkOperationExecutor.Failure(
                ExecutionStatus.Cancelled,
                FailureKind.Cancellation,
                "simulated-cancellation",
                "Simulated caller cancellation.");
            return Task.FromResult(new BenchmarkOperationExecutionResult(outcome, [], []));
        }
    }

    private sealed class NeverExecutor : IBenchmarkOperationExecutor
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
            throw new AssertFailedException("Unsafe critical-counter execution was attempted.");
        }
    }

    private sealed class ThrowingCommandProvider : IAnalysisWorkerCommandProvider
    {
        public WorkerProcessCommand GetCommand() =>
            throw new FileNotFoundException("Simulated missing worker.");
    }
}

internal static class InterlockedExtensions
{
    public static void Max(ref int target, int value)
    {
        var current = Volatile.Read(ref target);
        while (current < value)
        {
            var observed = Interlocked.CompareExchange(ref target, value, current);
            if (observed == current) return;
            current = observed;
        }
    }
}
