using Backend.Insights.Benchmarking;
using Backend.Insights.Contracts;
using Backend.Insights.Measurement;
using Backend.Insights.Workers;
using Backend.Tests.WorkerFixture;

namespace backend.Tests.Insights.Benchmarking;

[TestClass]
public sealed class SerialBenchmarkRunnerWorkerProcessTests
{
    [TestMethod]
    [Timeout(30_000)]
    public async Task ProductionBoundedDeepSinglePairWorker_SucceedsWithoutLosingIsolation()
    {
        var result = (await new SerialBenchmarkRunner(
            new BenchmarkOperationExecutor()).RunAsync(new BenchmarkRunSelection(
                BenchmarkProfiles.QuickKey,
                ScenarioKey: "quick.single-pair.deep-1k.maximum"))).Runs.Single();

        Assert.IsTrue(result.Manifest.ExecutionPolicy.IsolatedWorker);
        Assert.AreEqual(ExecutionStatus.Succeeded, result.Manifest.Execution.Status);
        Assert.AreEqual(OperationStrategyNames.Maximum, result.Manifest.Strategy.Used);
        Assert.AreEqual(1L, result.Outputs.Single().TotalResultCardinality);
        Assert.IsTrue(result.Samples.Any(sample =>
            sample.Phase == InsightMeasurementPhases.WorkerSupervision));
    }

    [TestMethod]
    [Timeout(30_000)]
    public async Task ProductionDeepRobustnessWorker_RoundTripsCanonicalCardinalityAndSucceeds()
    {
        var scenario = BenchmarkScenarioRegistry.Get("quick.robustness.deep-1k");
        var fixture = DeterministicStressGraphFixtureFactory.Create(scenario.DatasetId);
        var operation = BenchmarkOperationRequestFactory.Create(
            scenario,
            fixture,
            scenario.Parameters,
            Guid.NewGuid(),
            Guid.NewGuid());
        var result = await new IsolatedWorkerRunner().RunAsync(
            new PublishedAnalysisWorkerCommandProvider().GetCommand(),
            operation.Request,
            new IsolatedWorkerRunOptions(TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(1)));

        Assert.AreEqual(ExecutionStatus.Succeeded, result.Execution.Status);
        Assert.AreEqual(0, result.ExitCode);
        Assert.IsTrue(result.ReceivedTerminalEvent);
        Assert.IsFalse(result.ForcedTermination);
        Assert.AreEqual(string.Empty, result.StandardError);
        var output = result.Outputs.Single();
        Assert.AreEqual(1_000L, output.TotalResultCardinality);
        Assert.IsTrue(output.Items.Count is > 0 and <= OperationResultEnvelope.MaximumRetainedItems);
        StringAssert.StartsWith(output.ResultDigest, "sha256:");
    }

    [DataTestMethod]
    [DataRow("ignore-cancel", ExecutionStatus.TimedOut, FailureKind.Timeout, "worker-timeout")]
    [DataRow("crash-after-partials", ExecutionStatus.Crashed, FailureKind.Crash, "worker-exited-without-terminal")]
    [Timeout(30_000)]
    public async Task ActualIsolatedWorkerFailure_PreservesPartialsAndRunnerAcceptsLaterCall(
        string workerMode,
        ExecutionStatus expectedStatus,
        FailureKind expectedFailureKind,
        string expectedFailureCode)
    {
        var executor = new BenchmarkOperationExecutor(
            workerCommandProvider: new FixtureWorkerCommandProvider(workerMode));
        var runner = new SerialBenchmarkRunner(executor);

        var failed = (await runner.RunAsync(new BenchmarkRunSelection(
            BenchmarkProfiles.QuickKey,
            ScenarioKey: "quick.single-pair.deep-1k.maximum",
            Timeout: TimeSpan.FromMilliseconds(400)))).Runs.Single();

        Assert.AreEqual(expectedStatus, failed.Manifest.Execution.Status);
        Assert.AreEqual(expectedFailureKind, failed.Manifest.Execution.Failure?.Kind);
        Assert.AreEqual(expectedFailureCode, failed.Manifest.Execution.Failure?.Code);
        Assert.IsTrue(failed.Samples.Any(sample =>
            sample.Phase == InsightMeasurementPhases.WorkerSupervision &&
            sample.TimingBoundaryProvenance == TimingBoundaryProvenance.DirectlyInstrumented),
            "The partial sample accepted before worker termination should remain in the export.");
        Assert.IsTrue(failed.Samples.Any(sample =>
            sample.Phase == InsightMeasurementPhases.WorkerSupervision &&
            sample.TimingBoundaryProvenance == TimingBoundaryProvenance.ExternallyObserved),
            "The parent process should retain its supervision measurement.");
        Assert.AreEqual(1, failed.Outputs.Count,
            "The partial output accepted before worker termination should remain in the export.");
        Assert.AreEqual("partial", failed.Outputs.Single().Items.Single().GetProperty("fixture").GetString());
        Assert.AreEqual(
            failed.Export.Digests.SamplesDigest,
            failed.DeserializedExport.Digests.SamplesDigest);
        Assert.AreEqual(
            failed.Export.Digests.OutputsDigest,
            failed.DeserializedExport.Digests.OutputsDigest);

        var later = (await runner.RunAsync(new BenchmarkRunSelection(
            BenchmarkProfiles.QuickKey,
            ScenarioKey: "quick.strongest.balanced-1k"))).Runs.Single();

        Assert.AreEqual(ExecutionStatus.Succeeded, later.Manifest.Execution.Status);
        Assert.AreEqual(1, later.Outputs.Count);
        Assert.AreEqual(
            later.Export.Digests.ManifestDigest,
            later.DeserializedExport.Digests.ManifestDigest);
    }

    private sealed class FixtureWorkerCommandProvider : IAnalysisWorkerCommandProvider
    {
        private readonly string _mode;

        public FixtureWorkerCommandProvider(string mode) => _mode = mode;

        public WorkerProcessCommand GetCommand()
        {
            var fixtureAssembly = typeof(WorkerFixtureMarker).Assembly.Location;
            var testRuntimeConfiguration = Path.Combine(
                AppContext.BaseDirectory,
                "backend.Tests.runtimeconfig.json");
            var testDependencies = Path.Combine(
                AppContext.BaseDirectory,
                "backend.Tests.deps.json");
            var dotNetHost = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH");
            if (string.IsNullOrWhiteSpace(dotNetHost))
            {
                dotNetHost = "dotnet";
            }

            return new WorkerProcessCommand(
                dotNetHost,
                [
                    "exec",
                    "--runtimeconfig",
                    testRuntimeConfiguration,
                    "--depsfile",
                    testDependencies,
                    fixtureAssembly,
                    "--mode",
                    _mode
                ],
                AppContext.BaseDirectory);
        }
    }
}
