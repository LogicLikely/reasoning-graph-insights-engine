using Backend.Insights.Contracts;

namespace Backend.Insights.Benchmarking;

public sealed record BenchmarkProfileDefinition(
    string Key,
    string Description,
    int WarmupIterations,
    int MeasuredIterations,
    TimeSpan DefaultTimeout,
    TimeSpan CancellationGracePeriod,
    string WarmupPolicy,
    string SamplePolicy,
    string JitPolicy,
    string CachePolicy,
    string SampleMode,
    bool ExecutionEnabled,
    bool RequiresFreshChildProcess,
    string ResetDisclosure)
{
    public BenchmarkProfileDefinition Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(Key);
        ArgumentException.ThrowIfNullOrWhiteSpace(Description);
        ArgumentException.ThrowIfNullOrWhiteSpace(WarmupPolicy);
        ArgumentException.ThrowIfNullOrWhiteSpace(SamplePolicy);
        ArgumentException.ThrowIfNullOrWhiteSpace(JitPolicy);
        ArgumentException.ThrowIfNullOrWhiteSpace(CachePolicy);
        ArgumentException.ThrowIfNullOrWhiteSpace(ResetDisclosure);
        ArgumentOutOfRangeException.ThrowIfNegative(WarmupIterations);
        ArgumentOutOfRangeException.ThrowIfNegative(MeasuredIterations);
        if (DefaultTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(DefaultTimeout),
                "A benchmark profile timeout must be positive.");
        }

        if (CancellationGracePeriod <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(CancellationGracePeriod),
                "A benchmark profile cancellation grace period must be positive.");
        }

        if (!RunSampleModeTokens.IsKnown(SampleMode) ||
            SampleMode == RunSampleModeTokens.LegacyUnspecified)
        {
            throw new ArgumentException(
                $"Benchmark profile '{Key}' must declare warm or cold sample mode.",
                nameof(SampleMode));
        }

        if (ExecutionEnabled && MeasuredIterations == 0)
        {
            throw new ArgumentException(
                $"Executable benchmark profile '{Key}' must have at least one measured iteration.",
                nameof(MeasuredIterations));
        }

        if (!ExecutionEnabled && (WarmupIterations != 0 || MeasuredIterations != 0))
        {
            throw new ArgumentException(
                $"Configuration-only benchmark profile '{Key}' cannot schedule iterations.");
        }

        if (RequiresFreshChildProcess && SampleMode != RunSampleModeTokens.Cold)
        {
            throw new ArgumentException(
                $"Fresh-child-process profile '{Key}' must use cold sample mode.",
                nameof(SampleMode));
        }

        if (SampleMode == RunSampleModeTokens.Cold && !RequiresFreshChildProcess)
        {
            throw new ArgumentException(
                $"Cold benchmark profile '{Key}' must require a fresh child process per iteration.",
                nameof(RequiresFreshChildProcess));
        }

        return this;
    }

    public WarmupSampleCachePolicy ToSamplingPolicy() => new(
        WarmupIterations,
        MeasuredIterations,
        WarmupPolicy,
        SamplePolicy,
        JitPolicy,
        CachePolicy,
        SampleMode);
}

public static class BenchmarkProfiles
{
    public const string QuickKey = "quick";
    public const string StandardKey = "standard";
    public const string ColdKey = "cold";
    public const string AuthoritativeKey = "authoritative";

    public static BenchmarkProfileDefinition Quick { get; } = new BenchmarkProfileDefinition(
        QuickKey,
        "Bounded correctness and smoke profile; never an authoritative baseline.",
        WarmupIterations: 0,
        MeasuredIterations: 1,
        DefaultTimeout: TimeSpan.FromSeconds(15),
        CancellationGracePeriod: TimeSpan.FromSeconds(1),
        WarmupPolicy: "none",
        SamplePolicy: "one-recorded-measured-iteration",
        JitPolicy: "not-controlled-without-profile-warmup;fresh-child-for-browser-or-isolated-scenarios",
        CachePolicy: "application-shared-service-postgresql-and-os-caches-not-cleared",
        SampleMode: RunSampleModeTokens.Warm,
        ExecutionEnabled: true,
        RequiresFreshChildProcess: false,
        ResetDisclosure:
            "The profile performs no reset. Browser and isolated scenarios independently launch " +
            "their required fresh child process; the runner, API, PostgreSQL, and OS caches remain shared.").Validate();

    public static BenchmarkProfileDefinition Standard { get; } = new BenchmarkProfileDefinition(
        StandardKey,
        "Repeatable development profile with one recorded warmup and three recorded measured iterations; never an authoritative baseline.",
        WarmupIterations: 1,
        MeasuredIterations: 3,
        DefaultTimeout: TimeSpan.FromMinutes(2),
        CancellationGracePeriod: TimeSpan.FromSeconds(2),
        WarmupPolicy: "one-recorded-warmup-iteration",
        SamplePolicy: "three-recorded-measured-iterations",
        JitPolicy: "in-process-warmed-before-measurement;fresh-child-per-browser-or-isolated-iteration",
        CachePolicy: "application-shared-service-postgresql-and-os-caches-not-cleared",
        SampleMode: RunSampleModeTokens.Warm,
        ExecutionEnabled: true,
        RequiresFreshChildProcess: false,
        ResetDisclosure:
            "The warmup and measured iterations share the runner, API, PostgreSQL, and OS caches. " +
            "Each browser iteration still launches fresh Node and Chromium processes, and each isolated " +
            "algorithm iteration launches a fresh .NET worker; those child-process facts are classified per sample.").Validate();

