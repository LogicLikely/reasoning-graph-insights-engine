using System.Text.Json.Nodes;

namespace Backend.Reporting;

public sealed record PerformanceRunRecord
{
    public long RunNumber { get; init; }

    public string? BenchmarkSetId { get; init; }

    public required DateTimeOffset StartedAtUtc { get; init; }

    public required PerformanceAlgorithmInfo Algorithm { get; init; }

    public required PerformanceBuildInfo Build { get; init; }

    public required PerformanceGraphInfo Graph { get; init; }

    public required PerformanceInvocationInfo Invocation { get; init; }

    public required PerformanceTimingInfo Timing { get; init; }

    public required PerformanceResourceInfo Resources { get; init; }

    public required PerformanceOutcomeInfo Outcome { get; init; }

    public JsonObject Details { get; init; } = new();
}

public static class PerformanceReportingHeaders
{
    public const string BenchmarkSetId = "X-Insights-Benchmark-Set-Id";
}

public sealed record PerformanceAlgorithmInfo
{
    public required string Name { get; init; }

    public required string Implementation { get; init; }

    public string? CalculationModel { get; init; }
}

public sealed record PerformanceBuildInfo
{
    public string? GitCommit { get; init; }

    public bool? Dirty { get; init; }

    public string? GitBranch { get; init; }

    public required string Configuration { get; init; }

    public required string DotNetVersion { get; init; }

    public required string OperatingSystem { get; init; }

    public required string ProcessArchitecture { get; init; }

    public required int LogicalProcessorCount { get; init; }

    public required bool ServerGarbageCollection { get; init; }
}

public sealed record PerformanceGraphInfo
{
    public required string Slug { get; init; }

    public string? Type { get; init; }

    public required int NodeCount { get; init; }

    public required int EdgeCount { get; init; }

    public int? MaximumDepth { get; init; }

    public IReadOnlyDictionary<string, int> NodeKindCounts { get; init; } =
        new Dictionary<string, int>(StringComparer.Ordinal);

    public string? Fingerprint { get; init; }
}

public sealed record PerformanceInvocationInfo
{
    public required string DataSource { get; init; }

    public string? TargetNodeId { get; init; }

    public string? ChangedNodeId { get; init; }

    public string? ChangedField { get; init; }

    public JsonNode? OldValue { get; init; }

    public JsonNode? NewValue { get; init; }

    public JsonObject Parameters { get; init; } = new();
}

public sealed record PerformanceTimingInfo
{
    public double? LoadElapsedMilliseconds { get; init; }

    public required double ComputeElapsedMilliseconds { get; init; }

    public double? PersistElapsedMilliseconds { get; init; }

    public required double OperationElapsedMilliseconds { get; init; }
}

public sealed record PerformanceResourceInfo
{
    public required double CpuTimeMilliseconds { get; init; }

    public long? AllocatedBytes { get; init; }

    public required int Gen0Collections { get; init; }

    public required int Gen1Collections { get; init; }

    public required int Gen2Collections { get; init; }

    public string CpuMeasurement { get; init; } = "processCpuTimeDelta";

    public string AllocationMeasurement { get; init; } = "currentThreadAllocatedBytesDelta";
}

public sealed record PerformanceOutcomeInfo
{
    public required string Status { get; init; }

    public int? ResultCount { get; init; }

    public string? ResultDigest { get; init; }

    public string? ErrorType { get; init; }

    public string? ErrorMessage { get; init; }
}

public static class PerformanceRunStatuses
{
    public const string Completed = "completed";
    public const string Failed = "failed";
    public const string Cancelled = "cancelled";
    public const string TimedOut = "timedOut";
    public const string NotProven = "notProven";
}

public static class PerformanceAlgorithmNames
{
    public const string MinimalCounterSet = "minimal-counter-set";
    public const string EvidenceImpactRanking = "evidence-impact-ranking";
    public const string LeastRobustNode = "least-robust-node";
    public const string RobustnessRanking = "robustness-ranking";
    public const string LeafUpdate = "leaf-update";
}

public static class PerformanceAlgorithmImplementations
{
    public const string Greedy = "greedy";
    public const string BoundedBruteForce = "bounded-brute-force";
    public const string Current = "current";
}
