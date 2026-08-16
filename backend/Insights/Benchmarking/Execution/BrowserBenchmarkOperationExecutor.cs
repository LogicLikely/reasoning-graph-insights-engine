using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Nodes;
using Backend.Insights.Contracts;
using Backend.Insights.Measurement;
using Backend.Insights.Workers;

namespace Backend.Insights.Benchmarking;

public static class BrowserJourneyActions
{
    public const string Collapsed = "collapsed";
    public const string FullExpansion = "full-expansion";
    public const string Search = "search";
    public const string ResultRender = "result-render";

    public static bool IsKnown(string action) => action is
        Collapsed or FullExpansion or Search or ResultRender;
}

public sealed record BrowserJourneyDefinition(
    string Action,
    string? SearchQuery = null,
    bool MayMaterializeMostGraph = false)
{
    public BrowserJourneyDefinition Validate()
    {
        if (!BrowserJourneyActions.IsKnown(Action))
        {
            throw new ArgumentException($"Unknown browser journey action '{Action}'.", nameof(Action));
        }

        if (Action == BrowserJourneyActions.Search && string.IsNullOrWhiteSpace(SearchQuery))
        {
            throw new ArgumentException("Browser search journeys require a non-empty query.", nameof(SearchQuery));
        }

        if (Action != BrowserJourneyActions.Search && SearchQuery is not null)
        {
            throw new ArgumentException("Only browser search journeys accept a search query.", nameof(SearchQuery));
        }

        if (MayMaterializeMostGraph && Action != BrowserJourneyActions.Search)
        {
            throw new ArgumentException(
                "Only browser search journeys can declare materialization risk.",
                nameof(MayMaterializeMostGraph));
        }

        return this;
    }
}

public sealed record BrowserJourneyOptions
{
    public BrowserJourneyOptions(
        Uri harnessUrl,
        Uri? apiBaseAddress,
        string postgreSqlVersion,
        string graphMapVersion = "0.2.0")
    {
        HarnessUrl = RequireHttpUri(harnessUrl, nameof(harnessUrl));
        ApiBaseAddress = apiBaseAddress is null
            ? null
            : RequireHttpUri(apiBaseAddress, nameof(apiBaseAddress));
        PostgreSqlVersion = RequireText(postgreSqlVersion, nameof(postgreSqlVersion));
        GraphMapVersion = RequireText(graphMapVersion, nameof(graphMapVersion));
    }

    public Uri HarnessUrl { get; }

    public Uri? ApiBaseAddress { get; }

    public string PostgreSqlVersion { get; }

    public string GraphMapVersion { get; }

    private static Uri RequireHttpUri(Uri value, string parameterName)
    {
        ArgumentNullException.ThrowIfNull(value, parameterName);
        if (!value.IsAbsoluteUri || value.Scheme is not ("http" or "https"))
        {
            throw new ArgumentException("Browser journey URLs must be absolute HTTP or HTTPS URIs.", parameterName);
        }

        return value;
    }

    private static string RequireText(string value, string parameterName) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException("Browser journey identity values must not be blank.", parameterName)
            : value;
}

public sealed record BrowserJourneyEnvironment(
    string NodeVersion,
    string BrowserVersion,
    string PlaywrightVersion,
    string GraphMapVersion);

public sealed record BrowserPhaseObservation(
    string Layer,
    string Phase,
    decimal DurationMilliseconds,
    TimingBoundaryProvenance TimingBoundaryProvenance,
    string Source,
    JsonElement Evidence);

public sealed record BrowserJourneyFailure(
    string Code,
    string Message,
    string? ExceptionType = null);

public sealed record BrowserJourneyTerminalEvidence(
    string Version,
    string ScenarioId,
    Guid RunId,
    Guid SampleId,
    string Status,
    long? ActualNodeCount,
    long? ActualEdgeCount,
    long? RenderedNodeCount,
    long? RenderedEdgeCount,
    long? MatchCount,
    long? RequiredAncestorUnionCount,
    IReadOnlyList<string>? RequiredAncestorNodeIds,
    IReadOnlyList<string>? MatchNodeIds,
    long? TotalResultCardinality,
    long? BoundedResultItemCount,
    long? RequestBytes,
    long? ResponseBytes,
    string? ResponsePayloadSha256,
    string? IdentityLimitation,
    JsonElement? DriverPayload,
    BrowserJourneyFailure? Failure,
    IReadOnlyList<string>? UnexpectedConsoleErrors,
    IReadOnlyList<string>? PageErrors,
    IReadOnlyList<string>? ExactSuppressions,
    JsonElement? Evidence,
    BrowserJourneyEnvironment? Environment);

public sealed record BrowserJourneyDriverRequest(
    string Mode,
    Guid RunId,
    Guid SampleId,
    string ScenarioId,
    string OperationKey,
    string HarnessUrl,
    string ApiBaseUrl,
    string GraphSlug,
    string Action,
    string? SearchQuery,
    int TimeoutMilliseconds,
    JsonElement? ResultPayload);

public sealed record BrowserJourneyDriverResult(
    ExecutionOutcome Execution,
    IReadOnlyList<BrowserPhaseObservation> Phases,
    BrowserJourneyTerminalEvidence? Terminal,
    BrowserJourneyEnvironment? Environment,
    int? ProcessId,
    int? ExitCode,
    bool ForcedTermination,
    string StandardError);

public interface IBrowserJourneyDriver
{
    Task<BrowserJourneyDriverResult> ProbeAsync(
        TimeSpan timeout,
        CancellationToken cancellationToken);

    Task<BrowserJourneyDriverResult> RunAsync(
        BrowserJourneyDriverRequest request,
        TimeSpan timeout,
        CancellationToken cancellationToken);
}

public interface IBrowserJourneyCommandProvider
{
    WorkerProcessCommand GetCommand();
}

public sealed class PublishedPlaywrightBrowserJourneyCommandProvider : IBrowserJourneyCommandProvider
{
    public const string DriverPathEnvironmentVariable = "LOGICLIKELY_INSIGHTS_BROWSER_DRIVER_PATH";
    public const string FrontendDirectoryEnvironmentVariable = "LOGICLIKELY_INSIGHTS_FRONTEND_DIRECTORY";
    public const string NodePathEnvironmentVariable = "LOGICLIKELY_NODE_PATH";

    private readonly string? _driverPath;
    private readonly string? _frontendDirectory;

    public PublishedPlaywrightBrowserJourneyCommandProvider(
        string? driverPath = null,
        string? frontendDirectory = null)
    {
        _driverPath = driverPath;
        _frontendDirectory = frontendDirectory;
    }

    public WorkerProcessCommand GetCommand()
    {
        var driverPath = FirstNonBlank(
            _driverPath,
            Environment.GetEnvironmentVariable(DriverPathEnvironmentVariable),
            ExistingPath(Path.Combine(
                Directory.GetCurrentDirectory(), "frontend", "performance", "run-browser-journey.mjs")),
            ExistingPath(Path.Combine(AppContext.BaseDirectory, "browser", "run-browser-journey.mjs")))
            ?? throw new FileNotFoundException(
                $"The Playwright browser driver was not found. Set {DriverPathEnvironmentVariable}.");
        driverPath = Path.GetFullPath(driverPath);

        var frontendDirectory = FirstNonBlank(
            _frontendDirectory,
            Environment.GetEnvironmentVariable(FrontendDirectoryEnvironmentVariable),
            ExistingDirectory(Path.Combine(Directory.GetCurrentDirectory(), "frontend")),
            Directory.GetParent(Path.GetDirectoryName(driverPath)!)?.FullName)
            ?? throw new DirectoryNotFoundException(
                $"The frontend directory was not found. Set {FrontendDirectoryEnvironmentVariable}.");
        frontendDirectory = Path.GetFullPath(frontendDirectory);

        if (!File.Exists(Path.Combine(frontendDirectory, "package.json")))
        {
            throw new FileNotFoundException(
                "The configured frontend directory does not contain package.json.",
                Path.Combine(frontendDirectory, "package.json"));
        }

        var node = FirstNonBlank(
            Environment.GetEnvironmentVariable(NodePathEnvironmentVariable),
            "node")!;
        return new WorkerProcessCommand(
            node,
            [driverPath, "--frontend-dir", frontendDirectory],
            frontendDirectory);
    }

    private static string? ExistingPath(string path) => File.Exists(path) ? path : null;

    private static string? ExistingDirectory(string path) => Directory.Exists(path) ? path : null;

    private static string? FirstNonBlank(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
}

/// <summary>
/// Supervises the Node/Playwright process and retains every correlated phase
/// frame received before timeout, cancellation, protocol failure, or crash.
/// The browser process is an observation boundary; it never runs inside the
/// API process or mutates GraphMap.
/// </summary>
public sealed class PlaywrightBrowserJourneyDriver : IBrowserJourneyDriver
{
    private const int MaximumProtocolLineBytes = 1_048_576;
    private const int MaximumPhaseFrames = 512;
    private const int MaximumStandardErrorCharacters = 8_192;
    private static readonly Encoding Utf8WithoutBom = new UTF8Encoding(false, true);
    private static readonly JsonSerializerOptions ProtocolJsonOptions = CreateProtocolJsonOptions();
    private readonly IBrowserJourneyCommandProvider _commandProvider;

    public PlaywrightBrowserJourneyDriver(IBrowserJourneyCommandProvider? commandProvider = null)
    {
        _commandProvider = commandProvider ?? new PublishedPlaywrightBrowserJourneyCommandProvider();
    }

    public Task<BrowserJourneyDriverResult> ProbeAsync(
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var runId = Guid.NewGuid();
        var sampleId = Guid.NewGuid();
        return RunProcessAsync(
            new BrowserJourneyDriverRequest(
                "probe",
                runId,
                sampleId,
                "browser-environment-probe",
                OperationKeys.GraphFetch,
                "http://127.0.0.1/",
                "http://127.0.0.1/",
                "browser-environment-probe",
                BrowserJourneyActions.Collapsed,
                null,
                CheckedTimeoutMilliseconds(timeout),
                null),
            timeout,
            cancellationToken);
    }

    public Task<BrowserJourneyDriverResult> RunAsync(
        BrowserJourneyDriverRequest request,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return RunProcessAsync(request with { TimeoutMilliseconds = CheckedTimeoutMilliseconds(timeout) }, timeout, cancellationToken);
    }

