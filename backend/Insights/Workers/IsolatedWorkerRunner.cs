using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using Backend.Insights.Contracts;

namespace Backend.Insights.Workers;

public sealed class WorkerProcessCommand
{
    public WorkerProcessCommand(
        string fileName,
        IEnumerable<string>? arguments = null,
        string? workingDirectory = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        if (workingDirectory is not null && string.IsNullOrWhiteSpace(workingDirectory))
        {
            throw new ArgumentException("Worker working directory must not be blank.", nameof(workingDirectory));
        }

        FileName = fileName;
        Arguments = Array.AsReadOnly((arguments ?? []).ToArray());
        WorkingDirectory = workingDirectory;
    }

    public string FileName { get; }
    public IReadOnlyList<string> Arguments { get; }
    public string? WorkingDirectory { get; }
}

public sealed class IsolatedWorkerRunOptions
{
    public const int DefaultMaximumProtocolLineBytes = 1_048_576;
    public const int DefaultMaximumStandardErrorCharacters = 8_192;
    public const string MaximumProtocolLineBytesEnvironmentVariable =
        "LOGICLIKELY_INSIGHTS_ANALYSIS_WORKER_MAX_PROTOCOL_LINE_BYTES";

    public IsolatedWorkerRunOptions(
        TimeSpan timeout,
        TimeSpan cancellationGracePeriod,
        int maximumProtocolLineBytes = DefaultMaximumProtocolLineBytes,
        int maximumStandardErrorCharacters = DefaultMaximumStandardErrorCharacters)
    {
        if (timeout <= TimeSpan.Zero || timeout > TimeSpan.FromMilliseconds(int.MaxValue))
        {
            throw new ArgumentOutOfRangeException(
                nameof(timeout),
                timeout,
                "Worker timeout must be positive and no greater than Int32.MaxValue milliseconds.");
        }

        if (cancellationGracePeriod < TimeSpan.Zero ||
            cancellationGracePeriod > TimeSpan.FromMilliseconds(int.MaxValue))
        {
            throw new ArgumentOutOfRangeException(
                nameof(cancellationGracePeriod),
                cancellationGracePeriod,
                "Worker cancellation grace period must be non-negative and no greater than Int32.MaxValue milliseconds.");
        }

        ArgumentOutOfRangeException.ThrowIfLessThan(maximumProtocolLineBytes, 256);
        ArgumentOutOfRangeException.ThrowIfNegative(maximumStandardErrorCharacters);

        Timeout = timeout;
        CancellationGracePeriod = cancellationGracePeriod;
        MaximumProtocolLineBytes = maximumProtocolLineBytes;
        MaximumStandardErrorCharacters = maximumStandardErrorCharacters;
    }

    public TimeSpan Timeout { get; }
    public TimeSpan CancellationGracePeriod { get; }
    public int MaximumProtocolLineBytes { get; }
    public int MaximumStandardErrorCharacters { get; }
}

public sealed record IsolatedWorkerRunResult(
    ExecutionOutcome Execution,
    IReadOnlyList<RunSample> Samples,
    IReadOnlyList<CompactRunOutput> Outputs,
    int? ProcessId,
    int? ExitCode,
    bool ProcessExited,
    bool ForcedTermination,
    bool ReceivedTerminalEvent,
    string StandardError,
    bool StandardErrorWasTruncated);

public sealed class IsolatedWorkerRunner
{
    private static readonly Encoding Utf8WithoutBom = new UTF8Encoding(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);
    private static readonly TimeSpan CleanupWait = TimeSpan.FromSeconds(5);

