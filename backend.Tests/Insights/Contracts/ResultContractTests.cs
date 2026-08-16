using System.Text.Json;
using Backend.Insights.Contracts;

namespace backend.Tests.Insights.Contracts;

[TestClass]
public class ResultContractTests
{
    [TestMethod]
    public void StatusEnums_AreExactFrozenSets()
    {
        CollectionAssert.AreEqual(
            new[] { "Queued", "Running", "Succeeded", "Failed", "TimedOut", "Cancelled", "Crashed", "Skipped" },
            Enum.GetNames<ExecutionStatus>());
        CollectionAssert.AreEqual(
            new[] { "NotRequested", "Allowed", "Warned", "Blocked" },
            Enum.GetNames<VisualizationAdmission>());
        CollectionAssert.AreEqual(
            new[] { "Validation", "Execution", "Timeout", "Cancellation", "Crash", "Skip" },
            Enum.GetNames<FailureKind>());

        Assert.AreEqual(
            """{"execution":["queued","running","succeeded","failed","timed-out","cancelled","crashed","skipped"],"failure":["validation","execution","timeout","cancellation","crash","skip"],"visualization":["not-requested","allowed","warned","blocked"]}""",
            CanonicalJson.Canonicalize(new
            {
                Execution = Enum.GetValues<ExecutionStatus>(),
                Failure = Enum.GetValues<FailureKind>(),
                Visualization = Enum.GetValues<VisualizationAdmission>()
            }));
    }

    [TestMethod]
    public void ExecutionAndVisualizationStates_AreOrthogonalAndSerializeAsContractValues()
    {
        var value = new
        {
            Execution = new ExecutionOutcome(ExecutionStatus.Succeeded),
            VisualizationAdmission = VisualizationAdmission.Blocked
        };

        Assert.AreEqual(
            """{"execution":{"failure":null,"status":"succeeded"},"visualizationAdmission":"blocked"}""",
            CanonicalJson.Canonicalize(value));
    }

    [TestMethod]
    public void ValidationFailure_IsFailedExecutionWithValidationKind()
    {
        var outcome = ExecutionOutcome.ValidationFailed(
        [
            new ValidationFailure("targetNodeId", "missing", "The target node does not exist.")
        ]);

        Assert.AreEqual(ExecutionStatus.Failed, outcome.Status);
        Assert.IsNotNull(outcome.Failure);
        Assert.AreEqual(FailureKind.Validation, outcome.Failure.Kind);
        Assert.AreEqual(1, outcome.Failure.ValidationFailures.Count);
    }

    [TestMethod]
    public void TerminalFailureStates_RequireTheirMatchingFailureKinds()
    {
        var cases = new[]
        {
            (ExecutionStatus.Failed, FailureKind.Execution),
            (ExecutionStatus.TimedOut, FailureKind.Timeout),
            (ExecutionStatus.Cancelled, FailureKind.Cancellation),
            (ExecutionStatus.Crashed, FailureKind.Crash),
            (ExecutionStatus.Skipped, FailureKind.Skip)
        };

        foreach (var (status, kind) in cases)
        {
            var outcome = new ExecutionOutcome(status, Failure(kind));
            Assert.AreEqual(kind, outcome.Failure?.Kind);
        }

        Assert.ThrowsException<ArgumentException>(() =>
            new ExecutionOutcome(ExecutionStatus.TimedOut, Failure(FailureKind.Execution)));
        Assert.ThrowsException<ArgumentException>(() =>
            new ExecutionOutcome(ExecutionStatus.Failed));
        Assert.ThrowsException<ArgumentException>(() =>
            new ExecutionOutcome(ExecutionStatus.Succeeded, Failure(FailureKind.Execution)));
        Assert.ThrowsException<ArgumentOutOfRangeException>(() =>
            new ExecutionOutcome((ExecutionStatus)999, Failure(FailureKind.Execution)));
    }

    [TestMethod]
    public void ResultEnvelope_RejectsMoreThanTop100RetainedItems()
    {
        var items = Enumerable.Range(0, 101)
            .Select(value => JsonSerializer.SerializeToElement(new { value }))
            .ToArray();

        Assert.ThrowsException<ArgumentException>(() => CreateEnvelope(items, 101));
    }

