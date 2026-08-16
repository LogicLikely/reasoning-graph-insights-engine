using System.Diagnostics;
using System.Text.Json;
using Backend.Insights.Contracts;
using Backend.Insights.Measurement;
using Backend.Insights.Workers;

namespace Backend.Insights.Benchmarking;

public sealed record BenchmarkOperationExecutionResult(
    ExecutionOutcome Execution,
    IReadOnlyList<RunSample> Samples,
    IReadOnlyList<CompactRunOutput> Outputs);

public interface IBenchmarkOperationExecutor
{
    Task<BenchmarkOperationExecutionResult> ExecuteAsync(
        PreparedBenchmarkOperation operation,
        BenchmarkScenarioDefinition scenario,
        DeterministicStressGraphFixture fixture,
        BenchmarkProfileDefinition profile,
        TimeSpan timeout,
        CancellationToken cancellationToken);
}

public sealed class BenchmarkOperationExecutor : IBenchmarkOperationExecutor
{
    public static MeasurementUnitContract StandardUnits { get; } =
        new("ms", "ms", "bytes", "bytes", "count", "ratio");

    private readonly AnalysisWorkerDispatcher _dispatcher;
    private readonly IsolatedWorkerRunner _workerRunner;
    private readonly IAnalysisWorkerCommandProvider _workerCommandProvider;

    public BenchmarkOperationExecutor(
        AnalysisWorkerDispatcher? dispatcher = null,
        IsolatedWorkerRunner? workerRunner = null,
        IAnalysisWorkerCommandProvider? workerCommandProvider = null)
    {
        _dispatcher = dispatcher ?? new AnalysisWorkerDispatcher();
        _workerRunner = workerRunner ?? new IsolatedWorkerRunner();
        _workerCommandProvider = workerCommandProvider ?? new PublishedAnalysisWorkerCommandProvider();
    }

    public async Task<BenchmarkOperationExecutionResult> ExecuteAsync(
        PreparedBenchmarkOperation operation,
        BenchmarkScenarioDefinition scenario,
        DeterministicStressGraphFixture fixture,
        BenchmarkProfileDefinition profile,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var started = Stopwatch.GetTimestamp();
        if (scenario.RequiresIsolation)
        {
            try
            {
                var result = await _workerRunner.RunAsync(
                    _workerCommandProvider.GetCommand(),
                    operation.Request,
                    new IsolatedWorkerRunOptions(timeout, profile.CancellationGracePeriod),
                    cancellationToken);
                var samples = result.Samples.Concat([
                    CreateSample(operation, scenario, fixture, result.Execution, result.Outputs.LastOrDefault(),
                        ElapsedMilliseconds(started), InsightMeasurementPhases.WorkerSupervision,
                        TimingBoundaryProvenance.ExternallyObserved)
                ]).ToArray();
                return new BenchmarkOperationExecutionResult(result.Execution, samples, result.Outputs);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                var execution = Failure(ExecutionStatus.Cancelled, FailureKind.Cancellation,
                    "benchmark-worker-cancelled", "The isolated benchmark worker was cancelled.");
                return new BenchmarkOperationExecutionResult(
                    execution,
                    [CreateSample(operation, scenario, fixture, execution, null,
                        ElapsedMilliseconds(started), InsightMeasurementPhases.WorkerSupervision,
                        TimingBoundaryProvenance.ExternallyObserved)],
                    []);
            }
            catch (Exception exception)
            {
                var execution = Failure(ExecutionStatus.Failed, FailureKind.Execution,
                    "benchmark-worker-supervision-failed",
                    "The isolated benchmark worker could not be supervised.",
                    exception.GetType().FullName);
                return new BenchmarkOperationExecutionResult(
                    execution,
                    [CreateSample(operation, scenario, fixture, execution, null,
                        ElapsedMilliseconds(started), InsightMeasurementPhases.WorkerSupervision,
                        TimingBoundaryProvenance.ExternallyObserved)],
                    []);
            }
        }

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(timeout);
        try
        {
            deadline.Token.ThrowIfCancellationRequested();
            var output = _dispatcher.Dispatch(operation.Request, deadline.Token);
            var execution = new ExecutionOutcome(ExecutionStatus.Succeeded);
            return new BenchmarkOperationExecutionResult(
                execution,
                [CreateSample(operation, scenario, fixture, execution, output,
                    ElapsedMilliseconds(started), InsightMeasurementPhases.OperationExecution,
                    TimingBoundaryProvenance.DirectlyInstrumented)],
                [output]);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            var execution = Failure(ExecutionStatus.Cancelled, FailureKind.Cancellation,
                "benchmark-operation-cancelled", "The benchmark operation was cancelled.");
            return new BenchmarkOperationExecutionResult(
                execution,
                [CreateSample(operation, scenario, fixture, execution, null,
                    ElapsedMilliseconds(started), InsightMeasurementPhases.OperationExecution,
                    TimingBoundaryProvenance.DirectlyInstrumented)],
                []);
        }
        catch (OperationCanceledException) when (deadline.IsCancellationRequested)
        {
            var execution = Failure(ExecutionStatus.TimedOut, FailureKind.Timeout,
                "benchmark-operation-timeout", "The benchmark operation exceeded its hard timeout.");
            return new BenchmarkOperationExecutionResult(
                execution,
                [CreateSample(operation, scenario, fixture, execution, null,
                    ElapsedMilliseconds(started), InsightMeasurementPhases.OperationExecution,
                    TimingBoundaryProvenance.DirectlyInstrumented)],
                []);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or JsonException)
        {
            var execution = ExecutionOutcome.ValidationFailed([
                new ValidationFailure("parameters", "benchmark-input-invalid", "The benchmark input failed validation.")
            ]);
            return new BenchmarkOperationExecutionResult(
                execution,
                [CreateSample(operation, scenario, fixture, execution, null,
                    ElapsedMilliseconds(started), InsightMeasurementPhases.OperationExecution,
                    TimingBoundaryProvenance.DirectlyInstrumented)],
                []);
        }
        catch (Exception exception)
        {
            var execution = Failure(ExecutionStatus.Failed, FailureKind.Execution,
                "benchmark-operation-failed", "The benchmark operation failed.", exception.GetType().FullName);
            return new BenchmarkOperationExecutionResult(
                execution,
                [CreateSample(operation, scenario, fixture, execution, null,
                    ElapsedMilliseconds(started), InsightMeasurementPhases.OperationExecution,
                    TimingBoundaryProvenance.DirectlyInstrumented)],
                []);
        }
    }

