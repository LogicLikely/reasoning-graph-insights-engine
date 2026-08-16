using System.Text.Json;
using Backend.Insights.Contracts;

namespace backend.Tests.Insights.Persistence;

internal static class BenchmarkPersistenceTestData
{
    internal static readonly Guid RunId =
        Guid.Parse("11111111-1111-1111-1111-111111111111");

    internal static readonly Guid SampleId =
        Guid.Parse("22222222-2222-2222-2222-222222222222");

    internal static MeasurementUnitContract Units { get; } =
        new("ms", "ms", "bytes", "bytes", "count", "ratio");

    internal static RunManifest Manifest(
        ExecutionOutcome? execution = null,
        DateTimeOffset? completedAt = null)
    {
        var parameterValue = JsonSerializer.SerializeToElement(new
        {
            includeEvidence = true
        });

        return new RunManifest(
            RunId,
            "phase-1-persistence-fixture",
            execution ?? new ExecutionOutcome(ExecutionStatus.Running),
            DateTimeOffset.Parse("2026-08-15T14:00:00-04:00"),
            completedAt,
            RunnerType.CommandLine,
            "fixture.graph-fetch",
            OperationKeys.GraphFetch,
            new GraphRunIdentity("sample-medium", "1", "balanced", 18, 17, 4),
            new DatasetRunIdentity(
                "fixture-generator-v1",
                "fixture-corpus-v1",
                Digest('a'),
                Digest('b'),
                Digest('c'),
                Digest('d')),
            new AlgorithmRunIdentity(OperationKeys.GraphFetch, AlgorithmSemanticIdentities.GraphFetchV1),
            new StrategySelection(null, null),
            new CanonicalParameters(parameterValue, CanonicalJson.ComputeSha256(parameterValue)),
            new RunTargets(["R1"], []),
            new SourceRevision("0123456789abcdef0123456789abcdef01234567", false),
            "Release",
            "release",
            new DependencyVersions(
                "8.0.0",
                "24.0.0",
                "chromium 140",
                "0.2.0",
                "17.0",
                new Dictionary<string, string> { ["Npgsql"] = "8.0.6" }),
            new Backend.Insights.Contracts.HostEnvironment(
                "test-os",
                "arm64",
                "test-cpu",
                8,
                16_000_000_000),
            "fixture-environment",
            new WarmupSampleCachePolicy(0, 1, "none", "single", "warm", "warm"),
            new TimeoutCancellationPolicy(TimeSpan.FromSeconds(30), "cooperative", true),
            Units);
    }

    internal static RunSample Sample(
        string phase = "graph.lookup",
        ExecutionOutcome? execution = null,
        Guid? sampleId = null)
    {
        return new RunSample(
            RunId,
            sampleId ?? SampleId,
            "fixture.graph-fetch",
            OperationKeys.GraphFetch,
            "postgresql-repository",
            phase,
            1.25m,
            0,
            new IterationClassification(
                IterationClassificationTokens.Measured,
                IterationClassificationTokens.Warm,
                IterationClassificationTokens.PostJit,
                IterationClassificationTokens.WarmCache),
            new SampleNodeCounts(18, 18, 0, null),
            new SampleEdgeCounts(17, null, null),
            new SampleSearchCounts(null, null),
            18,
            new SampleTransportMeasurements(null, null, null, null),
            new RuntimeResourceMeasurements(null, null, null, null, null, "ms", null),
            execution ?? new ExecutionOutcome(ExecutionStatus.Succeeded),
            Units,
            TimingBoundaryProvenance.DirectlyInstrumented,
            new SampleOperationCounters(null, 18, 17, 1, null, null));
    }

    internal static CompactRunOutput Output(
        ExecutionOutcome? execution = null,
        Guid? sampleId = null)
    {
        var parameters = Manifest().CanonicalParameters;
        var item = JsonSerializer.SerializeToElement(new
        {
            graphSlug = "sample-medium",
            nodeCount = 18,
            edgeCount = 17
        });

        return new CompactRunOutput(
            RunId,
            sampleId ?? SampleId,
            "fixture.graph-fetch",
            OperationKeys.GraphFetch,
            AlgorithmSemanticIdentities.GraphFetchV1,
            new StrategySelection(null, null),
            new GraphTargetIdentifiers("sample-medium", "1", "R1", []),
            parameters,
            execution ?? new ExecutionOutcome(ExecutionStatus.Succeeded),
            JsonSerializer.SerializeToElement(new { nodeCount = 18, edgeCount = 17 }),
            JsonSerializer.SerializeToElement(new { payloadBytes = 4_096 }),
            1,
            [item],
            CanonicalJson.ComputeSha256(new[] { item }),
            null,
            []);
    }

    internal static FailureDetails Failure(FailureKind kind)
    {
        return kind == FailureKind.Validation
            ? FailureDetails.Validation(
            [
                new ValidationFailure("targetNodeId", "missing", "The target does not exist.")
            ])
            : new FailureDetails(kind, $"fixture-{kind}", "Fixture failure.", null, false, []);
    }

    internal static string Digest(char value) => $"sha256:{new string(value, 64)}";
}