    [TestMethod]
    public void ResultEnvelope_CarriesEveryCommonResultSectionAndAllowsSucceededBlocked()
    {
        var parameterValue = JsonSerializer.SerializeToElement(new { direction = "down" });
        var item = JsonSerializer.SerializeToElement(new { nodeId = "target", score = 2m });
        var envelope = new OperationResultEnvelope(
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            Guid.Parse("22222222-2222-2222-2222-222222222222"),
            OperationKeys.PathStrongest,
            AlgorithmSemanticIdentities.StrongestPathV1,
            new StrategySelection(null, null),
            new GraphTargetIdentifiers("balanced-1k", "graph-1", "target", ["path-1"]),
            new CanonicalParameters(parameterValue, CanonicalJson.ComputeSha256(parameterValue)),
            new ExecutionOutcome(ExecutionStatus.Succeeded),
            VisualizationAdmission.Blocked,
            new Dictionary<string, JsonElement>
            {
                ["maximumScore"] = JsonSerializer.SerializeToElement(2m)
            },
            1,
            [item],
            CanonicalJson.ComputeSha256(new[] { item }),
            [new OrderedPathProjection(["leaf", "target"], ["edge"], 2m)],
            [new PhaseTimingMeasurement("backend-service", "ranking", 1.25m, "ms")],
            new RuntimeResourceMeasurements(1_024, 1, 0, 0, 1m, "ms", 512),
            ["Projection exceeded the render budget."]);

        Assert.AreEqual(ExecutionStatus.Succeeded, envelope.Execution.Status);
        Assert.AreEqual(VisualizationAdmission.Blocked, envelope.VisualizationAdmission);
        Assert.AreEqual(1, envelope.SummaryMetrics.Count);
        Assert.AreEqual(1, envelope.Items.Count);
        Assert.AreEqual(1, envelope.OrderedPaths.Count);
        Assert.AreEqual(1, envelope.PhaseTimings.Count);
        Assert.AreEqual(1_024, envelope.Resources.AllocatedBytes);
        Assert.AreEqual(1, envelope.Warnings.Count);

        var canonical = CanonicalJson.Canonicalize(envelope);
        StringAssert.Contains(canonical, "\"algorithmSemanticIdentity\":\"strongest-path-v1\"");
        StringAssert.Contains(canonical, "\"visualizationAdmission\":\"blocked\"");
        StringAssert.Contains(canonical, "\"orderedPaths\"");
        StringAssert.Contains(canonical, "\"phaseTimings\"");
    }

    [TestMethod]
    public void CompactRunOutput_PreservesOptionalFullArtifactReference()
    {
        var output = new CompactRunOutput(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "catalog-all",
            OperationKeys.GraphCatalog,
            AlgorithmSemanticIdentities.GraphCatalogV1,
            new StrategySelection(null, null),
            new GraphTargetIdentifiers("all", null, null, []),
            new CanonicalParameters(
                JsonSerializer.SerializeToElement(new { }),
                CanonicalJson.ComputeSha256(new { })),
            new ExecutionOutcome(ExecutionStatus.Succeeded),
            VisualizationAdmission.NotRequested,
            JsonSerializer.SerializeToElement(new { count = 1 }),
            JsonSerializer.SerializeToElement(new { minimum = 1 }),
            0,
            [],
            "sha256:result",
            "artifacts/run-1/full-result.json",
            [],
            []);

        Assert.AreEqual("artifacts/run-1/full-result.json", output.FullResultArtifactReference);
        StringAssert.Contains(
            CanonicalJson.Canonicalize(output),
            "\"fullResultArtifactReference\":\"artifacts/run-1/full-result.json\"");
    }