    private static RunSample CreateSample(
        PreparedBenchmarkOperation operation,
        BenchmarkScenarioDefinition scenario,
        DeterministicStressGraphFixture fixture,
        ExecutionOutcome execution,
        CompactRunOutput? output,
        decimal duration,
        string phase,
        TimingBoundaryProvenance provenance)
    {
        var candidateCount = ReadLong(output?.Summary, "candidateCount");
        var iterations = ReadLong(output?.Distribution, "evaluatedSubsetCount");
        var threshold = ReadBool(output?.Summary, "thresholdAttained");
        return new RunSample(
            operation.Request.RunId,
            operation.Request.SampleId,
            scenario.Key,
            scenario.OperationKey,
            InsightMeasurementLayers.BenchmarkOrchestration,
            phase,
            duration,
            0,
            new IterationClassification(
                IterationClassificationTokens.Measured,
                IterationClassificationTokens.Warm,
                IterationClassificationTokens.PostJit,
                IterationClassificationTokens.WarmCache),
            new SampleNodeCounts(fixture.NodeCount, fixture.NodeCount, fixture.NodeCount, null),
            new SampleEdgeCounts(fixture.EdgeCount, null,
                fixture.NodeCount == 0 ? null : (decimal)fixture.EdgeCount / fixture.NodeCount),
            new SampleSearchCounts(null, null),
            output?.TotalResultCardinality,
            new SampleTransportMeasurements(null, null, null, null),
            new RuntimeResourceMeasurements(null, null, null, null, null, "ms", null),
            execution,
            StandardUnits,
            provenance,
            new SampleOperationCounters(candidateCount, null, null, iterations, null, threshold));
    }

    private static long? ReadLong(JsonElement? element, string property) =>
        element is { ValueKind: JsonValueKind.Object } value && value.TryGetProperty(property, out var item) &&
        item.TryGetInt64(out var result) ? result : null;

    private static bool? ReadBool(JsonElement? element, string property) =>
        element is { ValueKind: JsonValueKind.Object } value && value.TryGetProperty(property, out var item) &&
        item.ValueKind is JsonValueKind.True or JsonValueKind.False ? item.GetBoolean() : null;

    private static decimal ElapsedMilliseconds(long started) =>
        (Stopwatch.GetTimestamp() - started) * 1000m / Stopwatch.Frequency;

    public static ExecutionOutcome Failure(
        ExecutionStatus status,
        FailureKind kind,
        string code,
        string message,
        string? exceptionType = null) => new(
            status,
            new FailureDetails(kind, code, message, exceptionType, false, []));
}
