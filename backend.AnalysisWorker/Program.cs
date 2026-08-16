using System.Globalization;
using System.Text;
using System.Text.Json;
using Backend.Insights.Contracts;
using Backend.Insights.Workers;

namespace Backend.AnalysisWorker;

internal static class Program
{
    private static readonly Encoding Utf8WithoutBom = new UTF8Encoding(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);

    public static async Task<int> Main()
    {
        Console.InputEncoding = Utf8WithoutBom;
        Console.OutputEncoding = Utf8WithoutBom;
        var maximumProtocolLineBytes = ResolveMaximumProtocolLineBytes();

        var requestLine = await Console.In.ReadLineAsync();
        if (requestLine is null)
        {
            return 2;
        }

        WorkerRequestFrame request;
        try
        {
            request = WorkerProtocolJson.DeserializeRequest(requestLine);
        }
        catch
        {
            // A request that cannot establish trusted correlation cannot emit
            // a protocol event. The supervisor classifies the resulting EOF.
            return 2;
        }

        using var cancellation = new CancellationTokenSource();
        var cancellationFrameTask = Task.Factory.StartNew(
            () => ReadCancellationFrame(request),
            CancellationToken.None,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default);
        var workTask = Task.Run(() =>
        {
            var output = new AnalysisWorkerDispatcher().Dispatch(
                request,
                cancellation.Token);
            return CreateBoundedOutputEvent(
                output,
                maximumProtocolLineBytes,
                cancellation.Token);
        });

        try
        {
            var firstCompleted = await Task.WhenAny(workTask, cancellationFrameTask);
            if (firstCompleted == cancellationFrameTask)
            {
                var cancellationRead = await cancellationFrameTask;
                if (cancellationRead == CancellationReadResult.MatchingFrame)
                {
                    cancellation.Cancel();
                    await ObserveWorkCompletionAsync(workTask);
                    await WriteFrameAsync(WorkerEventFrame.ForTerminal(
                        0,
                        request.RunId,
                        request.SampleId,
                        CancellationOutcome()));
                    return 0;
                }

                if (cancellationRead == CancellationReadResult.InvalidFrame)
                {
                    cancellation.Cancel();
                    await ObserveWorkCompletionAsync(workTask);
                    await WriteFrameAsync(WorkerEventFrame.ForTerminal(
                        0,
                        request.RunId,
                        request.SampleId,
                        ExecutionFailure(
                            "analysis-worker-cancel-frame-invalid",
                            "The analysis worker received an invalid cancellation frame.")));
                    return 0;
                }
            }

            var boundedOutputEvent = await workTask;
            await WriteLineAsync(boundedOutputEvent.SerializedFrame);
            await WriteFrameAsync(WorkerEventFrame.ForTerminal(
                1,
                request.RunId,
                request.SampleId,
                new ExecutionOutcome(ExecutionStatus.Succeeded)));
            return 0;
        }
        catch (OperationCanceledException)
        {
            await WriteFrameAsync(WorkerEventFrame.ForTerminal(
                0,
                request.RunId,
                request.SampleId,
                CancellationOutcome()));
            return 0;
        }
        catch (Exception exception) when (IsValidationException(exception))
        {
            await WriteFrameAsync(WorkerEventFrame.ForTerminal(
                0,
                request.RunId,
                request.SampleId,
                ExecutionOutcome.ValidationFailed(
                [
                    new ValidationFailure(
                        "input",
                        "analysis-input-invalid",
                        "The analysis worker input failed validation.")
                ])));
            return 0;
        }
        catch (Exception exception)
        {
            await WriteFrameAsync(WorkerEventFrame.ForTerminal(
                0,
                request.RunId,
                request.SampleId,
                ExecutionFailure(
                    "analysis-worker-execution-failed",
                    "The analysis worker could not complete the requested operation.",
                    exception.GetType().FullName)));
            return 0;
        }
    }

    private static CancellationReadResult ReadCancellationFrame(WorkerRequestFrame request)
    {
        var line = Console.In.ReadLine();
        if (line is null)
        {
            return CancellationReadResult.EndOfInput;
        }

        try
        {
            var frame = WorkerProtocolJson.DeserializeCancel(line);
            return frame.RunId == request.RunId && frame.SampleId == request.SampleId
                ? CancellationReadResult.MatchingFrame
                : CancellationReadResult.InvalidFrame;
        }
        catch
        {
            return CancellationReadResult.InvalidFrame;
        }
    }

    private static async Task ObserveWorkCompletionAsync(Task<BoundedOutputEvent> workTask)
    {
        try
        {
            await workTask;
        }
        catch
        {
            // The terminal outcome is controlled by the accepted cancellation
            // frame, not by the cancellation exception's implementation text.
        }
    }

    private static async Task WriteFrameAsync(WorkerEventFrame frame)
    {
        await WriteLineAsync(WorkerProtocolJson.Serialize(frame));
    }

