using System.Diagnostics;
using System.Text.Json;
using Backend.Insights.Contracts;
using Backend.Insights.Workers;
using Backend.Tests.WorkerFixture;

namespace backend.Tests.Insights.Workers;

[TestClass]
public class IsolatedWorkerRunnerTests
{
    private static readonly TimeSpan NormalTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan NormalGrace = TimeSpan.FromSeconds(2);

    [TestMethod]
    [Timeout(15_000)]
    public async Task RunAsync_SuccessAcceptsOrderedPartialSampleAndOutput()
    {
        var result = await RunFixtureAsync("success");

        Assert.AreEqual(ExecutionStatus.Succeeded, result.Execution.Status);
        Assert.AreEqual(1, result.Samples.Count);
        Assert.AreEqual(1, result.Outputs.Count);
        Assert.AreEqual(0, result.ExitCode);
        Assert.IsTrue(result.ReceivedTerminalEvent);
        Assert.IsFalse(result.ForcedTermination);
        AssertProcessWasReaped(result);
    }

    [TestMethod]
    [Timeout(15_000)]
    public async Task RunAsync_ExecutionFailureTerminalRemainsAuthoritativeAfterNonzeroExit()
    {
        var result = await RunFixtureAsync("execution-failure");

        Assert.AreEqual(ExecutionStatus.Failed, result.Execution.Status);
        Assert.AreEqual(FailureKind.Execution, result.Execution.Failure?.Kind);
        Assert.AreEqual("fixture-execution-failure", result.Execution.Failure?.Code);
        Assert.AreEqual(19, result.ExitCode);
        Assert.AreEqual(1, result.Samples.Count);
        Assert.IsTrue(result.ReceivedTerminalEvent);
        AssertProcessWasReaped(result);
    }

    [TestMethod]
    [Timeout(15_000)]
    public async Task RunAsync_ValidationFailureRemainsDistinct()
    {
        var result = await RunFixtureAsync("validation-failure");

        Assert.AreEqual(ExecutionStatus.Failed, result.Execution.Status);
        Assert.AreEqual(FailureKind.Validation, result.Execution.Failure?.Kind);
        Assert.AreEqual(1, result.Execution.Failure?.ValidationFailures.Count);
        Assert.AreEqual(0, result.ExitCode);
        Assert.IsTrue(result.ReceivedTerminalEvent);
        AssertProcessWasReaped(result);
    }

    [TestMethod]
    [Timeout(15_000)]
    public async Task RunAsync_HardTimeoutKillsWorkerAndPreservesEveryAcceptedPartial()
    {
        var result = await RunFixtureAsync(
            "ignore-cancel",
            new IsolatedWorkerRunOptions(
                TimeSpan.FromSeconds(1),
                TimeSpan.FromMilliseconds(150)));

        Assert.AreEqual(ExecutionStatus.TimedOut, result.Execution.Status);
        Assert.AreEqual(FailureKind.Timeout, result.Execution.Failure?.Kind);
        Assert.AreEqual(1, result.Samples.Count);
        Assert.AreEqual(1, result.Outputs.Count);
        Assert.IsTrue(result.ForcedTermination);
        Assert.IsFalse(result.ReceivedTerminalEvent);
        AssertProcessWasReaped(result);
    }

