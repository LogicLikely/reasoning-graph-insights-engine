using System.Text;
using System.Text.Json;
using Backend.Insights.Contracts;
using Backend.Insights.Measurement;
using Backend.Insights.Workers;

Console.InputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
Console.OutputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
Console.Error.Write(string.Empty);

return await WorkerFixture.RunAsync(args);

internal static class WorkerFixture
{
    public static async Task<int> RunAsync(IReadOnlyList<string>? arguments = null)
    {
        var requestLine = await Console.In.ReadLineAsync();
        if (requestLine is null)
        {
            return 64;
        }

        WorkerRequestFrame request;
        try
        {
            request = WorkerProtocolJson.DeserializeRequest(requestLine);
        }
        catch (WorkerProtocolException)
        {
            return 65;
        }

        var mode = ReadString(request.Input, "mode") ?? ReadModeArgument(arguments);
        if (string.IsNullOrWhiteSpace(mode))
        {
            return 66;
        }

        return mode switch
        {
            "success" => await SucceedAsync(request),
            "execution-failure" => await FailExecutionAsync(request),
            "validation-failure" => await FailValidationAsync(request),
            "cooperative-cancel" => await CooperativelyCancelAsync(request),
            "ignore-cancel" => await IgnoreCancellationAsync(request),
            "crash-after-partials" => await CrashAfterPartialsAsync(request),
            "malformed-after-sample" => await EmitMalformedAfterSampleAsync(request),
            "out-of-order" => await EmitOutOfOrderAsync(request),
            "correlation-mismatch" => await EmitCorrelationMismatchAsync(request),
            "exit-zero-no-terminal" => await ExitZeroWithoutTerminalAsync(request),
            "oversized-protocol-line" => await EmitOversizedProtocolLineAsync(request),
            "success-nonzero" => await SucceedWithNonzeroExitAsync(request),
            "bounded-stderr" => await EmitBoundedStandardErrorAsync(request),
            _ => 67
        };
    }

    private static async Task<int> SucceedAsync(WorkerRequestFrame request)
    {
        await EmitPartialsAsync(request);
        await EmitAsync(WorkerEventFrame.ForTerminal(
            2,
            request.RunId,
            request.SampleId,
            new ExecutionOutcome(ExecutionStatus.Succeeded)));
        return 0;
    }

    private static async Task<int> FailExecutionAsync(WorkerRequestFrame request)
    {
        await EmitAsync(WorkerEventFrame.ForSample(0, CreateSample(request)));
        await EmitAsync(WorkerEventFrame.ForTerminal(
            1,
            request.RunId,
            request.SampleId,
            Failure(
                ExecutionStatus.Failed,
                FailureKind.Execution,
                "fixture-execution-failure")));
        return 19;
    }

    private static async Task<int> FailValidationAsync(WorkerRequestFrame request)
    {
        var outcome = ExecutionOutcome.ValidationFailed(
        [
            new ValidationFailure("input.mode", "fixture-validation", "Fixture validation failed.")
        ]);
        await EmitAsync(WorkerEventFrame.ForTerminal(
            0,
            request.RunId,
            request.SampleId,
            outcome));
        return 0;
    }

    private static async Task<int> CooperativelyCancelAsync(WorkerRequestFrame request)
    {
        await EmitAsync(WorkerEventFrame.ForSample(0, CreateSample(request)));
        var cancelLine = await Console.In.ReadLineAsync();
        if (cancelLine is null)
        {
            return 68;
        }

        var cancel = WorkerProtocolJson.DeserializeCancel(cancelLine);
        if (cancel.RunId != request.RunId || cancel.SampleId != request.SampleId)
        {
            return 69;
        }

        await Console.Error.WriteAsync($"cancel:{ToToken(cancel.Reason)}");
        await Console.Error.FlushAsync();
        await EmitAsync(WorkerEventFrame.ForTerminal(
            1,
            request.RunId,
            request.SampleId,
            Failure(
                ExecutionStatus.Cancelled,
                FailureKind.Cancellation,
                "fixture-cancelled")));
        return 0;
    }

    private static async Task<int> IgnoreCancellationAsync(WorkerRequestFrame request)
    {
        await EmitPartialsAsync(request);
        await Task.Delay(Timeout.InfiniteTimeSpan);
        return 70;
    }

    private static async Task<int> CrashAfterPartialsAsync(WorkerRequestFrame request)
    {
        await EmitPartialsAsync(request);
        Environment.Exit(23);
        return 23;
    }

    private static async Task<int> EmitMalformedAfterSampleAsync(WorkerRequestFrame request)
    {
        await EmitAsync(WorkerEventFrame.ForSample(0, CreateSample(request)));
        await Console.Out.WriteLineAsync("{not-json");
        await Console.Out.FlushAsync();
        return 0;
    }

    private static async Task<int> EmitOutOfOrderAsync(WorkerRequestFrame request)
    {
        await EmitAsync(WorkerEventFrame.ForSample(1, CreateSample(request)));
        return 0;
    }

    private static async Task<int> EmitCorrelationMismatchAsync(WorkerRequestFrame request)
    {
        await EmitAsync(WorkerEventFrame.ForTerminal(
            0,
            Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            request.SampleId,
            new ExecutionOutcome(ExecutionStatus.Succeeded)));
        return 0;
    }

    private static async Task<int> SucceedWithNonzeroExitAsync(WorkerRequestFrame request)
    {
        await EmitPartialsAsync(request);
        await EmitAsync(WorkerEventFrame.ForTerminal(
            2,
            request.RunId,
            request.SampleId,
            new ExecutionOutcome(ExecutionStatus.Succeeded)));
        return 29;
    }