    [TestMethod]
    public void VersionedRunExport_CarriesCompleteManifestSamplesOutputsAndDigests()
    {
        var parametersValue = JsonSerializer.SerializeToElement(new { thresholdLogOdds = -1m });
        var parameters = new CanonicalParameters(parametersValue, CanonicalJson.ComputeSha256(parametersValue));
        var units = StandardUnits();
        var runId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var sampleId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var manifest = new RunManifest(
            runId,
            "phase-0-contract-run",
            new ExecutionOutcome(ExecutionStatus.Succeeded),
            DateTimeOffset.Parse("2026-08-15T12:00:00Z"),
            null,
            RunnerType.CommandLine,
            "balanced-1k",
            OperationKeys.NodeRobustness,
            new GraphRunIdentity("stress-balanced-1k", "graph-10", "balanced", 1_000, 999, 10),
            new DatasetRunIdentity("generator-v1", "corpus-v1", "sha256:corpus", "sha256:topology", "sha256:input", "sha256:dataset-input"),
            new AlgorithmRunIdentity("node.robustness", AlgorithmSemanticIdentities.RobustnessV0),
            new StrategySelection(null, null),
            parameters,
            new RunTargets(["target"], ["path-1"]),
            new SourceRevision("0123456789abcdef", false),
            "Release",
            "release",
            new DependencyVersions("8.0.407", "24.13.0", "chromium", "0.2.0", "16", new Dictionary<string, string> { ["npgsql"] = "8.0.6" }),
            new HostEnvironment("macOS", "arm64", "Apple", 10, 16_000_000_000),
            "ll-arm64-mac-primary",
            new WarmupSampleCachePolicy(1, 5, "one warmup", "five samples", "recorded", "warm"),
            new TimeoutCancellationPolicy(TimeSpan.FromSeconds(30), "cooperative", true),
            units);
        var sample = new RunSample(
            runId,
            sampleId,
            "balanced-1k",
            OperationKeys.NodeRobustness,
            "backend-service",
            "algorithm",
            12.5m,
            0,
            new IterationClassification("measured", "warm", "post-jit", "warm-cache"),
            new SampleNodeCounts(1_000, 1_000, 0, null),
            new SampleEdgeCounts(999, null, null),
            new SampleSearchCounts(null, null),
            1_000,
            new SampleTransportMeasurements(256, 2_048, 2m, 3m),
            new RuntimeResourceMeasurements(10_000, 1, 0, 0, 11m, "ms", 4_096),
            new ExecutionOutcome(ExecutionStatus.Succeeded),
            VisualizationAdmission.NotRequested,
            ["Warm sample."],
            units);
        var item = JsonSerializer.SerializeToElement(new { nodeId = "node-1", score = 0.9m });
        var output = new CompactRunOutput(
            runId,
            sampleId,
            "balanced-1k",
            OperationKeys.NodeRobustness,
            AlgorithmSemanticIdentities.RobustnessV0,
            new StrategySelection(null, null),
            new GraphTargetIdentifiers("stress-balanced-1k", "graph-10", "target", ["path-1"]),
            parameters,
            new ExecutionOutcome(ExecutionStatus.Succeeded),
            VisualizationAdmission.Blocked,
            JsonSerializer.SerializeToElement(new { leastRobustNodeId = "node-1" }),
            JsonSerializer.SerializeToElement(new { minimum = 0.9m, maximum = 1m }),
            1,
            [item],
            CanonicalJson.ComputeSha256(new[] { item }),
            null,
            [new OrderedPathProjection(["leaf", "node-1"], ["edge-1"], 0.5m)],
            ["Projection was blocked without truncating the result."]);
        var export = new VersionedRunExport(
            VersionedRunExport.CurrentSchemaIdentity,
            VersionedRunExport.CurrentSchemaVersion,
            manifest,
            [sample],
            [output],
            new RunExportDigests("sha256:manifest", "sha256:samples", "sha256:outputs"));

        var canonical = CanonicalJson.Canonicalize(export);
        using var document = JsonDocument.Parse(canonical);
        var root = document.RootElement;

        Assert.AreEqual("insights-run-export-v1", root.GetProperty("schemaIdentity").GetString());
        Assert.AreEqual(1, root.GetProperty("schemaVersion").GetInt32());
        Assert.AreEqual(JsonValueKind.Null, root.GetProperty("manifest").GetProperty("completedAt").ValueKind);
        Assert.AreEqual("graph-10", root.GetProperty("manifest").GetProperty("graph").GetProperty("graphId").GetString());
        Assert.AreEqual("Release", root.GetProperty("manifest").GetProperty("buildConfiguration").GetString());
        Assert.AreEqual("release", root.GetProperty("manifest").GetProperty("buildMode").GetString());
        Assert.AreEqual(1, root.GetProperty("samples").GetArrayLength());
        Assert.AreEqual("measured",
            root.GetProperty("samples")[0].GetProperty("classification").GetProperty("iterationKind").GetString());
        Assert.AreEqual(2m,
            root.GetProperty("samples")[0].GetProperty("transport").GetProperty("timeToFirstByte").GetDecimal());
        Assert.AreEqual(3m,
            root.GetProperty("samples")[0].GetProperty("transport").GetProperty("fullTransferDuration").GetDecimal());
        Assert.AreEqual(1, root.GetProperty("outputs").GetArrayLength());
        Assert.AreEqual("robustness-v0",
            root.GetProperty("outputs")[0].GetProperty("algorithmSemanticIdentity").GetString());
        Assert.AreEqual("blocked",
            root.GetProperty("outputs")[0].GetProperty("visualizationAdmission").GetString());
        Assert.AreEqual(1, root.GetProperty("outputs")[0].GetProperty("warnings").GetArrayLength());
        Assert.AreEqual(JsonValueKind.Null,
            root.GetProperty("outputs")[0].GetProperty("fullResultArtifactReference").ValueKind);
        Assert.AreEqual("sha256:outputs", root.GetProperty("digests").GetProperty("outputsDigest").GetString());
    }

    private static OperationResultEnvelope CreateEnvelope(IReadOnlyList<JsonElement> items, long cardinality)
    {
        var parametersValue = JsonSerializer.SerializeToElement(new { });
        return new OperationResultEnvelope(
            Guid.NewGuid(),
            Guid.NewGuid(),
            OperationKeys.GraphCatalog,
            AlgorithmSemanticIdentities.GraphCatalogV1,
            new StrategySelection(null, null),
            new GraphTargetIdentifiers("all", null, null, []),
            new CanonicalParameters(parametersValue, CanonicalJson.ComputeSha256(parametersValue)),
            new ExecutionOutcome(ExecutionStatus.Succeeded),
            VisualizationAdmission.NotRequested,
            new Dictionary<string, JsonElement>(),
            cardinality,
            items,
            "sha256:result",
            [],
            [],
            new RuntimeResourceMeasurements(null, null, null, null, null, "ms", null),
            []);
    }

    private static FailureDetails Failure(FailureKind kind)
    {
        return new FailureDetails(kind, "test", "test failure", null, false, []);
    }

    private static MeasurementUnitContract StandardUnits()
    {
        return new MeasurementUnitContract("ms", "ms", "bytes", "bytes", "count", "ratio");
    }
}
