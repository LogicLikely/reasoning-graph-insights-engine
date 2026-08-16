namespace Backend.Insights.Benchmarking;

public sealed record BenchmarkProfileDefinition(
    string Key,
    string Description,
    int WarmupIterations,
    int MeasuredIterations,
    TimeSpan DefaultTimeout,
    TimeSpan CancellationGracePeriod);

public static class BenchmarkProfiles
{
    public const string QuickKey = "quick";

    public static BenchmarkProfileDefinition Quick { get; } = new(
        QuickKey,
        "Bounded correctness and smoke profile; never an authoritative baseline.",
        WarmupIterations: 0,
        MeasuredIterations: 1,
        DefaultTimeout: TimeSpan.FromSeconds(15),
        CancellationGracePeriod: TimeSpan.FromSeconds(1));

    public static IReadOnlyList<BenchmarkProfileDefinition> All { get; } = [Quick];

    public static BenchmarkProfileDefinition Get(string key) =>
        All.SingleOrDefault(profile => string.Equals(profile.Key, key, StringComparison.Ordinal))
        ?? throw new KeyNotFoundException($"Unknown benchmark profile '{key}'.");
}