    private static async Task<int> ExitZeroWithoutTerminalAsync(WorkerRequestFrame request)
    {
        await EmitAsync(WorkerEventFrame.ForSample(0, CreateSample(request)));
        return 0;
    }

    private static async Task<int> EmitOversizedProtocolLineAsync(WorkerRequestFrame request)
    {
        await EmitAsync(WorkerEventFrame.ForSample(0, CreateSample(request)));
        await Console.Out.WriteLineAsync(new string('x', 8_192));
        await Console.Out.FlushAsync();
        return 0;
    }

    private static async Task<int> EmitBoundedStandardErrorAsync(WorkerRequestFrame request)
    {
        await Console.Error.WriteAsync("visible\0" + new string('x', 2_048));
        await Console.Error.FlushAsync();
        await EmitAsync(WorkerEventFrame.ForTerminal(
            0,
            request.RunId,
            request.SampleId,
            new ExecutionOutcome(ExecutionStatus.Succeeded)));
        return 0;
    }

    private static async Task EmitPartialsAsync(WorkerRequestFrame request)
    {
        await EmitAsync(WorkerEventFrame.ForSample(0, CreateSample(request)));
        await EmitAsync(WorkerEventFrame.ForOutput(1, CreateOutput(request)));
    }

    private static async Task EmitAsync(WorkerEventFrame frame)
    {
        await Console.Out.WriteLineAsync(WorkerProtocolJson.Serialize(frame));
        await Console.Out.FlushAsync();
    }

    private static RunSample CreateSample(WorkerRequestFrame request)
    {
        return new RunSample(
            request.RunId,
            request.SampleId,
            ReadScenarioKey(request),
            request.OperationKey,
            InsightMeasurementLayers.BenchmarkOrchestration,
            InsightMeasurementPhases.WorkerSupervision,
            1m,
            0,
            new IterationClassification(
                IterationClassificationTokens.Measured,
                IterationClassificationTokens.Warm,
                IterationClassificationTokens.PostJit,
                IterationClassificationTokens.WarmCache),
            new SampleNodeCounts(null, null, null, null),
            new SampleEdgeCounts(null, null, null),
            new SampleSearchCounts(null, null),
            1,
            new SampleTransportMeasurements(null, null, null, null),
            new RuntimeResourceMeasurements(null, null, null, null, null, "ms", null),
            new ExecutionOutcome(ExecutionStatus.Succeeded),
            StandardUnits(),
            TimingBoundaryProvenance.DirectlyInstrumented,
            null);
    }

    private static CompactRunOutput CreateOutput(WorkerRequestFrame request)
    {
        var item = JsonSerializer.SerializeToElement(new { fixture = "partial" });
        var graph = request.Input.TryGetProperty("graph", out var graphElement)
            ? graphElement
            : default;
        var graphSlug = ReadString(graph, "slug") ?? "worker-fixture";
        var graphId = graph.ValueKind == JsonValueKind.Object &&
                      graph.TryGetProperty("id", out var graphIdElement) &&
                      graphIdElement.TryGetInt32(out var parsedGraphId)
            ? parsedGraphId.ToString(System.Globalization.CultureInfo.InvariantCulture)
            : null;
        var requestedStrategy = ReadString(request.Input, "requestedStrategy");
        return new CompactRunOutput(
            request.RunId,
            request.SampleId,
            ReadScenarioKey(request),
            request.OperationKey,
            request.AlgorithmSemanticIdentity,
            new StrategySelection(requestedStrategy, requestedStrategy),
            new GraphTargetIdentifiers(
                graphSlug,
                graphId,
                ReadString(request.Input, "targetNodeId") ?? ReadString(request.Input, "changedNodeId"),
                Array.Empty<string>()),
            request.CanonicalParameters,
            new ExecutionOutcome(ExecutionStatus.Succeeded),
            JsonSerializer.SerializeToElement(new { accepted = true }),
            JsonSerializer.SerializeToElement(new { }),
            1,
            [item],
            CanonicalJson.ComputeSha256(new[] { item }),
            null,
            Array.Empty<OrderedPathProjection>());
    }

    private static ExecutionOutcome Failure(
        ExecutionStatus status,
        FailureKind kind,
        string code)
    {
        return new ExecutionOutcome(
            status,
            new FailureDetails(
                kind,
                code,
                "Fixture failure.",
                null,
                false,
                Array.Empty<ValidationFailure>()));
    }

    private static MeasurementUnitContract StandardUnits()
    {
        return new MeasurementUnitContract("ms", "ms", "bytes", "bytes", "count", "ratio");
    }

    private static string ToToken(WorkerCancellationReason reason)
    {
        return reason switch
        {
            WorkerCancellationReason.UserCancellation => "user-cancellation",
            WorkerCancellationReason.Timeout => "timeout",
            _ => "unknown"
        };
    }

    private static string ReadScenarioKey(WorkerRequestFrame request) =>
        ReadString(request.Input, "scenarioKey") ?? "worker.fixture";

    private static string? ReadString(JsonElement value, string propertyName) =>
        value.ValueKind == JsonValueKind.Object &&
        value.TryGetProperty(propertyName, out var property) &&
        property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;

    private static string? ReadModeArgument(IReadOnlyList<string>? arguments)
    {
        if (arguments is null)
        {
            return null;
        }

        for (var index = 0; index + 1 < arguments.Count; index++)
        {
            if (string.Equals(arguments[index], "--mode", StringComparison.Ordinal))
            {
                return arguments[index + 1];
            }
        }

        return null;
    }
}

namespace Backend.Tests.WorkerFixture
{
    public sealed class WorkerFixtureMarker;
}