    private async Task<BrowserJourneyDriverResult> RunProcessAsync(
        BrowserJourneyDriverRequest request,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        if (timeout <= TimeSpan.Zero || timeout > TimeSpan.FromMilliseconds(int.MaxValue))
        {
            throw new ArgumentOutOfRangeException(nameof(timeout));
        }

        if (cancellationToken.IsCancellationRequested)
        {
            return ResultWithoutProcess(BenchmarkOperationExecutor.Failure(
                ExecutionStatus.Cancelled,
                FailureKind.Cancellation,
                "browser-journey-cancelled",
                "The browser journey was cancelled before its process started."));
        }

        WorkerProcessCommand command;
        try
        {
            command = _commandProvider.GetCommand();
        }
        catch (Exception exception)
        {
            return ResultWithoutProcess(BenchmarkOperationExecutor.Failure(
                ExecutionStatus.Failed,
                FailureKind.Execution,
                "browser-driver-resolution-failed",
                "The Playwright browser driver command could not be resolved.",
                exception.GetType().FullName));
        }

        using var process = new Process
        {
            StartInfo = CreateStartInfo(command),
            EnableRaisingEvents = true
        };
        try
        {
            if (!process.Start())
            {
                return ResultWithoutProcess(BenchmarkOperationExecutor.Failure(
                    ExecutionStatus.Failed,
                    FailureKind.Execution,
                    "browser-driver-start-failed",
                    "The Playwright browser driver process did not start."));
            }
        }
        catch (Exception exception)
        {
            return ResultWithoutProcess(BenchmarkOperationExecutor.Failure(
                ExecutionStatus.Failed,
                FailureKind.Execution,
                "browser-driver-start-failed",
                "The Playwright browser driver process could not be started.",
                exception.GetType().FullName));
        }

        var processId = process.Id;
        var frames = new BrowserProtocolState();
        var standardErrorCapture = new BoundedStandardErrorCapture();
        var standardErrorTask = DrainBoundedStandardErrorAsync(
            process.StandardError,
            standardErrorCapture);
        var readTask = ReadProtocolAsync(process.StandardOutput, request, frames);
        var exitTask = process.WaitForExitAsync();
        var forcedTermination = false;
        try
        {
            await process.StandardInput.WriteLineAsync(JsonSerializer.Serialize(request, ProtocolJsonOptions));
            await process.StandardInput.FlushAsync();
            process.StandardInput.Close();

            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(timeout);
            try
            {
                await exitTask.WaitAsync(deadline.Token);
            }
            catch (OperationCanceledException)
            {
                forcedTermination = TryKill(process);
                await AwaitExitWithoutThrowAsync(exitTask);
                await AwaitReaderWithoutThrowAsync(readTask);
                var standardError = await AwaitBoundedStandardErrorAsync(
                    standardErrorTask,
                    standardErrorCapture);
                var callerCancelled = cancellationToken.IsCancellationRequested;
                return new BrowserJourneyDriverResult(
                    BenchmarkOperationExecutor.Failure(
                        callerCancelled ? ExecutionStatus.Cancelled : ExecutionStatus.TimedOut,
                        callerCancelled ? FailureKind.Cancellation : FailureKind.Timeout,
                        callerCancelled ? "browser-journey-cancelled" : "browser-journey-timeout",
                        callerCancelled
                            ? "The browser journey was cancelled by the caller."
                            : "The browser journey exceeded its hard timeout."),
                    frames.Phases.AsReadOnly(),
                    frames.Terminal,
                    frames.Terminal?.Environment,
                    processId,
                    SafeExitCode(process),
                    forcedTermination,
                    standardError);
            }

            await readTask;
            var stderr = await AwaitBoundedStandardErrorAsync(
                standardErrorTask,
                standardErrorCapture);
            if (frames.ProtocolException is not null)
            {
                return new BrowserJourneyDriverResult(
                    BenchmarkOperationExecutor.Failure(
                        ExecutionStatus.Failed,
                        FailureKind.Execution,
                        "browser-driver-protocol-invalid",
                        "The Playwright browser driver emitted an invalid protocol frame.",
                        frames.ProtocolException.GetType().FullName),
                    frames.Phases.AsReadOnly(),
                    frames.Terminal,
                    frames.Terminal?.Environment,
                    processId,
                    SafeExitCode(process),
                    forcedTermination,
                    stderr);
            }

            var exitCode = SafeExitCode(process);
            if (frames.Terminal is null)
            {
                return new BrowserJourneyDriverResult(
                    BenchmarkOperationExecutor.Failure(
                        exitCode == 0 ? ExecutionStatus.Failed : ExecutionStatus.Crashed,
                        exitCode == 0 ? FailureKind.Execution : FailureKind.Crash,
                        exitCode == 0 ? "browser-terminal-missing" : "browser-driver-crashed",
                        exitCode == 0
                            ? "The browser driver exited without terminal evidence."
                            : "The browser driver exited unexpectedly before terminal evidence."),
                    frames.Phases.AsReadOnly(),
                    null,
                    null,
                    processId,
                    exitCode,
                    forcedTermination,
                    stderr);
            }

            var execution = ClassifyTerminal(frames.Terminal, exitCode);
            return new BrowserJourneyDriverResult(
                execution,
                frames.Phases.AsReadOnly(),
                frames.Terminal,
                frames.Terminal.Environment,
                processId,
                exitCode,
                forcedTermination,
                stderr);
        }
        catch (Exception exception)
        {
            forcedTermination |= TryKill(process);
            await AwaitExitWithoutThrowAsync(exitTask);
            await AwaitReaderWithoutThrowAsync(readTask);
            var stderr = await AwaitBoundedStandardErrorAsync(
                standardErrorTask,
                standardErrorCapture);
            return new BrowserJourneyDriverResult(
                BenchmarkOperationExecutor.Failure(
                    ExecutionStatus.Failed,
                    FailureKind.Execution,
                    "browser-driver-supervision-failed",
                    "The Playwright browser driver could not be supervised.",
                    exception.GetType().FullName),
                frames.Phases.AsReadOnly(),
                frames.Terminal,
                frames.Terminal?.Environment,
                processId,
                SafeExitCode(process),
                forcedTermination,
                stderr);
        }
        finally
        {
            if (!process.HasExited)
            {
                TryKill(process);
            }
        }
    }

    private static async Task ReadProtocolAsync(
        StreamReader reader,
        BrowserJourneyDriverRequest request,
        BrowserProtocolState state)
    {
        try
        {
            long expectedSequence = 0;
            while (await reader.ReadLineAsync() is { } line)
            {
                if (Utf8WithoutBom.GetByteCount(line) > MaximumProtocolLineBytes)
                {
                    throw new InvalidDataException("Browser protocol line exceeded the configured maximum size.");
                }

                using var document = JsonDocument.Parse(line);
                var root = document.RootElement;
                var runId = root.GetProperty("runId").GetGuid();
                var sampleId = root.GetProperty("sampleId").GetGuid();
                var sequence = root.GetProperty("sequence").GetInt64();
                var eventKind = root.GetProperty("eventKind").GetString();
                if (runId != request.RunId || sampleId != request.SampleId)
                {
                    throw new InvalidDataException("Browser protocol correlation did not match the request.");
                }

                if (sequence != expectedSequence++)
                {
                    throw new InvalidDataException("Browser protocol sequence was not contiguous from zero.");
                }

                if (state.Terminal is not null)
                {
                    throw new InvalidDataException("Browser protocol emitted evidence after its terminal frame.");
                }

                switch (eventKind)
                {
                    case "phase":
                        if (state.Phases.Count >= MaximumPhaseFrames)
                        {
                            throw new InvalidDataException(
                                "Browser protocol exceeded the maximum retained phase-frame count.");
                        }

                        var phase = root.GetProperty("phase").Deserialize<BrowserPhaseObservation>(ProtocolJsonOptions)
                            ?? throw new InvalidDataException("Browser phase frame did not contain phase evidence.");
                        state.Phases.Add(phase);
                        break;
                    case "terminal":
                        state.Terminal = root.GetProperty("terminal")
                            .Deserialize<BrowserJourneyTerminalEvidence>(ProtocolJsonOptions)
                            ?? throw new InvalidDataException("Browser terminal frame did not contain terminal evidence.");
                        break;
                    default:
                        throw new InvalidDataException($"Unknown browser protocol event kind '{eventKind}'.");
                }
            }
        }
        catch (Exception exception)
        {
            state.ProtocolException = exception;
        }
    }

    private static ExecutionOutcome ClassifyTerminal(
        BrowserJourneyTerminalEvidence terminal,
        int? exitCode)
    {
        if (exitCode is not 0)
        {
            return BenchmarkOperationExecutor.Failure(
                ExecutionStatus.Crashed,
                FailureKind.Crash,
                "browser-driver-crashed",
                "The browser driver returned terminal evidence but exited abnormally.");
        }

        var consoleErrors = terminal.UnexpectedConsoleErrors ?? [];
        var pageErrors = terminal.PageErrors ?? [];
        if (consoleErrors.Count > 0 || pageErrors.Count > 0)
        {
            return BenchmarkOperationExecutor.Failure(
                ExecutionStatus.Failed,
                FailureKind.Execution,
                "browser-page-error",
                "The browser journey observed an unexpected page or console error.");
        }

        return terminal.Status switch
        {
            "succeeded" => new ExecutionOutcome(ExecutionStatus.Succeeded),
            "failed" => TerminalFailure(terminal, ExecutionStatus.Failed, FailureKind.Execution),
            "timed-out" => TerminalFailure(terminal, ExecutionStatus.TimedOut, FailureKind.Timeout),
            "cancelled" => TerminalFailure(terminal, ExecutionStatus.Cancelled, FailureKind.Cancellation),
            "crashed" => TerminalFailure(terminal, ExecutionStatus.Crashed, FailureKind.Crash),
            "skipped" => TerminalFailure(terminal, ExecutionStatus.Skipped, FailureKind.Skip),
            _ => BenchmarkOperationExecutor.Failure(
                ExecutionStatus.Failed,
                FailureKind.Execution,
                "browser-terminal-status-invalid",
                $"The browser terminal status '{terminal.Status}' is not recognized.")
        };
    }

    private static ExecutionOutcome TerminalFailure(
        BrowserJourneyTerminalEvidence terminal,
        ExecutionStatus status,
        FailureKind kind) => BenchmarkOperationExecutor.Failure(
        status,
        kind,
        terminal.Failure?.Code ?? "browser-journey-failed",
        terminal.Failure?.Message ?? "The browser journey did not succeed.",
        terminal.Failure?.ExceptionType);