    [TestMethod]
    [Timeout(15_000)]
    public async Task RunAsync_UserCancellationSendsControlFrameAndAllowsCooperativeExit()
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(1));

        var result = await RunFixtureAsync(
            "cooperative-cancel",
            new IsolatedWorkerRunOptions(
                TimeSpan.FromSeconds(10),
                TimeSpan.FromSeconds(2)),
            cancellation.Token);

        Assert.AreEqual(ExecutionStatus.Cancelled, result.Execution.Status);
        Assert.AreEqual(FailureKind.Cancellation, result.Execution.Failure?.Kind);
        Assert.AreEqual(1, result.Samples.Count);
        Assert.AreEqual(0, result.Outputs.Count);
        Assert.IsFalse(result.ForcedTermination);
        Assert.IsTrue(result.ReceivedTerminalEvent);
        Assert.AreEqual("cancel:user-cancellation", result.StandardError);
        AssertProcessWasReaped(result);
    }

    [TestMethod]
    [Timeout(15_000)]
    public async Task RunAsync_AbruptCrashAfterPartialsCannotTerminateHostOrLosePartials()
    {
        var result = await RunFixtureAsync("crash-after-partials");

        Assert.AreEqual(ExecutionStatus.Crashed, result.Execution.Status);
        Assert.AreEqual(FailureKind.Crash, result.Execution.Failure?.Kind);
        Assert.AreEqual("worker-exited-without-terminal", result.Execution.Failure?.Code);
        Assert.AreEqual(23, result.ExitCode);
        Assert.AreEqual(1, result.Samples.Count);
        Assert.AreEqual(1, result.Outputs.Count);
        Assert.IsFalse(result.ReceivedTerminalEvent);
        AssertProcessWasReaped(result);
    }

    [TestMethod]
    [Timeout(15_000)]
    public async Task RunAsync_ZeroExitWithoutTerminalIsStillCrash()
    {
        var result = await RunFixtureAsync("exit-zero-no-terminal");

        Assert.AreEqual(ExecutionStatus.Crashed, result.Execution.Status);
        Assert.AreEqual(FailureKind.Crash, result.Execution.Failure?.Kind);
        Assert.AreEqual("worker-exited-without-terminal", result.Execution.Failure?.Code);
        Assert.AreEqual(0, result.ExitCode);
        Assert.AreEqual(1, result.Samples.Count);
        Assert.IsFalse(result.ReceivedTerminalEvent);
        AssertProcessWasReaped(result);
    }

    [DataTestMethod]
    [DataRow("malformed-after-sample", 1)]
    [DataRow("out-of-order", 0)]
    [DataRow("correlation-mismatch", 0)]
    [Timeout(20_000)]
    public async Task RunAsync_RejectsMalformedOrderedOrMismatchedProtocol(
        string mode,
        int expectedAcceptedSamples)
    {
        var result = await RunFixtureAsync(mode);

        Assert.AreEqual(ExecutionStatus.Failed, result.Execution.Status);
        Assert.AreEqual(FailureKind.Execution, result.Execution.Failure?.Kind);
        Assert.AreEqual("worker-protocol-invalid", result.Execution.Failure?.Code);
        Assert.AreEqual(expectedAcceptedSamples, result.Samples.Count);
        Assert.AreEqual(0, result.Outputs.Count);
        AssertProcessWasReaped(result);
    }

    [TestMethod]
    [Timeout(15_000)]
    public async Task RunAsync_SucceededTerminalFollowedByNonzeroExitIsCrash()
    {
        var result = await RunFixtureAsync("success-nonzero");

        Assert.AreEqual(ExecutionStatus.Crashed, result.Execution.Status);
        Assert.AreEqual(FailureKind.Crash, result.Execution.Failure?.Kind);
        Assert.AreEqual("worker-nonzero-after-success", result.Execution.Failure?.Code);
        Assert.AreEqual(29, result.ExitCode);
        Assert.AreEqual(1, result.Samples.Count);
        Assert.AreEqual(1, result.Outputs.Count);
        Assert.IsTrue(result.ReceivedTerminalEvent);
        AssertProcessWasReaped(result);
    }

    [TestMethod]
    [Timeout(15_000)]
    public async Task RunAsync_BoundsProtocolLinesBeforeParsing()
    {
        var result = await RunFixtureAsync(
            "oversized-protocol-line",
            new IsolatedWorkerRunOptions(
                NormalTimeout,
                NormalGrace,
                maximumProtocolLineBytes: 4_096));

        Assert.AreEqual(ExecutionStatus.Failed, result.Execution.Status);
        Assert.AreEqual(FailureKind.Execution, result.Execution.Failure?.Kind);
        Assert.AreEqual("worker-protocol-invalid", result.Execution.Failure?.Code);
        Assert.AreEqual(1, result.Samples.Count);
        AssertProcessWasReaped(result);
    }

    [TestMethod]
    [Timeout(15_000)]
    public async Task RunAsync_BoundsAndSanitizesStandardErrorWhileContinuingToDrainIt()
    {
        var result = await RunFixtureAsync(
            "bounded-stderr",
            new IsolatedWorkerRunOptions(
                NormalTimeout,
                NormalGrace,
                maximumStandardErrorCharacters: 32));

        Assert.AreEqual(ExecutionStatus.Succeeded, result.Execution.Status);
        Assert.AreEqual(32, result.StandardError.Length);
        Assert.IsTrue(result.StandardErrorWasTruncated);
        Assert.IsFalse(result.StandardError.Contains('\0'));
        Assert.IsTrue(result.StandardError.Contains('\uFFFD'));
        AssertProcessWasReaped(result);
    }

    [TestMethod]
    [Timeout(15_000)]
    public async Task RunAsync_ProcessStartFailureIsFailedExecutionWithoutAChild()
    {
        var request = CreateRequest("success");
        var runner = new IsolatedWorkerRunner();

        var result = await runner.RunAsync(
            new WorkerProcessCommand($"missing-worker-{Guid.NewGuid():N}"),
            request,
            new IsolatedWorkerRunOptions(NormalTimeout, NormalGrace));

        Assert.AreEqual(ExecutionStatus.Failed, result.Execution.Status);
        Assert.AreEqual(FailureKind.Execution, result.Execution.Failure?.Kind);
        Assert.AreEqual("worker-start-failed", result.Execution.Failure?.Code);
        Assert.IsNull(result.ProcessId);
        Assert.IsTrue(result.ProcessExited);
    }

    private static async Task<IsolatedWorkerRunResult> RunFixtureAsync(
        string mode,
        IsolatedWorkerRunOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var runner = new IsolatedWorkerRunner();
        return await runner.RunAsync(
            CreateFixtureCommand(),
            CreateRequest(mode),
            options ?? new IsolatedWorkerRunOptions(NormalTimeout, NormalGrace),
            cancellationToken);
    }

    private static WorkerRequestFrame CreateRequest(string mode)
    {
        var parametersValue = JsonSerializer.SerializeToElement(new { fixture = true });
        return new WorkerRequestFrame(
            Guid.NewGuid(),
            Guid.NewGuid(),
            OperationKeys.GraphFetch,
            AlgorithmSemanticIdentities.GraphFetchV1,
            new CanonicalParameters(
                parametersValue,
                CanonicalJson.ComputeSha256(parametersValue)),
            JsonSerializer.SerializeToElement(new { mode }));
    }

    private static WorkerProcessCommand CreateFixtureCommand()
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
                fixtureAssembly
            ],
            AppContext.BaseDirectory);
    }

    private static void AssertProcessWasReaped(IsolatedWorkerRunResult result)
    {
        Assert.IsTrue(result.ProcessExited);
        Assert.IsNotNull(result.ProcessId);

        try
        {
            using var process = Process.GetProcessById(result.ProcessId.Value);
            Assert.IsTrue(process.HasExited, $"Worker process {result.ProcessId.Value} is still running.");
        }
        catch (ArgumentException)
        {
            // A reaped process no longer has an addressable process ID.
        }
    }
}
