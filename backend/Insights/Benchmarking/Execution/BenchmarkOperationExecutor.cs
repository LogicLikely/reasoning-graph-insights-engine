using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Backend.Insights.Analysis;
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
                return AddQualityComparison(
                    new BenchmarkOperationExecutionResult(result.Execution, samples, result.Outputs),
                    operation,
                    scenario,
                    fixture,
                    timeout,
                    started,
                    cancellationToken);
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
            return AddQualityComparison(new BenchmarkOperationExecutionResult(
                execution,
                [CreateSample(operation, scenario, fixture, execution, output,
                    ElapsedMilliseconds(started), InsightMeasurementPhases.OperationExecution,
                    TimingBoundaryProvenance.DirectlyInstrumented)],
                [output]), operation, scenario, fixture, timeout, started, cancellationToken);
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

    private static BenchmarkOperationExecutionResult AddQualityComparison(
        BenchmarkOperationExecutionResult primary,
        PreparedBenchmarkOperation operation,
        BenchmarkScenarioDefinition scenario,
        DeterministicStressGraphFixture fixture,
        TimeSpan timeout,
        long operationStarted,
        CancellationToken cancellationToken)
    {
        if (!scenario.MeasureQualityComparison ||
            primary.Execution.Status != ExecutionStatus.Succeeded)
        {
            return primary;
        }

        var started = Stopwatch.GetTimestamp();
        var remaining = timeout - Stopwatch.GetElapsedTime(operationStarted);
        if (remaining <= TimeSpan.Zero)
        {
            var timedOut = Failure(
                ExecutionStatus.TimedOut,
                FailureKind.Timeout,
                "exact-greedy-quality-comparison-timeout",
                "No scenario deadline remained for the exact-versus-greedy quality comparison.");
            return QualityFailure(primary, operation, scenario, fixture, timedOut, started);
        }

        using var qualityDeadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        qualityDeadline.CancelAfter(remaining);
        try
        {
            qualityDeadline.Token.ThrowIfCancellationRequested();
            var parameters = scenario.Parameters.Deserialize<CriticalCounterV1WorkerParameters>(
                CanonicalJson.CreateSerializerOptions())
                ?? throw new ArgumentException(
                    "Quality-comparison parameters do not match critical-counter-v1.",
                    nameof(scenario));
            if (parameters.CandidateLimit is not > 0)
            {
                throw new ArgumentException(
                    "Quality comparison requires a positive candidate limit.",
                    nameof(scenario));
            }

            var graph = fixture.CreateGraph();
            var candidateLimit = CriticalCounterCandidateGuard.RequireAtMost(
                graph,
                parameters.TargetNodeId,
                parameters.CandidateLimit.Value,
                qualityDeadline.Token);
            var comparison = new CriticalCounterV1Analyzer().CompareExactAndGreedy(
                graph,
                parameters.TargetNodeId,
                parameters.ThresholdLogOdds,
                qualityDeadline.Token);
            qualityDeadline.Token.ThrowIfCancellationRequested();

            var execution = new ExecutionOutcome(ExecutionStatus.Succeeded);
            var qualitySample = CreateQualitySample(
                operation,
                scenario,
                fixture,
                execution,
                comparison,
                candidateLimit.ActualCandidateCount,
                ElapsedMilliseconds(started));
            var enrichedOutputs = primary.Outputs
                .Select(output => EnrichWithQualityComparison(
                    output,
                    comparison,
                    candidateLimit,
                    parameters.ThresholdLogOdds))
                .ToArray();
            return new BenchmarkOperationExecutionResult(
                primary.Execution,
                primary.Samples.Concat([qualitySample]).ToArray(),
                enrichedOutputs);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            var execution = Failure(
                ExecutionStatus.Cancelled,
                FailureKind.Cancellation,
                "exact-greedy-quality-comparison-cancelled",
                "The exact-versus-greedy quality comparison was cancelled.");
            return QualityFailure(primary, operation, scenario, fixture, execution, started);
        }
        catch (OperationCanceledException) when (qualityDeadline.IsCancellationRequested)
        {
            var execution = Failure(
                ExecutionStatus.TimedOut,
                FailureKind.Timeout,
                "exact-greedy-quality-comparison-timeout",
                "The exact-versus-greedy quality comparison exceeded the remaining scenario deadline.");
            return QualityFailure(primary, operation, scenario, fixture, execution, started);
        }
        catch (Exception exception) when (
            exception is ArgumentException or InvalidOperationException or JsonException)
        {
            var execution = Failure(
                ExecutionStatus.Failed,
                FailureKind.Execution,
                "exact-greedy-quality-comparison-failed",
                "The bounded exact-versus-greedy quality comparison failed.",
                exception.GetType().FullName);
            return QualityFailure(primary, operation, scenario, fixture, execution, started);
        }
        catch (Exception exception)
        {
            // Quality evidence is ancillary to the already-completed canonical
            // operation. Preserve every primary sample and output even when an
            // unforeseen comparison or enrichment failure occurs.
            var execution = Failure(
                ExecutionStatus.Failed,
                FailureKind.Execution,
                "exact-greedy-quality-comparison-failed",
                "The bounded exact-versus-greedy quality comparison failed unexpectedly.",
                exception.GetType().FullName);
            return QualityFailure(primary, operation, scenario, fixture, execution, started);
        }
    }

    private static BenchmarkOperationExecutionResult QualityFailure(
        BenchmarkOperationExecutionResult primary,
        PreparedBenchmarkOperation operation,
        BenchmarkScenarioDefinition scenario,
        DeterministicStressGraphFixture fixture,
        ExecutionOutcome execution,
        long started) => new(
            execution,
            primary.Samples.Concat([
                CreateQualitySample(
                    operation,
                    scenario,
                    fixture,
                    execution,
                    null,
                    null,
                    ElapsedMilliseconds(started))
            ]).ToArray(),
            primary.Outputs);

    private static RunSample CreateQualitySample(
        PreparedBenchmarkOperation operation,
        BenchmarkScenarioDefinition scenario,
        DeterministicStressGraphFixture fixture,
        ExecutionOutcome execution,
        CriticalCounterV1QualityComparison? comparison,
        int? candidateCount,
        decimal duration) => new(
            operation.Request.RunId,
            operation.Request.SampleId,
            scenario.Key,
            scenario.OperationKey,
            InsightMeasurementLayers.BenchmarkOrchestration,
            InsightMeasurementPhases.ExactGreedyQualityComparison,
            duration,
            0,
            new IterationClassification(
                IterationClassificationTokens.Measured,
                IterationClassificationTokens.Warm,
                IterationClassificationTokens.PostJit,
                IterationClassificationTokens.WarmCache),
            new SampleNodeCounts(fixture.NodeCount, fixture.NodeCount, fixture.NodeCount, null),
            new SampleEdgeCounts(
                fixture.EdgeCount,
                null,
                fixture.NodeCount == 0 ? null : (decimal)fixture.EdgeCount / fixture.NodeCount),
            new SampleSearchCounts(null, null),
            comparison is null ? null : 1,
            new SampleTransportMeasurements(null, null, null, null),
            new RuntimeResourceMeasurements(null, null, null, null, null, "ms", null),
            execution,
            StandardUnits,
            TimingBoundaryProvenance.DirectlyInstrumented,
            new SampleOperationCounters(
                candidateCount,
                null,
                null,
                comparison is null
                    ? null
                    : checked(comparison.ExactEvaluationCount + comparison.GreedyEvaluationCount),
                null,
                null));

    private static CompactRunOutput EnrichWithQualityComparison(
        CompactRunOutput canonical,
        CriticalCounterV1QualityComparison comparison,
        CriticalCounterCandidateLimit candidateLimit,
        decimal thresholdLogOdds)
    {
        var summary = JsonNode.Parse(canonical.Summary.GetRawText())?.AsObject()
            ?? throw new JsonException("Critical-counter summary must be a JSON object.");
        summary["qualityComparisonRecorded"] = true;
        summary["qualityComparisonMethod"] = "critical-counter-v1-exact-versus-greedy";

        var distribution = JsonNode.Parse(canonical.Distribution.GetRawText())?.AsObject()
            ?? throw new JsonException("Critical-counter distribution must be a JSON object.");
        distribution["exactGreedyQuality"] = JsonSerializer.SerializeToNode(new
        {
            method = "CriticalCounterV1Analyzer.CompareExactAndGreedy",
            executionBoundary = "shared-runner-process",
            phase = InsightMeasurementPhases.ExactGreedyQualityComparison,
            timingBoundaryProvenance = TimingBoundaryProvenance.DirectlyInstrumented,
            tractability = new
            {
                configuredCandidateLimit = candidateLimit.MaximumCandidateCount,
                actualCandidateCount = candidateLimit.ActualCandidateCount,
                candidateNodeIds = candidateLimit.EligibleCandidateNodeIds
            },
            thresholdLogOdds = CanonicalResultNumber.Normalize(thresholdLogOdds),
            comparison.ExactThresholdAttained,
            comparison.GreedyThresholdAttained,
            comparison.ExactSelectedCardinality,
            comparison.GreedySelectedCardinality,
            comparison.CardinalityGapFromOptimal,
            comparison.SelectedSetOverlapCount,
            comparison.SelectedSetUnionCount,
            comparison.SelectedSetJaccardSimilarity,
            comparison.ExactBelowThresholdMargin,
            comparison.GreedyBelowThresholdMargin,
            comparison.ExactEvaluationCount,
            comparison.GreedyEvaluationCount,
            comparison.ExactResultDigest,
            comparison.GreedyResultDigest
        }, CanonicalJson.CreateSerializerOptions());

        return new CompactRunOutput(
            canonical.RunId,
            canonical.SampleId,
            canonical.ScenarioKey,
            canonical.OperationKey,
            canonical.AlgorithmSemanticIdentity,
            canonical.Strategy,
            canonical.Identifiers,
            canonical.CanonicalParameters,
            canonical.Execution,
            JsonSerializer.SerializeToElement(summary, CanonicalJson.CreateSerializerOptions()),
            JsonSerializer.SerializeToElement(distribution, CanonicalJson.CreateSerializerOptions()),
            canonical.TotalResultCardinality,
            canonical.Items,
            canonical.ResultDigest,
            canonical.FullResultArtifactReference,
            canonical.OrderedPaths);
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