    private static ProcessStartInfo CreateStartInfo(WorkerProcessCommand command)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = command.FileName,
            WorkingDirectory = command.WorkingDirectory ?? string.Empty,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardInputEncoding = Utf8WithoutBom,
            StandardOutputEncoding = Utf8WithoutBom,
            StandardErrorEncoding = Utf8WithoutBom
        };
        foreach (var argument in command.Arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        return startInfo;
    }

    private static async Task DrainBoundedStandardErrorAsync(
        StreamReader reader,
        BoundedStandardErrorCapture capture)
    {
        var buffer = new char[1_024];
        try
        {
            int read;
            while ((read = await reader.ReadAsync(buffer)) > 0)
            {
                capture.Append(buffer, read);
            }
        }
        catch (Exception exception) when (exception is IOException or ObjectDisposedException)
        {
            // The process/pipe can be disposed after a bounded drain fallback.
            // The already captured prefix remains the only portable evidence.
        }
    }

    private static async Task<string> AwaitBoundedStandardErrorAsync(
        Task drainTask,
        BoundedStandardErrorCapture capture)
    {
        try
        {
            await drainTask.WaitAsync(TimeSpan.FromSeconds(1));
            return capture.Snapshot(incomplete: false);
        }
        catch (TimeoutException)
        {
            return capture.Snapshot(incomplete: true);
        }
        catch
        {
            return capture.Snapshot(incomplete: true);
        }
    }