    public async Task<IsolatedWorkerRunResult> RunAsync(
        WorkerProcessCommand command,
        WorkerRequestFrame request,
        IsolatedWorkerRunOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(options);

        if (cancellationToken.IsCancellationRequested)
        {
            return ResultWithoutProcess(CancelledOutcome());
        }

        using var process = new Process
        {
            StartInfo = CreateStartInfo(command, options.MaximumProtocolLineBytes),
            EnableRaisingEvents = true
        };

        try
        {
            if (!process.Start())
            {
                return ResultWithoutProcess(ExecutionFailure(
                    "worker-start-failed",
                    "The isolated worker process did not start."));
            }
        }
        catch (Exception exception)
        {
            return ResultWithoutProcess(ExecutionFailure(
                "worker-start-failed",
                "The isolated worker process could not be started.",
                exception.GetType().FullName));
        }

        var processId = process.Id;
        var protocolState = new WorkerProtocolReadState();
        var standardErrorCapture = new BoundedStandardErrorCapture(
            options.MaximumStandardErrorCharacters);
        using var ioCancellation = new CancellationTokenSource();

        var protocolTask = ReadProtocolAsync(
            process.StandardOutput,
            request,
            options.MaximumProtocolLineBytes,
            protocolState,
            ioCancellation.Token);
        var standardErrorTask = DrainStandardErrorAsync(
            process.StandardError,
            standardErrorCapture,
            ioCancellation.Token);
        var exitTask = process.WaitForExitAsync();
        var forcedTermination = false;

        try
        {
            await process.StandardInput.WriteLineAsync(WorkerProtocolJson.Serialize(request));
            await process.StandardInput.FlushAsync();

            var cancellationSignal = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            using var cancellationRegistration = cancellationToken.Register(
                static state => ((TaskCompletionSource<bool>)state!).TrySetResult(true),
                cancellationSignal);
            var timeoutTask = Task.Delay(options.Timeout);

            var completedTask = await Task.WhenAny(
                exitTask,
                protocolState.FaultSignal.Task,
                cancellationSignal.Task,
                timeoutTask);

            if (completedTask == cancellationSignal.Task || completedTask == timeoutTask)
            {
                var cancellationWins = cancellationToken.IsCancellationRequested;
                var reason = cancellationWins
                    ? WorkerCancellationReason.UserCancellation
                    : WorkerCancellationReason.Timeout;

                await TrySendCancellationAsync(process, request, reason);
                SafeCloseStandardInput(process);

                if (!await WaitForExitWithinAsync(exitTask, options.CancellationGracePeriod))
                {
                    forcedTermination = TryKillProcessTree(process);
                    await WaitForExitWithinAsync(exitTask, CleanupWait);
                }

                if (!HasExited(process))
                {
                    ioCancellation.Cancel();
                }

                await CompleteReadersAsync(protocolTask, standardErrorTask);

                return BuildResult(
                    cancellationWins ? CancelledOutcome() : TimedOutOutcome(),
                    protocolState,
                    standardErrorCapture,
                    process,
                    processId,
                    forcedTermination);
            }

            if (completedTask == protocolState.FaultSignal.Task)
            {
                forcedTermination = TryKillProcessTree(process);
                await WaitForExitWithinAsync(exitTask, CleanupWait);
                if (!HasExited(process))
                {
                    ioCancellation.Cancel();
                }

                SafeCloseStandardInput(process);
                await CompleteReadersAsync(protocolTask, standardErrorTask);

                return BuildResult(
                    ExecutionFailure(
                        "worker-protocol-invalid",
                        "The isolated worker emitted an invalid protocol frame.",
                        protocolState.Error?.GetType().FullName),
                    protocolState,
                    standardErrorCapture,
                    process,
                    processId,
                    forcedTermination);
            }

            SafeCloseStandardInput(process);
            await protocolTask;
            await standardErrorTask;

            if (protocolState.Error is not null)
            {
                return BuildResult(
                    ExecutionFailure(
                        "worker-protocol-invalid",
                        "The isolated worker emitted an invalid protocol frame.",
                        protocolState.Error.GetType().FullName),
                    protocolState,
                    standardErrorCapture,
                    process,
                    processId,
                    forcedTermination);
            }

            var exitCode = GetExitCode(process);
            var execution = ClassifyCompletedWorker(protocolState.Terminal, exitCode);
            return BuildResult(
                execution,
                protocolState,
                standardErrorCapture,
                process,
                processId,
                forcedTermination);
        }
        catch (Exception exception)
        {
            forcedTermination |= TryKillProcessTree(process);
            await WaitForExitWithinAsync(exitTask, CleanupWait);
            if (!HasExited(process))
            {
                ioCancellation.Cancel();
            }

            SafeCloseStandardInput(process);
            await CompleteReadersAsync(protocolTask, standardErrorTask);

            return BuildResult(
                ExecutionFailure(
                    "worker-runner-failed",
                    "The isolated worker runner failed while supervising the process.",
                    exception.GetType().FullName),
                protocolState,
                standardErrorCapture,
                process,
                processId,
                forcedTermination);
        }
        finally
        {
            if (!HasExited(process))
            {
                TryKillProcessTree(process);
                await WaitForExitWithinAsync(exitTask, CleanupWait);
            }

            ioCancellation.Cancel();
        }
    }