    private static async Task WriteLineAsync(string line)
    {
        await Console.Out.WriteLineAsync(line);
        await Console.Out.FlushAsync();
    }

    private static BoundedOutputEvent CreateBoundedOutputEvent(
        CompactRunOutput output,
        int maximumProtocolLineBytes,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var completeFrame = WorkerProtocolJson.Serialize(
            WorkerEventFrame.ForOutput(0, output));
        cancellationToken.ThrowIfCancellationRequested();
        if (Utf8WithoutBom.GetByteCount(completeFrame) <= maximumProtocolLineBytes)
        {
            return new BoundedOutputEvent(output, completeFrame);
        }

        BoundedOutputEvent? best = null;
        var lowerBound = 0;
        var upperBound = output.Items.Count;
        while (lowerBound <= upperBound)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var retainedItemCount = lowerBound + ((upperBound - lowerBound) / 2);
            var candidateOutput = CreateRetainedPrefix(
                output,
                retainedItemCount,
                cancellationToken);
            var candidateFrame = WorkerProtocolJson.Serialize(
                WorkerEventFrame.ForOutput(0, candidateOutput));
            cancellationToken.ThrowIfCancellationRequested();
            if (Utf8WithoutBom.GetByteCount(candidateFrame) <= maximumProtocolLineBytes)
            {
                best = new BoundedOutputEvent(candidateOutput, candidateFrame);
                lowerBound = retainedItemCount + 1;
            }
            else
            {
                upperBound = retainedItemCount - 1;
            }
        }

        return best ?? throw new InvalidOperationException(
            "The compact analysis output envelope exceeds the configured worker protocol line limit even with zero retained items.");
    }

    private static CompactRunOutput CreateRetainedPrefix(
        CompactRunOutput output,
        int retainedItemCount,
        CancellationToken cancellationToken)
    {
        var retainedItems = new JsonElement[retainedItemCount];
        for (var index = 0; index < retainedItems.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            retainedItems[index] = output.Items[index].Clone();
        }

        var retainedPathCount = output.OperationKey == OperationKeys.CounterCriticalSet
            ? retainedItemCount == 0 ? 0 : output.OrderedPaths.Count
            : Math.Min(retainedItemCount, output.OrderedPaths.Count);
        var retainedPaths = new OrderedPathProjection[retainedPathCount];
        for (var index = 0; index < retainedPaths.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var path = output.OrderedPaths[index];
            retainedPaths[index] = new OrderedPathProjection(
                Array.AsReadOnly(path.NodeIds.ToArray()),
                Array.AsReadOnly(path.EdgeIds.ToArray()),
                path.AccumulatedScore);
        }

        cancellationToken.ThrowIfCancellationRequested();
        return new CompactRunOutput(
            output.RunId,
            output.SampleId,
            output.ScenarioKey,
            output.OperationKey,
            output.AlgorithmSemanticIdentity,
            output.Strategy,
            output.Identifiers,
            output.CanonicalParameters,
            output.Execution,
            output.Summary.Clone(),
            output.Distribution.Clone(),
            output.TotalResultCardinality,
            retainedItems,
            output.ResultDigest,
            output.FullResultArtifactReference,
            retainedPaths);
    }

    private static int ResolveMaximumProtocolLineBytes()
    {
        var value = Environment.GetEnvironmentVariable(
            IsolatedWorkerRunOptions.MaximumProtocolLineBytesEnvironmentVariable);
        return int.TryParse(
                   value,
                   NumberStyles.None,
                   CultureInfo.InvariantCulture,
                   out var parsed) && parsed >= 256
            ? parsed
            : IsolatedWorkerRunOptions.DefaultMaximumProtocolLineBytes;
    }

    private static bool IsValidationException(Exception exception)
    {
        return exception is ArgumentException or
               JsonException or
               WorkerProtocolException or
               KeyNotFoundException;
    }

    private static ExecutionOutcome CancellationOutcome()
    {
        return new ExecutionOutcome(
            ExecutionStatus.Cancelled,
            new FailureDetails(
                FailureKind.Cancellation,
                "analysis-worker-cancelled",
                "The analysis worker cancelled the requested operation.",
                null,
                false,
                Array.Empty<ValidationFailure>()));
    }

    private static ExecutionOutcome ExecutionFailure(
        string code,
        string message,
        string? exceptionType = null)
    {
        return new ExecutionOutcome(
            ExecutionStatus.Failed,
            new FailureDetails(
                FailureKind.Execution,
                code,
                message,
                exceptionType,
                false,
                Array.Empty<ValidationFailure>()));
    }

    private enum CancellationReadResult
    {
        EndOfInput,
        MatchingFrame,
        InvalidFrame
    }

    private sealed record BoundedOutputEvent(
        CompactRunOutput Output,
        string SerializedFrame);
}

public sealed class AnalysisWorkerMarker;