    private static bool TryKill(Process process)
    {
        try
        {
            if (process.HasExited) return false;
            process.Kill(entireProcessTree: true);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static async Task AwaitExitWithoutThrowAsync(Task task)
    {
        try { await task.WaitAsync(TimeSpan.FromSeconds(5)); }
        catch { }
    }

    private static async Task AwaitReaderWithoutThrowAsync(Task task)
    {
        try { await task.WaitAsync(TimeSpan.FromSeconds(5)); }
        catch { }
    }

    private static int? SafeExitCode(Process process)
    {
        try { return process.HasExited ? process.ExitCode : null; }
        catch { return null; }
    }

    private static int CheckedTimeoutMilliseconds(TimeSpan timeout) =>
        timeout <= TimeSpan.Zero || timeout > TimeSpan.FromMilliseconds(int.MaxValue)
            ? throw new ArgumentOutOfRangeException(nameof(timeout))
            : checked((int)Math.Ceiling(timeout.TotalMilliseconds));

    private static JsonSerializerOptions CreateProtocolJsonOptions()
    {
        var options = new JsonSerializerOptions(CanonicalJson.CreateSerializerOptions())
        {
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Skip
        };
        return options;
    }

    private static BrowserJourneyDriverResult ResultWithoutProcess(ExecutionOutcome outcome) =>
        new(outcome, [], null, null, null, null, false, string.Empty);

    private sealed class BrowserProtocolState
    {
        public List<BrowserPhaseObservation> Phases { get; } = [];
        public BrowserJourneyTerminalEvidence? Terminal { get; set; }
        public Exception? ProtocolException { get; set; }
    }

    private sealed class BoundedStandardErrorCapture
    {
        private const string IncompleteMarker = "\n[stderr drain incomplete after bounded wait]";
        private readonly StringBuilder _value = new();
        private readonly object _gate = new();

        public void Append(char[] buffer, int count)
        {
            lock (_gate)
            {
                if (_value.Length >= MaximumStandardErrorCharacters) return;
                var take = Math.Min(count, MaximumStandardErrorCharacters - _value.Length);
                _value.Append(buffer, 0, take);
            }
        }

        public string Snapshot(bool incomplete)
        {
            lock (_gate)
            {
                if (!incomplete) return _value.ToString();
                var retainedLength = Math.Max(
                    0,
                    MaximumStandardErrorCharacters - IncompleteMarker.Length);
                return string.Concat(
                    _value.ToString(0, Math.Min(_value.Length, retainedLength)),
                    IncompleteMarker);
            }
        }
    }
}

/// <summary>
/// Converts browser-owned evidence into the shared Phase 1 raw-sample and
/// compact-output contracts. No GraphMap internals are inferred: DOM settling
/// and layout evidence must remain observed/estimated, while consumer marks and
/// React Profiler callbacks may be directly instrumented.
/// </summary>
public sealed class BrowserBenchmarkOperationExecutor :
    IBenchmarkOperationExecutor,
    IBenchmarkScenarioPreparer
{
    public const string HarnessVersion = "phase-4-browser-v1";
    public const string ExpectedHarnessBuildIdentity = "storybook-production-profiling";
    private const int MaximumBrowserResultPayloadBytes = 786_432;
    private const int MaximumBrowserResultItems = OperationResultEnvelope.MaximumRetainedItems;
    private const int MaximumBrowserResultPaths = 20;
    private const int MaximumBrowserStructuredArrayElements = 128;
    private const int MaximumBrowserObjectProperties = 16;
    private const int MaximumBrowserProjectionDepth = 4;
    private const int MaximumBrowserStringUtf16CodeUnits = 512;
    private const int MaximumBrowserStructuredValueBytes = 3_072;
    private const int MaximumBrowserItemBytes = 4_096;
    private const int MaximumBrowserSummaryBytes = 16_384;
    private static readonly JsonSerializerOptions JsonOptions = CanonicalJson.CreateSerializerOptions();
    private readonly IBrowserJourneyDriver _driver;
    private readonly BrowserJourneyOptions _options;
    private readonly AnalysisWorkerDispatcher _dispatcher;

    public BrowserBenchmarkOperationExecutor(
        IBrowserJourneyDriver driver,
        BrowserJourneyOptions options,
        AnalysisWorkerDispatcher? dispatcher = null)
    {
        _driver = driver ?? throw new ArgumentNullException(nameof(driver));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _dispatcher = dispatcher ?? new AnalysisWorkerDispatcher();
    }

    public async Task<BenchmarkScenarioPreparationResult> PrepareAsync(
        PreparedBenchmarkOperation operation,
        BenchmarkScenarioDefinition scenario,
        DeterministicStressGraphFixture fixture,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var usesApi = scenario.BrowserJourney?.Action != BrowserJourneyActions.ResultRender;
        if (usesApi && _options.ApiBaseAddress is null)
        {
            var missingApi = BenchmarkOperationExecutor.Failure(
                ExecutionStatus.Failed,
                FailureKind.Execution,
                "browser-api-base-url-required",
                "Graph browser scenarios require an explicit browser-compatible API endpoint.");
            return new BenchmarkScenarioPreparationResult(
                operation,
                missingApi,
                [CreateSetupSample(operation, scenario, fixture, missingApi, 0)],
                RunnerType: RunnerType.ApiBrowserJourney);
        }

        var started = Stopwatch.GetTimestamp();
        var probe = await _driver.ProbeAsync(timeout, cancellationToken);
        var duration = ElapsedMilliseconds(started);
        var sample = CreateSetupSample(operation, scenario, fixture, probe.Execution, duration);
        if (probe.Execution.Status != ExecutionStatus.Succeeded || probe.Environment is null)
        {
            return new BenchmarkScenarioPreparationResult(
                operation,
                probe.Execution,
                [sample],
                RunnerType: RunnerType.ApiBrowserJourney);
        }

        var environment = probe.Environment;
        var browserOriginTopology = BrowserOriginTopology(usesApi);
        if (!string.Equals(environment.GraphMapVersion, _options.GraphMapVersion, StringComparison.Ordinal))
        {
            var mismatch = ExecutionOutcome.ValidationFailed([
                new ValidationFailure(
                    "dependencies.graphMap",
                    "browser-graphmap-version-mismatch",
                    $"Browser harness reported GraphMap {environment.GraphMapVersion}; expected {_options.GraphMapVersion}.")
            ]);
            return new BenchmarkScenarioPreparationResult(
                operation,
                mismatch,
                [sample with { Execution = mismatch }],
                RunnerType: RunnerType.ApiBrowserJourney);
        }

        var dependencies = new DependencyVersions(
            Environment.Version.ToString(),
            environment.NodeVersion,
            environment.BrowserVersion,
            environment.GraphMapVersion,
            usesApi ? _options.PostgreSqlVersion : "not-used",
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["browser-control"] = "playwright-real-process",
                ["browser-host-class"] = _options.HarnessUrl.IsLoopback ? "loopback" : "remote",
                ["api-host-class"] = usesApi
                    ? (_options.ApiBaseAddress!.IsLoopback ? "loopback" : "remote")
                    : "not-used",
                ["browser-api-scheme"] = usesApi
                    ? _options.ApiBaseAddress!.Scheme
                    : "not-used",
                ["browser-api-origin-topology"] = browserOriginTopology,
                ["browser-fetch-ttfb-semantics"] = browserOriginTopology == "cross-origin"
                    ? "correlation-header-cors-preflight-may-be-included"
                    : "no-cross-origin-cors-preflight",
                ["browser-harness-build"] = ExpectedHarnessBuildIdentity,
                ["playwright"] = environment.PlaywrightVersion,
                ["runtime"] = RuntimeInformation.FrameworkDescription
            });
        return new BenchmarkScenarioPreparationResult(
            operation,
            new ExecutionOutcome(ExecutionStatus.Succeeded),
            [sample],
            Dependencies: dependencies,
            EnvironmentProfile: CreateEnvironmentProfile(
                environment.BrowserVersion,
                usesApi,
                browserOriginTopology),
            RunnerType: RunnerType.ApiBrowserJourney);
    }

    public async Task<BenchmarkOperationExecutionResult> ExecuteAsync(
        PreparedBenchmarkOperation operation,
        BenchmarkScenarioDefinition scenario,
        DeterministicStressGraphFixture fixture,
        BenchmarkProfileDefinition profile,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        if (scenario.ExecutionTarget != BenchmarkScenarioExecutionTarget.Browser ||
            scenario.BrowserJourney is null)
        {
            var invalid = ExecutionOutcome.ValidationFailed([
                new ValidationFailure(
                    "scenario.executionTarget",
                    "browser-scenario-required",
                    "The browser executor only accepts registered browser journey scenarios.")
            ]);
            return new BenchmarkOperationExecutionResult(
                invalid,
                [CreateOperationSample(operation, scenario, fixture, profile, invalid, 0)],
                []);
        }

        var browser = scenario.BrowserJourney.Validate();
        var safetyFailure = ValidateSafety(browser, fixture);
        if (safetyFailure is not null)
        {
            return new BenchmarkOperationExecutionResult(
                safetyFailure,
                [CreateOperationSample(operation, scenario, fixture, profile, safetyFailure, 0)],
                []);
        }

        CompactRunOutput? canonicalAnalysisOutput = null;
        JsonElement? resultPayload = null;
        int? expectedBoundedResultItemCount = null;
        decimal? resultPreparationDuration = null;
        if (browser.Action == BrowserJourneyActions.ResultRender)
        {
            var preparationStarted = Stopwatch.GetTimestamp();
            try
            {
                canonicalAnalysisOutput = _dispatcher.Dispatch(operation.Request, cancellationToken);
                resultPreparationDuration = ElapsedMilliseconds(preparationStarted);
                var projection = CreateBoundedResultPayload(
                    scenario,
                    canonicalAnalysisOutput,
                    cancellationToken);
                resultPayload = projection.Payload;
                expectedBoundedResultItemCount = projection.RetainedItemCount;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                var cancelled = BenchmarkOperationExecutor.Failure(
                    ExecutionStatus.Cancelled,
                    FailureKind.Cancellation,
                    "browser-result-fixture-cancelled",
                    "Preparation of the bounded analysis-result fixture was cancelled.");
                return new BenchmarkOperationExecutionResult(
                    cancelled,
                    [CreateResultPreparationSample(
                        operation, scenario, fixture, profile, cancelled,
                        ElapsedMilliseconds(preparationStarted))],
                    []);
            }
            catch (Exception exception)
            {
                var failed = BenchmarkOperationExecutor.Failure(
                    ExecutionStatus.Failed,
                    FailureKind.Execution,
                    "browser-result-fixture-failed",
                    "Preparation of the bounded analysis-result fixture failed.",
                    exception.GetType().FullName);
                return new BenchmarkOperationExecutionResult(
                    failed,
                    [CreateResultPreparationSample(
                        operation, scenario, fixture, profile, failed,
                        ElapsedMilliseconds(preparationStarted))],
                    []);
            }
        }

        var request = new BrowserJourneyDriverRequest(
            "journey",
            operation.Request.RunId,
            operation.Request.SampleId,
            scenario.Key,
            scenario.OperationKey,
            _options.HarnessUrl.AbsoluteUri,
            _options.ApiBaseAddress?.AbsoluteUri ?? "http://127.0.0.1/",
            scenario.DatasetId,
            browser.Action,
            browser.SearchQuery,
            checked((int)Math.Ceiling(timeout.TotalMilliseconds)),
            resultPayload);
        var driverStarted = Stopwatch.GetTimestamp();
        var driverResult = await _driver.RunAsync(request, timeout, cancellationToken);
        var driverDuration = ElapsedMilliseconds(driverStarted);
        var execution = ValidateDriverResult(
            driverResult,
            operation,
            scenario,
            browser,
            fixture,
            canonicalAnalysisOutput,
            expectedBoundedResultItemCount);
        var terminal = driverResult.Terminal;
        var samples = new List<RunSample>();
        if (resultPreparationDuration is not null)
        {
            samples.Add(CreateResultPreparationSample(
                operation,
                scenario,
                fixture,
                profile,
                new ExecutionOutcome(ExecutionStatus.Succeeded),
                resultPreparationDuration.Value));
        }

        samples.AddRange(driverResult.Phases
            .Where(IsPersistablePhase)
            .Select(phase => CreateBrowserSample(
                operation,
                scenario,
                fixture,
                profile,
                new ExecutionOutcome(ExecutionStatus.Succeeded),
                terminal,
                phase)));
        samples.Add(CreateOperationSample(
            operation,
            scenario,
            fixture,
            profile,
            execution,
            driverDuration));

        if (execution.Status != ExecutionStatus.Succeeded || terminal is null)
        {
            var evidenceOutput = CreateFailureEvidenceOutput(
                operation,
                scenario,
                fixture,
                browser,
                execution,
                terminal,
                driverResult,
                _options.ApiBaseAddress?.Scheme,
                BrowserOriginTopology(browser.Action != BrowserJourneyActions.ResultRender));
            return new BenchmarkOperationExecutionResult(
                execution,
                samples.AsReadOnly(),
                [evidenceOutput]);
        }

        CompactRunOutput output;
        try
        {
            output = canonicalAnalysisOutput is null
                ? CreateBrowserOutput(
                    operation,
                    scenario,
                    fixture,
                    browser,
                    terminal,
                    driverResult.Phases,
                    _options.ApiBaseAddress?.Scheme,
                    BrowserOriginTopology(usesApi: true))
                : EnrichAnalysisOutput(canonicalAnalysisOutput, terminal, driverResult.Phases);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or JsonException)
        {
            var invalid = BenchmarkOperationExecutor.Failure(
                ExecutionStatus.Failed,
                FailureKind.Validation,
                "browser-output-invalid",
                "Browser terminal evidence could not be projected into the canonical compact output.",
                exception.GetType().FullName);
            return new BenchmarkOperationExecutionResult(
                invalid,
                samples,
                [CreateFailureEvidenceOutput(
                    operation,
                    scenario,
                    fixture,
                    browser,
                    invalid,
                    terminal,
                    driverResult,
                    _options.ApiBaseAddress?.Scheme,
                    BrowserOriginTopology(browser.Action != BrowserJourneyActions.ResultRender))]);
        }

        return new BenchmarkOperationExecutionResult(execution, samples.AsReadOnly(), [output]);
    }

    private static ExecutionOutcome ValidateDriverResult(
        BrowserJourneyDriverResult result,
        PreparedBenchmarkOperation operation,
        BenchmarkScenarioDefinition scenario,
        BrowserJourneyDefinition browser,
        DeterministicStressGraphFixture fixture,
        CompactRunOutput? canonicalAnalysisOutput,
        int? expectedBoundedResultItemCount)
    {
        if (result.Execution.Status != ExecutionStatus.Succeeded)
        {
            return result.Execution;
        }

        var terminal = result.Terminal;
        if (terminal is null)
        {
            return BrowserValidationFailure(
                "browser-terminal-missing",
                "The successful driver result did not retain terminal browser evidence.");
        }

        if (!string.Equals(terminal.Version, HarnessVersion, StringComparison.Ordinal) ||
            !string.Equals(terminal.ScenarioId, scenario.Key, StringComparison.Ordinal) ||
            terminal.RunId != operation.Request.RunId ||
            terminal.SampleId != operation.Request.SampleId)
        {
            return BrowserValidationFailure(
                "browser-correlation-mismatch",
                "The browser terminal version or correlation identity did not match the invocation.");
        }

        if (terminal.DriverPayload is { ValueKind: not (JsonValueKind.Null or JsonValueKind.Undefined) })
        {
            return BrowserValidationFailure(
                "browser-terminal-payload-unbounded",
                "Browser terminal evidence must remain compact and must not duplicate the fetched graph payload.");
        }

        if (terminal.Failure is not null ||
            terminal.Evidence is not { ValueKind: JsonValueKind.Object } terminalEvidence ||
            !terminalEvidence.EnumerateObject().Any())
        {
            return BrowserValidationFailure(
                "browser-terminal-evidence-invalid",
                "A succeeded browser terminal must not carry failure details and must retain stable-view evidence.");
        }

        if (!string.Equals(
                ReadEvidenceText(terminalEvidence, "harnessBuildIdentity"),
                ExpectedHarnessBuildIdentity,
                StringComparison.Ordinal))
        {
            return BrowserValidationFailure(
                "browser-harness-build-identity-invalid",
                $"Controlled browser measurements require harness build identity '{ExpectedHarnessBuildIdentity}'.");
        }

        if (browser.Action != BrowserJourneyActions.ResultRender &&
            (terminal.ActualNodeCount != fixture.NodeCount || terminal.ActualEdgeCount != fixture.EdgeCount))
        {
            return BrowserValidationFailure(
                "browser-graph-count-mismatch",
                $"Browser observed {terminal.ActualNodeCount?.ToString(CultureInfo.InvariantCulture) ?? "unknown"} nodes/" +
                $"{terminal.ActualEdgeCount?.ToString(CultureInfo.InvariantCulture) ?? "unknown"} edges; " +
                $"expected {fixture.NodeCount}/{fixture.EdgeCount}.");
        }

        if (browser.Action != BrowserJourneyActions.ResultRender)
        {
            var terminalNextHopProtocol = ReadEvidenceText(terminalEvidence, "nextHopProtocol");
            var terminalProtocolLimitation = ReadEvidenceText(
                terminalEvidence,
                "resourceTimingLimitation");
            var fullTransfer = result.Phases.LastOrDefault(phase =>
                phase.Layer == InsightMeasurementLayers.Transport &&
                phase.Phase == InsightMeasurementPhases.FullTransfer);
            var transferNextHopProtocol = ReadEvidenceText(
                fullTransfer?.Evidence,
                "nextHopProtocol");
            var transferProtocolLimitation = ReadEvidenceText(
                fullTransfer?.Evidence,
                "resourceTimingLimitation");
            if ((terminalNextHopProtocol is null && terminalProtocolLimitation is null) ||
                (transferNextHopProtocol is null && transferProtocolLimitation is null))
            {
                return BrowserValidationFailure(
                    "browser-network-protocol-evidence-missing",
                    "Graph browser journeys must retain Resource Timing next-hop protocol evidence or an explicit browser observability limitation in both the full-transfer boundary and terminal evidence.");
            }

            if (terminalNextHopProtocol is not null &&
                transferNextHopProtocol is not null &&
                !string.Equals(
                    terminalNextHopProtocol,
                    transferNextHopProtocol,
                    StringComparison.Ordinal))
            {
                return BrowserValidationFailure(
                    "browser-network-protocol-evidence-mismatch",
                    "Terminal and full-transfer Resource Timing next-hop protocol observations did not match.");
            }
        }

        if (browser.Action == BrowserJourneyActions.FullExpansion &&
            (terminal.RenderedNodeCount != fixture.NodeCount ||
             terminal.RenderedEdgeCount != fixture.EdgeCount))
        {
            return BrowserValidationFailure(
                "browser-full-expansion-count-mismatch",
                "The designated small full-expansion journey did not reach every expected node and edge.");
        }

        if (browser.Action == BrowserJourneyActions.Collapsed &&
            (terminal.RenderedNodeCount is null or <= 0 ||
             terminal.RenderedNodeCount > fixture.NodeCount ||
             terminal.RenderedEdgeCount is null or < 0 ||
             terminal.RenderedEdgeCount > fixture.EdgeCount))
        {
            return BrowserValidationFailure(
                "browser-collapsed-count-invalid",
                "Collapsed GraphMap rendered counts must be positive, bounded by the source graph, and explicitly observed.");
        }

        if ((terminal.ExactSuppressions?.Count ?? 0) > 0)
        {
            return BrowserValidationFailure(
                "browser-error-suppression-unapproved",
                "No browser error suppression is approved without a reproduced harmless notification.");
        }

        if (browser.Action != BrowserJourneyActions.ResultRender &&
            (terminal.ResponseBytes is not > 0 || !IsSha256(terminal.ResponsePayloadSha256)))
        {
            return BrowserValidationFailure(
                "browser-response-evidence-incomplete",
                "Browser graph journeys must retain response byte count and exact response-payload SHA-256 evidence.");
        }

        foreach (var phase in result.Phases)
        {
            if (!InsightPhaseRegistry.IsKnown(phase.Layer, phase.Phase) ||
                phase.DurationMilliseconds < 0 ||
                string.IsNullOrWhiteSpace(phase.Source) ||
                phase.Evidence.ValueKind != JsonValueKind.Object ||
                !phase.Evidence.EnumerateObject().Any() ||
                !HasMonotonicBoundaryEvidence(phase))
            {
                return BrowserValidationFailure(
                    "browser-phase-invalid",
                    "A browser phase had an unknown name, invalid duration/source, or no raw boundary evidence.");
            }

            if (!HasDefensibleProvenance(phase))
            {
                return BrowserValidationFailure(
                    "browser-phase-provenance-invalid",
                    $"Phase '{phase.Layer}/{phase.Phase}' used provenance its actual observation seam cannot support.");
            }
        }

        var requiredPhases = browser.Action == BrowserJourneyActions.ResultRender
            ? new[]
            {
                (InsightMeasurementLayers.LabResult, InsightMeasurementPhases.ResultRender),
                (InsightMeasurementLayers.LabResult, InsightMeasurementPhases.ReactCommit),
                (InsightMeasurementLayers.EndToEnd, InsightMeasurementPhases.ActionToStableResultAndView)
            }
            : new[]
            {
                (InsightMeasurementLayers.Transport, InsightMeasurementPhases.TimeToFirstByte),
                (InsightMeasurementLayers.Transport, InsightMeasurementPhases.FullTransfer),
                (InsightMeasurementLayers.BrowserData, InsightMeasurementPhases.JsonParse),
                (InsightMeasurementLayers.BrowserData, InsightMeasurementPhases.DomainMapping),
                (InsightMeasurementLayers.BrowserData, InsightMeasurementPhases.GraphMapAdapter),
                (InsightMeasurementLayers.GraphMap, InsightMeasurementPhases.NodeEdgeMaterialization),
                (InsightMeasurementLayers.GraphMap, InsightMeasurementPhases.ReactCommit),
                (InsightMeasurementLayers.GraphMap, InsightMeasurementPhases.ViewportFit),
                (InsightMeasurementLayers.EndToEnd, InsightMeasurementPhases.ActionToStableResultAndView)
            };
        var missing = requiredPhases.Where(required => !result.Phases.Any(phase =>
            phase.Layer == required.Item1 && phase.Phase == required.Item2)).ToArray();
        if (missing.Length > 0)
        {
            return BrowserValidationFailure(
                "browser-terminal-boundary-missing",
                $"Browser journey omitted required terminal boundaries: {string.Join(", ", missing.Select(item => $"{item.Item1}/{item.Item2}"))}.");
        }

        var requiresDagreLayout = browser.Action is
            BrowserJourneyActions.Collapsed or BrowserJourneyActions.FullExpansion ||
            (browser.Action == BrowserJourneyActions.Search && terminal.MatchCount > 0);
        if (requiresDagreLayout && !result.Phases.Any(phase =>
                phase.Layer == InsightMeasurementLayers.GraphMap &&
                phase.Phase == InsightMeasurementPhases.DagreLayout))
        {
            return BrowserValidationFailure(
                "browser-dagre-layout-boundary-missing",
                "A graph view with rendered results must retain the estimated consumer-observed Dagre layout boundary.");
        }

        if (browser.Action == BrowserJourneyActions.Search)
        {
            if (terminal.MatchCount is null || terminal.RequiredAncestorUnionCount is null)
            {
                return BrowserValidationFailure(
                    "browser-search-evidence-incomplete",
                    "Browser search must retain match count and required-ancestor-union count.");
            }

            if (terminal.MatchCount < 0 || terminal.RequiredAncestorUnionCount < 0 ||
                terminal.RequiredAncestorUnionCount > fixture.NodeCount ||
                terminal.TotalResultCardinality != terminal.MatchCount)
            {
                return BrowserValidationFailure(
                    "browser-search-count-invalid",
                    "Browser search counts must be non-negative, bounded, and preserve match cardinality.");
            }

            if (terminal.RequiredAncestorNodeIds is { } requiredNodeIds &&
                (requiredNodeIds.Count != terminal.RequiredAncestorUnionCount ||
                 requiredNodeIds.Any(string.IsNullOrWhiteSpace) ||
                 requiredNodeIds.Distinct(StringComparer.Ordinal).Count() != requiredNodeIds.Count))
            {
                return BrowserValidationFailure(
                    "browser-search-evidence-invalid",
                    "When provided, complete required-node union IDs must be non-blank, distinct, and reconcile exactly with the visible required-node union count.");
            }

            if (result.Phases.Any(phase =>
                    phase.Layer == InsightMeasurementLayers.BrowserData &&
                    phase.Phase == InsightMeasurementPhases.SearchIndexConstruction))
            {
                return BrowserValidationFailure(
                    "browser-search-index-boundary-unsupported",
                    "GraphMap 0.2.0 exposes no internal search-index boundary; it cannot be reported by the browser harness.");
            }

            var searchCompletion = result.Phases.LastOrDefault(phase =>
                phase.Layer == InsightMeasurementLayers.BrowserData &&
                phase.Phase == InsightMeasurementPhases.SearchCompletion);
            if (searchCompletion is null ||
                !searchCompletion.Evidence.TryGetProperty("searchStatus", out var status) ||
                status.ValueKind != JsonValueKind.String ||
                string.IsNullOrWhiteSpace(status.GetString()))
            {
                return BrowserValidationFailure(
                    "browser-search-completion-evidence-missing",
                    "GraphMap search completion must be externally observed through its visible status text.");
            }
        }

        if (browser.Action == BrowserJourneyActions.ResultRender && canonicalAnalysisOutput is not null)
        {
            var expectedMaximum = Math.Min(
                canonicalAnalysisOutput.TotalResultCardinality,
                OperationResultEnvelope.MaximumRetainedItems);
            if (terminal.TotalResultCardinality != canonicalAnalysisOutput.TotalResultCardinality ||
                terminal.BoundedResultItemCount is null ||
                terminal.BoundedResultItemCount > expectedMaximum ||
                terminal.BoundedResultItemCount != expectedBoundedResultItemCount)
            {
                return BrowserValidationFailure(
                    "browser-result-render-cardinality-mismatch",
                    "The bounded result surface did not preserve complete result cardinality or exceeded its bounded rows.");
            }
        }

        return result.Execution;
    }

    private static ExecutionOutcome? ValidateSafety(
        BrowserJourneyDefinition browser,
        DeterministicStressGraphFixture fixture)
    {
        if (browser.Action == BrowserJourneyActions.FullExpansion && fixture.NodeCount > 1_000)
        {
            return BenchmarkOperationExecutor.Failure(
                ExecutionStatus.Skipped,
                FailureKind.Skip,
                "browser-full-expansion-small-only",
                "Complete GraphMap expansion is scheduled only for the designated 1K dataset.");
        }

        if (browser.Action == BrowserJourneyActions.Search &&
            browser.MayMaterializeMostGraph && fixture.NodeCount >= 10_000)
        {
            return BenchmarkOperationExecutor.Failure(
                ExecutionStatus.Skipped,
                FailureKind.Skip,
                "browser-deep-search-materialization-unsafe",
                "Large deep-chain search is not run through GraphMap because it may materialize most of the graph.");
        }

        return null;
    }

    private static BrowserResultPayloadProjection CreateBoundedResultPayload(
        BenchmarkScenarioDefinition scenario,
        CompactRunOutput canonical,
        CancellationToken cancellationToken)
    {
        var projectedSummary = ProjectBrowserJsonRoot(
            canonical.Summary,
            "$.summary",
            MaximumBrowserSummaryBytes);
        var projectedDistribution = ProjectBrowserJsonRoot(
            canonical.Distribution,
            "$.distribution",
            MaximumBrowserSummaryBytes);
        var projectedItems = canonical.Items
            .Take(MaximumBrowserResultItems)
            .Select((item, index) =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                return ProjectBrowserJsonRoot(
                    item,
                    $"$.items[{index}]",
                    MaximumBrowserItemBytes);
            })
            .ToList();
        var projectedPaths = canonical.OrderedPaths
            .Take(MaximumBrowserResultPaths)
            .Select((path, index) =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                return ProjectBrowserPath(path, index);
            })
            .ToList();