    private static ProcessStartInfo CreateStartInfo(
        WorkerProcessCommand command,
        int maximumProtocolLineBytes)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = command.FileName,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardInputEncoding = Utf8WithoutBom,
            StandardOutputEncoding = Utf8WithoutBom,
            StandardErrorEncoding = Utf8WithoutBom
        };

        if (command.WorkingDirectory is not null)
        {
            startInfo.WorkingDirectory = command.WorkingDirectory;
        }

        startInfo.Environment[
            IsolatedWorkerRunOptions.MaximumProtocolLineBytesEnvironmentVariable] =
            maximumProtocolLineBytes.ToString(CultureInfo.InvariantCulture);

        foreach (var argument in command.Arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        return startInfo;
    }

    private static async Task ReadProtocolAsync(
        StreamReader reader,
        WorkerRequestFrame request,
        int maximumLineBytes,
        WorkerProtocolReadState state,
        CancellationToken cancellationToken)
    {
        try
        {
            long expectedSequence = 0;
            while (true)
            {
                var line = await ReadBoundedLineAsync(reader, maximumLineBytes, cancellationToken);
                if (line is null)
                {
                    return;
                }

                var frame = WorkerProtocolJson.DeserializeEvent(line);
                if (frame.RunId != request.RunId || frame.SampleId != request.SampleId)
                {
                    throw new WorkerProtocolException(
                        "Worker event correlation does not match the invocation request.");
                }

                if (frame.Sequence != expectedSequence)
                {
                    throw new WorkerProtocolException(
                        $"Worker event sequence must be contiguous from zero; expected {expectedSequence}.");
                }

                if (state.Terminal is not null)
                {
                    throw new WorkerProtocolException("Worker emitted an event after its terminal event.");
                }

                expectedSequence++;
                switch (frame.EventKind)
                {
                    case WorkerEventKind.Sample:
                        if (!string.Equals(
                                frame.Sample!.OperationKey,
                                request.OperationKey,
                                StringComparison.Ordinal))
                        {
                            throw new WorkerProtocolException(
                                "Worker sample operation does not match the invocation request.");
                        }

                        state.Samples.Add(frame.Sample);
                        break;

                    case WorkerEventKind.Output:
                        if (!string.Equals(
                                frame.Output!.OperationKey,
                                request.OperationKey,
                                StringComparison.Ordinal) ||
                            !string.Equals(
                                frame.Output.AlgorithmSemanticIdentity,
                                request.AlgorithmSemanticIdentity,
                                StringComparison.Ordinal))
                        {
                            throw new WorkerProtocolException(
                                "Worker output identity does not match the invocation request.");
                        }

                        state.Outputs.Add(frame.Output);
                        break;

                    case WorkerEventKind.Terminal:
                        state.Terminal = frame.Terminal;
                        break;

                    default:
                        throw new WorkerProtocolException("Worker emitted an unknown event kind.");
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            state.Error = exception;
            state.FaultSignal.TrySetResult(true);
        }
    }

    private static async Task<string?> ReadBoundedLineAsync(
        StreamReader reader,
        int maximumLineBytes,
        CancellationToken cancellationToken)
    {
        var builder = new StringBuilder(Math.Min(maximumLineBytes, 4_096));
        var oneCharacter = new char[1];

        while (true)
        {
            var read = await reader.ReadAsync(oneCharacter.AsMemory(0, 1), cancellationToken);
            if (read == 0)
            {
                if (builder.Length == 0)
                {
                    return null;
                }

                break;
            }

            var character = oneCharacter[0];
            if (character == '\n')
            {
                if (builder.Length > 0 && builder[^1] == '\r')
                {
                    builder.Length--;
                }

                break;
            }

            if (builder.Length >= maximumLineBytes)
            {
                throw new WorkerProtocolException(
                    $"Worker protocol line exceeded the {maximumLineBytes}-byte limit.");
            }

            builder.Append(character);
        }

        var line = builder.ToString();
        if (Utf8WithoutBom.GetByteCount(line) > maximumLineBytes)
        {
            throw new WorkerProtocolException(
                $"Worker protocol line exceeded the {maximumLineBytes}-byte limit.");
        }

        return line;
    }

    private static async Task DrainStandardErrorAsync(
        StreamReader reader,
        BoundedStandardErrorCapture capture,
        CancellationToken cancellationToken)
    {
        var buffer = new char[1_024];
        try
        {
            while (true)
            {
                var read = await reader.ReadAsync(buffer.AsMemory(), cancellationToken);
                if (read == 0)
                {
                    return;
                }

                capture.Append(buffer.AsSpan(0, read));
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (IOException)
        {
            // A process killed during cancellation may close its pipe mid-read.
        }
    }

    private static async Task TrySendCancellationAsync(
        Process process,
        WorkerRequestFrame request,
        WorkerCancellationReason reason)
    {
        try
        {
            if (HasExited(process))
            {
                return;
            }

            var frame = new WorkerCancelFrame(request.RunId, request.SampleId, reason);
            await process.StandardInput.WriteLineAsync(WorkerProtocolJson.Serialize(frame));
            await process.StandardInput.FlushAsync();
        }
        catch (Exception exception) when (
            exception is IOException or InvalidOperationException or ObjectDisposedException)
        {
            // The process can exit between the state check and the control-frame write.
        }
    }

    private static ExecutionOutcome ClassifyCompletedWorker(
        ExecutionOutcome? terminal,
        int? exitCode)
    {
        if (terminal is null)
        {
            return CrashedOutcome("worker-exited-without-terminal");
        }

        if (terminal.Status == ExecutionStatus.Succeeded && exitCode != 0)
        {
            return CrashedOutcome("worker-nonzero-after-success");
        }

        return terminal;
    }

    private static IsolatedWorkerRunResult BuildResult(
        ExecutionOutcome execution,
        WorkerProtocolReadState protocolState,
        BoundedStandardErrorCapture standardErrorCapture,
        Process process,
        int processId,
        bool forcedTermination)
    {
        return new IsolatedWorkerRunResult(
            execution,
            new ReadOnlyCollection<RunSample>(protocolState.Samples.ToArray()),
            new ReadOnlyCollection<CompactRunOutput>(protocolState.Outputs.ToArray()),
            processId,
            GetExitCode(process),
            HasExited(process),
            forcedTermination,
            protocolState.Terminal is not null,
            standardErrorCapture.Value,
            standardErrorCapture.WasTruncated);
    }

    private static IsolatedWorkerRunResult ResultWithoutProcess(ExecutionOutcome execution)
    {
        return new IsolatedWorkerRunResult(
            execution,
            Array.Empty<RunSample>(),
            Array.Empty<CompactRunOutput>(),
            null,
            null,
            true,
            false,
            false,
            string.Empty,
            false);
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

    private static ExecutionOutcome TimedOutOutcome()
    {
        return new ExecutionOutcome(
            ExecutionStatus.TimedOut,
            new FailureDetails(
                FailureKind.Timeout,
                "worker-timeout",
                "The isolated worker exceeded its hard timeout.",
                null,
                true,
                Array.Empty<ValidationFailure>()));
    }

    private static ExecutionOutcome CancelledOutcome()
    {
        return new ExecutionOutcome(
            ExecutionStatus.Cancelled,
            new FailureDetails(
                FailureKind.Cancellation,
                "worker-cancelled",
                "The isolated worker was cancelled by its caller.",
                null,
                false,
                Array.Empty<ValidationFailure>()));
    }

    private static ExecutionOutcome CrashedOutcome(string code)
    {
        return new ExecutionOutcome(
            ExecutionStatus.Crashed,
            new FailureDetails(
                FailureKind.Crash,
                code,
                "The isolated worker exited unexpectedly before a successful completion was established.",
                null,
                true,
                Array.Empty<ValidationFailure>()));
    }

    private static bool TryKillProcessTree(Process process)
    {
        try
        {
            if (HasExited(process))
            {
                return false;
            }

            process.Kill(entireProcessTree: true);
            return true;
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or NotSupportedException or System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }

    private static async Task<bool> WaitForExitWithinAsync(Task exitTask, TimeSpan timeout)
    {
        if (exitTask.IsCompleted)
        {
            await exitTask;
            return true;
        }

        if (timeout == TimeSpan.Zero)
        {
            return false;
        }

        var completed = await Task.WhenAny(exitTask, Task.Delay(timeout));
        if (completed != exitTask)
        {
            return false;
        }

        await exitTask;
        return true;
    }

    private static async Task CompleteReadersAsync(params Task[] readers)
    {
        foreach (var reader in readers)
        {
            try
            {
                await reader;
            }
            catch (OperationCanceledException)
            {
            }
            catch (IOException)
            {
            }
        }
    }

    private static bool HasExited(Process process)
    {
        try
        {
            return process.HasExited;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private static int? GetExitCode(Process process)
    {
        try
        {
            return process.HasExited ? process.ExitCode : null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    private static void SafeCloseStandardInput(Process process)
    {
        try
        {
            process.StandardInput.Close();
        }
        catch (Exception exception) when (
            exception is IOException or InvalidOperationException or ObjectDisposedException)
        {
        }
    }

    private sealed class WorkerProtocolReadState
    {
        public List<RunSample> Samples { get; } = [];
        public List<CompactRunOutput> Outputs { get; } = [];
        public ExecutionOutcome? Terminal { get; set; }
        public Exception? Error { get; set; }
        public TaskCompletionSource<bool> FaultSignal { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private sealed class BoundedStandardErrorCapture
    {
        private readonly int _maximumCharacters;
        private readonly StringBuilder _captured = new();

        public BoundedStandardErrorCapture(int maximumCharacters)
        {
            _maximumCharacters = maximumCharacters;
        }

        public string Value => _captured.ToString();
        public bool WasTruncated { get; private set; }

        public void Append(ReadOnlySpan<char> value)
        {
            foreach (var character in value)
            {
                if (_captured.Length >= _maximumCharacters)
                {
                    WasTruncated = true;
                    continue;
                }

                _captured.Append(Sanitize(character));
            }
        }

        private static char Sanitize(char character)
        {
            return !char.IsControl(character) || character is '\r' or '\n' or '\t'
                ? character
                : '\uFFFD';
        }
    }
}