    public static BenchmarkProfileDefinition Cold { get; } = new BenchmarkProfileDefinition(
        ColdKey,
        "Scoped cold-child-process profile: one measured iteration in a fresh isolated worker or fresh Node plus Chromium; the static Storybook server, shared services, and OS caches are not reset.",
        WarmupIterations: 0,
        MeasuredIterations: 1,
        DefaultTimeout: TimeSpan.FromMinutes(2),
        CancellationGracePeriod: TimeSpan.FromSeconds(2),
        WarmupPolicy: "none",
        SamplePolicy: "one-recorded-measured-iteration-in-a-fresh-child-process",
        JitPolicy: "fresh-isolated-dotnet-worker-or-fresh-node-and-chromium-per-iteration",
        CachePolicy: "fresh-child-process-cache;runner-api-storybook-postgresql-and-os-caches-not-cleared",
        SampleMode: RunSampleModeTokens.Cold,
        ExecutionEnabled: true,
        RequiresFreshChildProcess: true,
        ResetDisclosure:
            "Every measured iteration launches either a fresh isolated .NET worker or fresh Node and " +
            "Chromium processes. The long-lived runner, API, and static production-profiling Storybook " +
            "HTTP server processes, the Storybook server's serving/cache state, PostgreSQL process and " +
            "data cache, and OS page cache are shared and are not restarted or cleared. Cold browser " +
            "execution is restricted " +
            "to API-free result rendering; graph REST/browser journeys are skipped rather than mislabeled cold.").Validate();

    public static BenchmarkProfileDefinition Authoritative { get; } = new BenchmarkProfileDefinition(
        AuthoritativeKey,
        "Phase 6 authoritative-profile configuration and validation surface only; execution and baseline promotion are disabled.",
        WarmupIterations: 0,
        MeasuredIterations: 0,
        DefaultTimeout: TimeSpan.FromMinutes(10),
        CancellationGracePeriod: TimeSpan.FromSeconds(5),
        WarmupPolicy: "phase-6-configuration-deferred",
        SamplePolicy: "execution-disabled-no-samples",
        JitPolicy: "phase-6-configuration-deferred",
        CachePolicy: "phase-6-configuration-deferred",
        SampleMode: RunSampleModeTokens.Warm,
        ExecutionEnabled: false,
        RequiresFreshChildProcess: false,
        ResetDisclosure:
            "No reset is performed because authoritative execution and baseline promotion remain deferred to Phase 6.").Validate();

    public static IReadOnlyList<BenchmarkProfileDefinition> All { get; } =
        Array.AsReadOnly(new[] { Quick, Standard, Cold, Authoritative });

    static BenchmarkProfiles()
    {
        var duplicate = All
            .GroupBy(profile => profile.Key, StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
        {
            throw new InvalidOperationException(
                $"Duplicate benchmark profile key '{duplicate.Key}'.");
        }
    }

    public static BenchmarkProfileDefinition Get(string key) =>
        All.SingleOrDefault(profile => string.Equals(profile.Key, key, StringComparison.Ordinal))
        ?? throw new KeyNotFoundException($"Unknown benchmark profile '{key}'.");

    public static BenchmarkProfileDefinition RequireExecutable(string key)
    {
        var profile = Get(key).Validate();
        if (!profile.ExecutionEnabled)
        {
            throw new InvalidOperationException(
                $"Benchmark profile '{profile.Key}' is configuration-and-validation-only. " +
                "Execution and authoritative baseline promotion remain deferred to Phase 6.");
        }

        return profile;
    }

    public static void ValidateScenarioExecution(
        BenchmarkProfileDefinition profile,
        BenchmarkScenarioDefinition scenario)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(scenario);
        profile.Validate();
        if (!profile.RequiresFreshChildProcess || scenario.IsSkipped)
        {
            return;
        }

        var launchesFreshChild =
            (scenario.ExecutionTarget == BenchmarkScenarioExecutionTarget.Browser &&
             scenario.BrowserJourney?.Action == BrowserJourneyActions.ResultRender) ||
            (scenario.ExecutionTarget == BenchmarkScenarioExecutionTarget.InMemory && scenario.RequiresIsolation);
        if (!launchesFreshChild)
        {
            throw new InvalidOperationException(
                $"Cold profile scenario '{scenario.Key}' is not executable because it does not launch " +
                "a fresh isolated .NET worker or an API-free result-render journey in fresh Node and " +
                "Chromium processes for every iteration. " +
                "REST/API/PostgreSQL and in-process scenarios cannot be labeled cold without a real reset.");
        }
    }
}