        var retainedItemCount = projectedItems.Count;
        var retainedPathCount = projectedPaths.Count;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var payload = JsonSerializer.SerializeToElement(new
            {
                operationId = scenario.OperationKey,
                status = "succeeded",
                title = BoundBrowserString(scenario.Description),
                summary = projectedSummary,
                distribution = projectedDistribution,
                canonical.TotalResultCardinality,
                items = projectedItems.Take(retainedItemCount).ToArray(),
                orderedPaths = projectedPaths.Take(retainedPathCount).ToArray(),
                canonical.ResultDigest,
                presentationBounds = new
                {
                    maximumPayloadBytes = MaximumBrowserResultPayloadBytes,
                    maximumItems = MaximumBrowserResultItems,
                    maximumOrderedPaths = MaximumBrowserResultPaths,
                    maximumStructuredArrayElements = MaximumBrowserStructuredArrayElements,
                    maximumObjectProperties = MaximumBrowserObjectProperties,
                    maximumProjectionDepth = MaximumBrowserProjectionDepth,
                    maximumStringUtf16CodeUnits = MaximumBrowserStringUtf16CodeUnits,
                    maximumStructuredValueBytes = MaximumBrowserStructuredValueBytes,
                    maximumItemBytes = MaximumBrowserItemBytes,
                    maximumSummaryOrDistributionBytes = MaximumBrowserSummaryBytes
                },
                presentationOmissions = new
                {
                    sourceItemCount = canonical.Items.Count,
                    retainedItemCount,
                    omittedItemCount = canonical.Items.Count - retainedItemCount,
                    sourceOrderedPathCount = canonical.OrderedPaths.Count,
                    retainedOrderedPathCount = retainedPathCount,
                    omittedOrderedPathCount = canonical.OrderedPaths.Count - retainedPathCount
                }
            }, JsonOptions);
            if (Encoding.UTF8.GetByteCount(payload.GetRawText()) <= MaximumBrowserResultPayloadBytes)
            {
                return new BrowserResultPayloadProjection(payload, retainedItemCount);
            }

            // Preserve result rows before textual path projections. If an
            // unusually large bounded projection still exceeds the global
            // protocol budget, trim deterministically from the end and expose
            // exact omissions in presentationOmissions.
            if (retainedPathCount > 0)
            {
                retainedPathCount--;
                continue;
            }

            if (retainedItemCount > 0)
            {
                retainedItemCount--;
                continue;
            }

            throw new InvalidOperationException(
                "The bounded browser result identity exceeded its global protocol byte limit.");
        }
    }

    private static JsonElement ProjectBrowserPath(OrderedPathProjection path, int index)
    {
        var nodeIds = ProjectBrowserIdentifiers(path.NodeIds);
        var edgeIds = ProjectBrowserIdentifiers(path.EdgeIds);
        return JsonSerializer.SerializeToElement(new
        {
            pathIndex = index,
            nodeIds = nodeIds.Values,
            nodeIdCount = path.NodeIds.Count,
            retainedNodeIdCount = nodeIds.Values.Count,
            omittedNodeIdCount = path.NodeIds.Count - nodeIds.Values.Count,
            truncatedNodeIdCount = nodeIds.TruncatedStringCount,
            edgeIds = edgeIds.Values,
            edgeIdCount = path.EdgeIds.Count,
            retainedEdgeIdCount = edgeIds.Values.Count,
            omittedEdgeIdCount = path.EdgeIds.Count - edgeIds.Values.Count,
            truncatedEdgeIdCount = edgeIds.TruncatedStringCount,
            score = path.AccumulatedScore,
            path.AccumulatedScore
        }, JsonOptions);
    }

    private static BrowserIdentifierProjection ProjectBrowserIdentifiers(
        IReadOnlyList<string> identifiers)
    {
        var values = new List<string>(Math.Min(
            identifiers.Count,
            MaximumBrowserStructuredArrayElements));
        var truncatedStringCount = 0;
        foreach (var identifier in identifiers.Take(MaximumBrowserStructuredArrayElements))
        {
            var bounded = BoundBrowserString(identifier);
            if (!string.Equals(identifier, bounded, StringComparison.Ordinal))
            {
                truncatedStringCount++;
            }

            values.Add(bounded);
            if (JsonSerializer.SerializeToUtf8Bytes(values, JsonOptions).Length <=
                MaximumBrowserStructuredValueBytes)
            {
                continue;
            }

            values.RemoveAt(values.Count - 1);
            break;
        }

        return new BrowserIdentifierProjection(values.AsReadOnly(), truncatedStringCount);
    }

    private static JsonElement ProjectBrowserJsonRoot(
        JsonElement value,
        string path,
        int maximumBytes)
    {
        var omissions = new BrowserProjectionOmissionCollector();
        var projected = ProjectBrowserJson(value, path, 0, maximumBytes, omissions);
        if (projected is JsonObject projectedObject && omissions.TotalCount > 0)
        {
            projectedObject["_browserProjection"] = JsonSerializer.SerializeToNode(new
            {
                omissionCount = omissions.TotalCount,
                retainedOmissionRecords = omissions.Records.Count,
                omittedOmissionRecordCount = omissions.TotalCount - omissions.Records.Count,
                omissions = omissions.Records
            }, JsonOptions);
        }

        if (BrowserJsonByteCount(projected) > maximumBytes)
        {
            projected = JsonSerializer.SerializeToNode(new
            {
                _browserProjection = new
                {
                    sourcePath = path,
                    fullyOmitted = true,
                    reason = "maximum-projected-value-bytes",
                    maximumBytes,
                    sourceKind = value.ValueKind.ToString()
                }
            }, JsonOptions);
        }

        return JsonSerializer.SerializeToElement(projected, JsonOptions);
    }

    private static JsonNode? ProjectBrowserJson(
        JsonElement value,
        string path,
        int depth,
        int rootMaximumBytes,
        BrowserProjectionOmissionCollector omissions)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.Object:
            {
                var properties = value.EnumerateObject()
                    .OrderBy(property => property.Name, StringComparer.Ordinal)
                    .ToArray();
                if (depth >= MaximumBrowserProjectionDepth)
                {
                    omissions.Add(path, "maximum-depth-object", properties.Length, 0);
                    return JsonValue.Create($"{{{properties.Length} properties omitted at projection depth limit}}");
                }

                var maximumBytes = depth == 0
                    ? rootMaximumBytes
                    : MaximumBrowserStructuredValueBytes;
                var projected = new JsonObject();
                var retainedProperties = 0;
                foreach (var property in properties.Take(MaximumBrowserObjectProperties))
                {
                    var propertyPath = $"{path}.{property.Name}";
                    projected[property.Name] = ProjectBrowserJson(
                        property.Value,
                        propertyPath,
                        depth + 1,
                        rootMaximumBytes,
                        omissions);
                    if (BrowserJsonByteCount(projected) <= Math.Max(256, maximumBytes - 1_024))
                    {
                        retainedProperties++;
                        continue;
                    }

                    projected.Remove(property.Name);
                    omissions.Add(propertyPath, "maximum-projected-value-bytes", 1, 0);
                }

                if (properties.Length > MaximumBrowserObjectProperties)
                {
                    omissions.Add(
                        path,
                        "maximum-object-properties",
                        properties.Length,
                        Math.Min(properties.Length, MaximumBrowserObjectProperties));
                }

                if (retainedProperties < Math.Min(properties.Length, MaximumBrowserObjectProperties))
                {
                    omissions.Add(
                        path,
                        "object-property-byte-budget",
                        Math.Min(properties.Length, MaximumBrowserObjectProperties),
                        retainedProperties);
                }

                return projected;
            }
            case JsonValueKind.Array:
            {
                var elements = value.EnumerateArray().ToArray();
                if (depth >= MaximumBrowserProjectionDepth)
                {
                    omissions.Add(path, "maximum-depth-array", elements.Length, 0);
                    return JsonValue.Create($"[{elements.Length} entries omitted at projection depth limit]");
                }

                var projected = new JsonArray();
                foreach (var (element, index) in elements
                    .Take(MaximumBrowserStructuredArrayElements)
                    .Select((element, index) => (element, index)))
                {
                    projected.Add(ProjectBrowserJson(
                        element,
                        $"{path}[{index}]",
                        depth + 1,
                        rootMaximumBytes,
                        omissions));
                    if (BrowserJsonByteCount(projected) <= MaximumBrowserStructuredValueBytes)
                    {
                        continue;
                    }

                    projected.RemoveAt(projected.Count - 1);
                    break;
                }

                if (projected.Count < elements.Length)
                {
                    omissions.Add(path, "structured-array-bound", elements.Length, projected.Count);
                }

                return projected;
            }
            case JsonValueKind.String:
            {
                var text = value.GetString() ?? string.Empty;
                var bounded = BoundBrowserString(text);
                if (!string.Equals(text, bounded, StringComparison.Ordinal))
                {
                    omissions.Add(
                        path,
                        "maximum-string-utf16-code-units",
                        text.Length,
                        bounded.Length);
                }
                return JsonValue.Create(bounded);
            }
            case JsonValueKind.Number:
                return JsonNode.Parse(value.GetRawText());
            case JsonValueKind.True:
                return JsonValue.Create(true);
            case JsonValueKind.False:
                return JsonValue.Create(false);
            case JsonValueKind.Null:
            case JsonValueKind.Undefined:
                return null;
            default:
                throw new JsonException($"Unsupported JSON value kind '{value.ValueKind}'.");
        }
    }

    private static string BoundBrowserString(string value)
    {
        if (value.Length <= MaximumBrowserStringUtf16CodeUnits) return value;
        var suffix = $"… [truncated from {value.Length} UTF-16 code units]";
        var retainedLength = Math.Max(0, MaximumBrowserStringUtf16CodeUnits - suffix.Length);
        return value[..retainedLength] + suffix;
    }

    private static int BrowserJsonByteCount(JsonNode? value) =>
        Encoding.UTF8.GetByteCount(value?.ToJsonString(JsonOptions) ?? "null");

    private sealed record BrowserResultPayloadProjection(
        JsonElement Payload,
        int RetainedItemCount);

    private sealed record BrowserIdentifierProjection(
        IReadOnlyList<string> Values,
        int TruncatedStringCount);

    private sealed record BrowserProjectionOmission(
        string Path,
        string Kind,
        int OriginalCount,
        int RetainedCount,
        int OmittedCount);

    private sealed class BrowserProjectionOmissionCollector
    {
        private const int MaximumRetainedRecords = 16;

        public int TotalCount { get; private set; }

        public List<BrowserProjectionOmission> Records { get; } = [];

        public void Add(string path, string kind, int originalCount, int retainedCount)
        {
            TotalCount++;
            if (Records.Count >= MaximumRetainedRecords) return;
            Records.Add(new BrowserProjectionOmission(
                BoundBrowserString(path),
                kind,
                originalCount,
                retainedCount,
                Math.Max(0, originalCount - retainedCount)));
        }
    }

    private static CompactRunOutput CreateBrowserOutput(
        PreparedBenchmarkOperation operation,
        BenchmarkScenarioDefinition scenario,
        DeterministicStressGraphFixture fixture,
        BrowserJourneyDefinition browser,
        BrowserJourneyTerminalEvidence terminal,
        IReadOnlyList<BrowserPhaseObservation> phases,
        string? browserApiScheme,
        string browserApiOriginTopology)
    {
        var nextHopProtocol = ReadEvidenceText(terminal.Evidence, "nextHopProtocol");
        var resourceTimingLimitation = ReadEvidenceText(
            terminal.Evidence,
            "resourceTimingLimitation");
        JsonElement[] items;
        JsonElement summary;
        long cardinality;
        if (scenario.OperationKey == OperationKeys.GraphFetch)
        {
            var item = JsonSerializer.SerializeToElement(new
            {
                slug = scenario.DatasetId,
                actualNodeCount = terminal.ActualNodeCount,
                actualEdgeCount = terminal.ActualEdgeCount,
                observedPayloadFingerprint = terminal.ResponsePayloadSha256
            }, JsonOptions);
            items = [item];
            cardinality = 1;
            summary = JsonSerializer.SerializeToElement(new
            {
                slug = scenario.DatasetId,
                browserAction = browser.Action,
                terminal.ActualNodeCount,
                terminal.ActualEdgeCount,
                terminal.RenderedNodeCount,
                terminal.RenderedEdgeCount,
                terminal.RequestBytes,
                terminal.ResponseBytes,
                responseByteSemantics = "decoded-response-body-utf8-bytes",
                browserApiScheme,
                browserApiOriginTopology,
                nextHopProtocol,
                resourceTimingLimitation,
                observedPayloadFingerprint = terminal.ResponsePayloadSha256,
                terminal.IdentityLimitation
            }, JsonOptions);
        }
        else if (scenario.OperationKey == OperationKeys.GraphSearch)
        {
            var requiredIds = terminal.RequiredAncestorNodeIds?
                .Order(StringComparer.Ordinal).ToArray();
            var matchIds = terminal.MatchNodeIds?.Order(StringComparer.Ordinal).ToArray();
            var item = JsonSerializer.SerializeToElement(new
            {
                query = browser.SearchQuery,
                terminal.MatchCount,
                terminal.RequiredAncestorUnionCount,
                matchNodeIds = matchIds,
                requiredAncestorNodeIds = requiredIds,
                terminal.IdentityLimitation
            }, JsonOptions);
            cardinality = terminal.MatchCount ?? 0;
            items = cardinality == 0 ? [] : [item];
            summary = JsonSerializer.SerializeToElement(new
            {
                query = browser.SearchQuery,
                terminal.MatchCount,
                terminal.RequiredAncestorUnionCount,
                terminal.ActualNodeCount,
                terminal.ActualEdgeCount,
                terminal.ResponseBytes,
                browserApiScheme,
                browserApiOriginTopology,
                nextHopProtocol,
                resourceTimingLimitation,
                terminal.IdentityLimitation
            }, JsonOptions);
        }
        else
        {
            throw new InvalidOperationException(
                $"Operation '{scenario.OperationKey}' requires a canonical analysis output for browser rendering.");
        }

        var phaseEvidence = phases.Select(phase => new
        {
            phase.Layer,
            phase.Phase,
            phase.DurationMilliseconds,
            phase.TimingBoundaryProvenance,
            phase.Source,
            phase.Evidence
        }).ToArray();
        var distribution = JsonSerializer.SerializeToElement(new
        {
            browserHarnessVersion = terminal.Version,
            harnessBuildIdentity = ReadEvidenceText(terminal.Evidence, "harnessBuildIdentity"),
            phaseEvidence,
            unexpectedConsoleErrors = terminal.UnexpectedConsoleErrors ?? [],
            pageErrors = terminal.PageErrors ?? [],
            exactSuppressions = terminal.ExactSuppressions ?? [],
            terminal.Evidence,
            networkProtocol = new
            {
                configuredScheme = browserApiScheme,
                nextHopProtocol,
                resourceTimingLimitation
            },
            transportBoundaryDisclosure = browserApiOriginTopology == "cross-origin"
                ? "The consumer fetch start-to-headers/full-transfer boundaries may include the custom correlation-header CORS preflight."
                : "The browser API and harness are same-origin; no cross-origin CORS preflight is expected.",
            reconciliation = CreateTimingReconciliation(phases)
        }, JsonOptions);
        return new CompactRunOutput(
            operation.Request.RunId,
            operation.Request.SampleId,
            scenario.Key,
            scenario.OperationKey,
            operation.Request.AlgorithmSemanticIdentity,
            operation.Strategy,
            new GraphTargetIdentifiers(
                fixture.Specification.Slug,
                fixture.Specification.GraphId.ToString(CultureInfo.InvariantCulture),
                null,
                []),
            operation.Request.CanonicalParameters,
            new ExecutionOutcome(ExecutionStatus.Succeeded),
            summary,
            distribution,
            cardinality,
            items,
            CanonicalJson.ComputeSha256(items),
            null,
            []);
    }

    private static CompactRunOutput CreateFailureEvidenceOutput(
        PreparedBenchmarkOperation operation,
        BenchmarkScenarioDefinition scenario,
        DeterministicStressGraphFixture fixture,
        BrowserJourneyDefinition browser,
        ExecutionOutcome execution,
        BrowserJourneyTerminalEvidence? terminal,
        BrowserJourneyDriverResult driverResult,
        string? browserApiScheme,
        string browserApiOriginTopology)
    {
        var items = Array.Empty<JsonElement>();
        var summary = JsonSerializer.SerializeToElement(new
        {
            browserEvidenceOnly = true,
            browserAction = browser.Action,
            execution,
            terminal?.ActualNodeCount,
            terminal?.ActualEdgeCount,
            terminal?.RenderedNodeCount,
            terminal?.RenderedEdgeCount,
            terminal?.MatchCount,
            terminal?.RequiredAncestorUnionCount,
            terminal?.TotalResultCardinality,
            terminal?.BoundedResultItemCount,
            terminal?.IdentityLimitation,
            process = new
            {
                driverResult.ProcessId,
                driverResult.ExitCode,
                driverResult.ForcedTermination
            }
        }, JsonOptions);
        var distribution = JsonSerializer.SerializeToElement(new
        {
            browserHarnessVersion = terminal?.Version ?? HarnessVersion,
            harnessBuildIdentity = ReadEvidenceText(terminal?.Evidence, "harnessBuildIdentity"),
            phaseEvidence = driverResult.Phases.Select(phase => new
            {
                phase.Layer,
                phase.Phase,
                phase.DurationMilliseconds,
                phase.TimingBoundaryProvenance,
                phase.Source,
                phase.Evidence
            }).ToArray(),
            terminalEvidence = terminal?.Evidence,
            networkProtocol = new
            {
                configuredScheme = browserApiScheme,
                nextHopProtocol = ReadEvidenceText(terminal?.Evidence, "nextHopProtocol"),
                resourceTimingLimitation = ReadEvidenceText(
                    terminal?.Evidence,
                    "resourceTimingLimitation")
            },
            browserApiOriginTopology,
            transportBoundaryDisclosure = browserApiOriginTopology == "cross-origin"
                ? "The consumer fetch boundaries may include the custom correlation-header CORS preflight."
                : null,
            unexpectedConsoleErrors = terminal?.UnexpectedConsoleErrors ?? [],
            pageErrors = terminal?.PageErrors ?? [],
            exactSuppressions = terminal?.ExactSuppressions ?? [],
            reconciliation = CreateTimingReconciliation(driverResult.Phases)
        }, JsonOptions);
        return new CompactRunOutput(
            operation.Request.RunId,
            operation.Request.SampleId,
            scenario.Key,
            scenario.OperationKey,
            operation.Request.AlgorithmSemanticIdentity,
            operation.Strategy,
            new GraphTargetIdentifiers(
                fixture.Specification.Slug,
                fixture.Specification.GraphId.ToString(CultureInfo.InvariantCulture),
                operation.TargetNodeId,
                []),
            operation.Request.CanonicalParameters,
            execution,
            summary,
            distribution,
            0,
            items,
            CanonicalJson.ComputeSha256(items),
            null,
            []);
    }

    private static CompactRunOutput EnrichAnalysisOutput(
        CompactRunOutput canonical,
        BrowserJourneyTerminalEvidence terminal,
        IReadOnlyList<BrowserPhaseObservation> phases)
    {
        var summary = JsonNode.Parse(canonical.Summary.GetRawText())?.AsObject()
            ?? throw new JsonException("Canonical analysis summary must be an object.");
        summary["browserPresentation"] = JsonSerializer.SerializeToNode(new
        {
            terminal.TotalResultCardinality,
            terminal.BoundedResultItemCount,
            terminal.IdentityLimitation
        }, JsonOptions);

        var distribution = JsonNode.Parse(canonical.Distribution.GetRawText())?.AsObject()
            ?? throw new JsonException("Canonical analysis distribution must be an object.");
        distribution["browserJourneyEvidence"] = JsonSerializer.SerializeToNode(new
        {
            browserHarnessVersion = terminal.Version,
            harnessBuildIdentity = ReadEvidenceText(terminal.Evidence, "harnessBuildIdentity"),
            phaseEvidence = phases.Select(phase => new
            {
                phase.Layer,
                phase.Phase,
                phase.DurationMilliseconds,
                phase.TimingBoundaryProvenance,
                phase.Source,
                phase.Evidence
            }).ToArray(),
            unexpectedConsoleErrors = terminal.UnexpectedConsoleErrors ?? [],
            pageErrors = terminal.PageErrors ?? [],
            exactSuppressions = terminal.ExactSuppressions ?? [],
            terminal.Evidence,
            reconciliation = CreateTimingReconciliation(phases)
        }, JsonOptions);

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
            JsonSerializer.SerializeToElement(summary, JsonOptions),
            JsonSerializer.SerializeToElement(distribution, JsonOptions),
            canonical.TotalResultCardinality,
            canonical.Items,
            canonical.ResultDigest,
            canonical.FullResultArtifactReference,
            canonical.OrderedPaths);
    }

    private static object CreateTimingReconciliation(IReadOnlyList<BrowserPhaseObservation> phases)
    {
        decimal? Duration(string layer, string phase) => phases.LastOrDefault(item =>
            item.Layer == layer && item.Phase == phase)?.DurationMilliseconds;
        var headers = Duration(InsightMeasurementLayers.Transport, InsightMeasurementPhases.TimeToFirstByte);
        var transfer = Duration(InsightMeasurementLayers.Transport, InsightMeasurementPhases.FullTransfer);
        var stable = Duration(InsightMeasurementLayers.EndToEnd, InsightMeasurementPhases.ActionToStableResultAndView);
        return new
        {
            interpretation = "derived-overhead-observations-not-phase-sums-or-equality-assertions",
            headersToFullTransferMilliseconds = headers is not null && transfer is not null
                ? transfer - headers
                : null,
            fullTransferToStableViewMilliseconds = transfer is not null && stable is not null
                ? stable - transfer
                : null
        };
    }

    private static RunSample CreateBrowserSample(
        PreparedBenchmarkOperation operation,
        BenchmarkScenarioDefinition scenario,
        DeterministicStressGraphFixture fixture,
        BenchmarkProfileDefinition profile,
        ExecutionOutcome execution,
        BrowserJourneyTerminalEvidence? terminal,
        BrowserPhaseObservation phase) => new(
            operation.Request.RunId,
            operation.Request.SampleId,
            scenario.Key,
            scenario.OperationKey,
            phase.Layer,
            phase.Phase,
            phase.DurationMilliseconds,
            0,
            MeasuredClassification(profile),
            new SampleNodeCounts(
                fixture.NodeCount,
                terminal?.ActualNodeCount,
                fixture.NodeCount,
                terminal?.RenderedNodeCount),
            new SampleEdgeCounts(
                fixture.EdgeCount,
                terminal?.RenderedEdgeCount,
                fixture.NodeCount == 0 ? null : (decimal)fixture.EdgeCount / fixture.NodeCount),
            new SampleSearchCounts(
                terminal?.MatchCount,
                terminal?.RequiredAncestorUnionCount),
            terminal?.TotalResultCardinality,
            new SampleTransportMeasurements(
                terminal?.RequestBytes,
                terminal?.ResponseBytes,
                phase.Phase == InsightMeasurementPhases.TimeToFirstByte
                    ? phase.DurationMilliseconds
                    : null,
                phase.Phase == InsightMeasurementPhases.FullTransfer
                    ? phase.DurationMilliseconds
                    : null),
            new RuntimeResourceMeasurements(null, null, null, null, null, "ms", null),
            execution,
            BenchmarkOperationExecutor.StandardUnits,
            phase.TimingBoundaryProvenance,
            null);

    private static RunSample CreateOperationSample(
        PreparedBenchmarkOperation operation,
        BenchmarkScenarioDefinition scenario,
        DeterministicStressGraphFixture fixture,
        BenchmarkProfileDefinition profile,
        ExecutionOutcome execution,
        decimal duration) => new(
            operation.Request.RunId,
            operation.Request.SampleId,
            scenario.Key,
            scenario.OperationKey,
            InsightMeasurementLayers.BenchmarkOrchestration,
            InsightMeasurementPhases.OperationExecution,
            Math.Max(0, duration),
            0,
            MeasuredClassification(profile),
            new SampleNodeCounts(fixture.NodeCount, null, fixture.NodeCount, null),
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

    private static RunSample CreateSetupSample(
        PreparedBenchmarkOperation operation,
        BenchmarkScenarioDefinition scenario,
        DeterministicStressGraphFixture fixture,
        ExecutionOutcome execution,
        decimal duration) => new(
            operation.Request.RunId,
            operation.Request.SampleId,
            scenario.Key,
            scenario.OperationKey,
            InsightMeasurementLayers.BenchmarkOrchestration,
            InsightMeasurementPhases.FixtureConstruction,
            duration,
            0,
            new IterationClassification(
                IterationClassificationTokens.Setup,
                IterationClassificationTokens.Cold,
                IterationClassificationTokens.PreJit,
                IterationClassificationTokens.ColdCache),
            new SampleNodeCounts(fixture.NodeCount, null, fixture.NodeCount, null),
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

    private static RunSample CreateResultPreparationSample(
        PreparedBenchmarkOperation operation,
        BenchmarkScenarioDefinition scenario,
        DeterministicStressGraphFixture fixture,
        BenchmarkProfileDefinition profile,
        ExecutionOutcome execution,
        decimal duration) => new(
            operation.Request.RunId,
            operation.Request.SampleId,
            scenario.Key,
            scenario.OperationKey,
            InsightMeasurementLayers.BenchmarkOrchestration,
            InsightMeasurementPhases.OperationExecution,
            duration,
            0,
            new IterationClassification(
                IterationClassificationTokens.Setup,
                string.Equals(profile.Key, "cold", StringComparison.Ordinal)
                    ? IterationClassificationTokens.Cold
                    : IterationClassificationTokens.Warm,
                IterationClassificationTokens.PreJit,
                string.Equals(profile.Key, "cold", StringComparison.Ordinal)
                    ? IterationClassificationTokens.ColdCache
                    : IterationClassificationTokens.WarmCache),
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

    private static IterationClassification MeasuredClassification(BenchmarkProfileDefinition profile) => new(
        IterationClassificationTokens.Measured,
        string.Equals(profile.Key, "cold", StringComparison.Ordinal)
            ? IterationClassificationTokens.Cold
            : IterationClassificationTokens.Warm,
        IterationClassificationTokens.PostJit,
        string.Equals(profile.Key, "cold", StringComparison.Ordinal)
            ? IterationClassificationTokens.ColdCache
            : IterationClassificationTokens.WarmCache);

    private static bool IsPersistablePhase(BrowserPhaseObservation phase) =>
        InsightPhaseRegistry.IsKnown(phase.Layer, phase.Phase) &&
        phase.DurationMilliseconds >= 0 &&
        !string.IsNullOrWhiteSpace(phase.Source) &&
        phase.Evidence.ValueKind == JsonValueKind.Object &&
        phase.Evidence.EnumerateObject().Any();

    private static bool HasMonotonicBoundaryEvidence(BrowserPhaseObservation phase)
    {
        var evidence = phase.Evidence;
        if (!evidence.TryGetProperty("startMilliseconds", out var start) ||
            start.ValueKind != JsonValueKind.Number || !start.TryGetDecimal(out var startValue) ||
            !evidence.TryGetProperty("endMilliseconds", out var end) ||
            end.ValueKind != JsonValueKind.Number || !end.TryGetDecimal(out var endValue) ||
            startValue < 0 || endValue < startValue)
        {
            return false;
        }

        var boundaryDuration = endValue - startValue;
        var tolerance = Math.Max(0.25m, Math.Abs(phase.DurationMilliseconds) * 0.05m);
        return Math.Abs(boundaryDuration - phase.DurationMilliseconds) <= tolerance;
    }

    private static bool HasDefensibleProvenance(BrowserPhaseObservation phase)
    {
        if (phase.Layer is InsightMeasurementLayers.PostgreSqlRepository or
            InsightMeasurementLayers.BackendServiceApi or
            InsightMeasurementLayers.Transport)
        {
            return phase.TimingBoundaryProvenance == TimingBoundaryProvenance.DirectlyInstrumented;
        }

        if (phase.Layer == InsightMeasurementLayers.BrowserData)
        {
            if (phase.Phase == InsightMeasurementPhases.SearchCompletion)
            {
                return phase.TimingBoundaryProvenance == TimingBoundaryProvenance.ExternallyObserved;
            }

            return phase.Phase != InsightMeasurementPhases.SearchIndexConstruction &&
                phase.TimingBoundaryProvenance == TimingBoundaryProvenance.DirectlyInstrumented;
        }

        if (phase.Layer == InsightMeasurementLayers.LabResult)
        {
            return phase.TimingBoundaryProvenance == TimingBoundaryProvenance.DirectlyInstrumented;
        }

        if (phase.Layer == InsightMeasurementLayers.EndToEnd)
        {
            return phase.TimingBoundaryProvenance == TimingBoundaryProvenance.ExternallyObserved;
        }

        if (phase.Layer != InsightMeasurementLayers.GraphMap) return false;
        return phase.Phase switch
        {
            InsightMeasurementPhases.ReactCommit =>
                phase.TimingBoundaryProvenance == TimingBoundaryProvenance.DirectlyInstrumented,
            InsightMeasurementPhases.NodeEdgeMaterialization =>
                phase.TimingBoundaryProvenance == TimingBoundaryProvenance.ExternallyObserved,
            InsightMeasurementPhases.DeferredEdgeCommit =>
                phase.TimingBoundaryProvenance is
                    TimingBoundaryProvenance.ExternallyObserved or TimingBoundaryProvenance.Estimated,
            InsightMeasurementPhases.DagreLayout or InsightMeasurementPhases.ViewportFit =>
                phase.TimingBoundaryProvenance == TimingBoundaryProvenance.Estimated,
            _ => false
        };
    }

    private static bool IsSha256(string? value) =>
        value is { Length: 71 } && value.StartsWith("sha256:", StringComparison.Ordinal) &&
        value.AsSpan(7).IndexOfAnyExcept("0123456789abcdef") < 0;

    private static string? ReadEvidenceText(JsonElement? evidence, string propertyName) =>
        evidence is { ValueKind: JsonValueKind.Object } value &&
        value.TryGetProperty(propertyName, out var property) &&
        property.ValueKind == JsonValueKind.String &&
        !string.IsNullOrWhiteSpace(property.GetString())
            ? property.GetString()
            : null;

    private static ExecutionOutcome BrowserValidationFailure(string code, string message) =>
        new(
            ExecutionStatus.Failed,
            FailureDetails.Validation(
                [new ValidationFailure("browserJourney", code, message)],
                code,
                message));

    private string CreateEnvironmentProfile(
        string browserVersion,
        bool usesApi,
        string browserOriginTopology)
    {
        var browserMajor = browserVersion
            .Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault() ?? "unknown";
        return string.Join(
            ':',
            "api-browser-real-process",
            usesApi
                ? string.Join(
                    '-',
                    _options.ApiBaseAddress!.IsLoopback ? "api-loopback" : "api-remote",
                    _options.ApiBaseAddress.Scheme)
                : "api-not-used",
            browserOriginTopology,
            _options.HarnessUrl.IsLoopback ? "harness-loopback" : "harness-remote",
            ExpectedHarnessBuildIdentity,
            $"browser-{browserMajor}");
    }

    private string BrowserOriginTopology(bool usesApi)
    {
        if (!usesApi || _options.ApiBaseAddress is null) return "origin-not-used";
        return SameOrigin(_options.HarnessUrl, _options.ApiBaseAddress)
            ? "same-origin"
            : "cross-origin";
    }

    private static bool SameOrigin(Uri left, Uri right) =>
        string.Equals(left.Scheme, right.Scheme, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(left.IdnHost, right.IdnHost, StringComparison.OrdinalIgnoreCase) &&
        left.Port == right.Port;

    private static decimal ElapsedMilliseconds(long started) =>
        (Stopwatch.GetTimestamp() - started) * 1000m / Stopwatch.Frequency;
}
