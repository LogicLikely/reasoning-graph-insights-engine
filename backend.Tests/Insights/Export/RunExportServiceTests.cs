using System.Text.Json;
using System.Text.Json.Nodes;
using Backend.Insights.Contracts;
using Backend.Insights.Export;
using Backend.Insights.Measurement;

namespace backend.Tests.Insights.Export;

[TestClass]
public sealed class RunExportServiceTests
{
    private static readonly Guid RunId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid SampleId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly DateTimeOffset StartedAt =
        DateTimeOffset.Parse("2026-08-15T18:15:00+00:00", System.Globalization.CultureInfo.InvariantCulture);

    [TestMethod]
    public void Create_NormalizesPhaseRowsAndComputesAllSectionDigests()
    {
        var manifest = Manifest(new ExecutionOutcome(ExecutionStatus.Succeeded));
        var graphLookup = Sample(
            new ExecutionOutcome(ExecutionStatus.Succeeded),
            phase: InsightMeasurementPhases.GraphLookup);
        var edgeQuery = Sample(
            new ExecutionOutcome(ExecutionStatus.Succeeded),
            phase: InsightMeasurementPhases.EdgeQuery);

        var export = new RunExportService().Create(
            manifest,
            [edgeQuery, graphLookup],
            [Output()]);

        CollectionAssert.AreEqual(
            new[] { InsightMeasurementPhases.GraphLookup, InsightMeasurementPhases.EdgeQuery },
            export.Samples.Select(sample => sample.Phase).ToArray());
        Assert.AreEqual(CanonicalJson.ComputeSha256(export.Manifest), export.Digests.ManifestDigest);
        Assert.AreEqual(CanonicalJson.ComputeSha256(export.Samples), export.Digests.SamplesDigest);
        Assert.AreEqual(CanonicalJson.ComputeSha256(export.Outputs), export.Digests.OutputsDigest);
    }

    [TestMethod]
    public void CanonicalSerializeValidateDeserializeRoundTrip_PreservesBytesAndDigests()
    {
        var service = new RunExportService();
        var original = service.Create(
            Manifest(new ExecutionOutcome(ExecutionStatus.Succeeded)),
            [Sample(new ExecutionOutcome(ExecutionStatus.Succeeded))],
            [Output()]);

        var firstJson = service.Serialize(original);
        var restored = service.DeserializeAndValidate(firstJson);
        var secondJson = service.Serialize(restored);

        Assert.AreEqual(firstJson, secondJson);
        Assert.AreEqual(original.Digests, restored.Digests);
        Assert.IsTrue(service.Validate(restored).IsValid);
        Assert.IsTrue(firstJson.Contains("\"fullResultArtifactReference\":null", StringComparison.Ordinal));
    }

    [TestMethod]
    public void FrozenPhase0Example_StrictlyValidatesWithoutChangingDigests()
    {
        var path = Path.Combine(
            AppContext.BaseDirectory,
            "Contracts",
            "InsightsLab",
            "run-export-v1.example.json");
        var service = new RunExportService();

        var export = service.DeserializeAndValidate(File.ReadAllText(path));
        var canonical = service.Serialize(export);
        var restored = service.DeserializeAndValidate(canonical);

        Assert.AreEqual(export.Digests, restored.Digests);
        Assert.AreEqual(export.Digests.ManifestDigest, CanonicalJson.ComputeSha256(export.Manifest));
        Assert.AreEqual(export.Digests.SamplesDigest, CanonicalJson.ComputeSha256(export.Samples));
        Assert.AreEqual(export.Digests.OutputsDigest, CanonicalJson.ComputeSha256(export.Outputs));
    }

    [DataTestMethod]
    [DataRow("succeeded")]
    [DataRow("failed-execution")]
    [DataRow("failed-validation")]
    [DataRow("timed-out")]
    [DataRow("cancelled")]
    [DataRow("crashed")]
    [DataRow("skipped")]
    public void Create_PreservesEveryDistinctTerminalOutcome(string outcomeName)
    {
        var outcome = Outcome(outcomeName);
        var outputs = outcome.Status == ExecutionStatus.Succeeded
            ? new[] { Output() }
            : Array.Empty<CompactRunOutput>();

        var export = new RunExportService().Create(
            Manifest(outcome),
            [Sample(outcome)],
            outputs);
        var restored = new RunExportService().DeserializeAndValidate(
            new RunExportService().Serialize(export));

        Assert.AreEqual(outcome.Status, restored.Manifest.Execution.Status);
        Assert.AreEqual(outcome.Failure?.Kind, restored.Manifest.Execution.Failure?.Kind);
        if (outcomeName == "failed-validation")
        {
            Assert.AreEqual(1, restored.Manifest.Execution.Failure?.ValidationFailures.Count);
        }
    }

    [TestMethod]
    public void Validate_RejectsCorruptSectionDigest()
    {
        var service = new RunExportService();
        var valid = ValidExport();
        var corrupt = new VersionedRunExport(
            valid.SchemaIdentity,
            valid.SchemaVersion,
            valid.Manifest,
            valid.Samples,
            valid.Outputs,
            valid.Digests with { SamplesDigest = Digest('f') });

        var result = service.Validate(corrupt);

        Assert.IsFalse(result.IsValid);
        Assert.IsTrue(result.Issues.Any(issue =>
            issue.Path == "$.digests.samplesDigest" && issue.Code == "digest-mismatch"));
    }

    [TestMethod]
    public void Validate_RejectsCrossRunAndUnknownSampleCorrelationEvenWithRecomputedDigests()
    {
        var valid = ValidExport();
        var wrongSample = valid.Samples[0] with
        {
            RunId = Guid.Parse("33333333-3333-3333-3333-333333333333")
        };
        var wrongOutput = CopyOutput(
            valid.Outputs[0],
            sampleId: Guid.Parse("44444444-4444-4444-4444-444444444444"));
        var samples = new[] { wrongSample };
        var outputs = new[] { wrongOutput };
        var corrupt = new VersionedRunExport(
            valid.SchemaIdentity,
            valid.SchemaVersion,
            valid.Manifest,
            samples,
            outputs,
            new RunExportDigests(
                CanonicalJson.ComputeSha256(valid.Manifest),
                CanonicalJson.ComputeSha256(samples),
                CanonicalJson.ComputeSha256(outputs)));

        var result = new RunExportService().Validate(corrupt);

        Assert.IsFalse(result.IsValid);
        Assert.IsTrue(result.Issues.Any(issue => issue.Path == "$.samples[0].runId" && issue.Code == "correlation"));
        Assert.IsTrue(result.Issues.Any(issue => issue.Path == "$.outputs[0].sampleId" && issue.Code == "correlation"));
    }

    [TestMethod]
    public void Create_RejectsNegativeMeasurementsAndInvalidIdentityMetadata()
    {
        var invalidSample = Sample(new ExecutionOutcome(ExecutionStatus.Succeeded)) with
        {
            WallClockDuration = -1m
        };
        var invalidManifest = Manifest(new ExecutionOutcome(ExecutionStatus.Succeeded)) with
        {
            EnvironmentProfile = ""
        };

        var exception = Assert.ThrowsException<RunExportValidationException>(() =>
            new RunExportService().Create(invalidManifest, [invalidSample], [Output()]));

        Assert.IsTrue(exception.Issues.Any(issue => issue.Path == "$.manifest.environmentProfile"));
        Assert.IsTrue(exception.Issues.Any(issue => issue.Path == "$.samples[0].wallClockDuration"));
    }

    [TestMethod]
    public void Create_RejectsCanonicalParameterDigestMismatch()
    {
        var manifest = Manifest(new ExecutionOutcome(ExecutionStatus.Succeeded)) with
        {
            CanonicalParameters = Parameters(Digest('a'))
        };

        var exception = Assert.ThrowsException<RunExportValidationException>(() =>
            new RunExportService().Create(
                manifest,
                [Sample(new ExecutionOutcome(ExecutionStatus.Succeeded))],
                [Output()]));

        Assert.IsTrue(exception.Issues.Any(issue =>
            issue.Path == "$.manifest.canonicalParameters.digest" && issue.Code == "digest-mismatch"));
    }

    [TestMethod]
    public void ResultDigest_IsCheckedOnlyWhenAllLogicalItemsAreRetained()
    {
        var badDigest = Digest('a');
        var truncated = Output(totalCardinality: 2, resultDigest: badDigest);
        var truncatedExport = new RunExportService().Create(
            Manifest(new ExecutionOutcome(ExecutionStatus.Succeeded)),
            [Sample(new ExecutionOutcome(ExecutionStatus.Succeeded))],
            [truncated]);
        Assert.AreEqual(badDigest, truncatedExport.Outputs[0].ResultDigest);

        var complete = Output(totalCardinality: 1, resultDigest: badDigest);
        var exception = Assert.ThrowsException<RunExportValidationException>(() =>
            new RunExportService().Create(
                Manifest(new ExecutionOutcome(ExecutionStatus.Succeeded)),
                [Sample(new ExecutionOutcome(ExecutionStatus.Succeeded))],
                [complete]));
        Assert.IsTrue(exception.Issues.Any(issue => issue.Path == "$.outputs[0].resultDigest"));
    }

    [TestMethod]
    public void DeserializeAndValidate_RejectsZTimestampAndUnknownMembers()
    {
        var service = new RunExportService();
        var json = service.Serialize(ValidExport());
        var withZ = json.Replace(
            "2026-08-15T18:15:01+00:00",
            "2026-08-15T18:15:01Z",
            StringComparison.Ordinal);

        var timestampException = Assert.ThrowsException<RunExportValidationException>(() =>
            service.DeserializeAndValidate(withZ));
        Assert.IsTrue(timestampException.Issues.Any(issue =>
            issue.Path == "$.manifest.completedAt" && issue.Code == "format"));

        var withUnknown = json.Insert(json.Length - 1, ",\"unexpected\":true");
        var unknownException = Assert.ThrowsException<RunExportValidationException>(() =>
            service.DeserializeAndValidate(withUnknown));
        Assert.IsTrue(unknownException.Issues.Any(issue => issue.Code == "json-schema"));
    }

    [TestMethod]
    public void InjectedSchemaEvaluator_ParticipatesInValidation()
    {
        var service = new RunExportService(new RejectingSchemaEvaluator());

        var result = service.Validate(ValidExport());

        Assert.IsFalse(result.IsValid);
        Assert.AreEqual("external-schema", result.Issues.Single().Code);
    }

    [TestMethod]
    public void DeserializeAndValidate_RejectsMissingRequiredValueBeforeClrDefaultsCanFillIt()
    {
        var service = new RunExportService();
        var root = JsonNode.Parse(service.Serialize(ValidExport()))!.AsObject();
        var sourceRevision = root["manifest"]!["sourceRevision"]!.AsObject();
        Assert.IsTrue(sourceRevision.Remove("dirtyWorktree"));

        var exception = Assert.ThrowsException<RunExportValidationException>(() =>
            service.DeserializeAndValidate(root.ToJsonString()));

        Assert.IsTrue(exception.Issues.Any(issue => issue.Code == "json-schema"));
    }

    [TestMethod]
    public void DeserializeAndValidate_PassesTheUnnormalizedSourceToInjectedSchemaEvaluation()
    {
        var fixture = new RunExportService().Serialize(ValidExport());
        var root = JsonNode.Parse(fixture)!.AsObject();
        Assert.IsTrue(root["manifest"]!["sourceRevision"]!.AsObject().Remove("dirtyWorktree"));
        var evaluator = new RequiredMemberRecordingSchemaEvaluator();
        var service = new RunExportService(evaluator);

        _ = Assert.ThrowsException<RunExportValidationException>(() =>
            service.DeserializeAndValidate(root.ToJsonString()));

        Assert.IsTrue(evaluator.SawMissingDirtyWorktree);
    }

    [TestMethod]
    public void JsonSchemaEvaluator_EnforcesDraftDateTimeFormatBeyondTheLexicalPattern()
    {
        var service = new RunExportService();
        var root = JsonNode.Parse(service.Serialize(ValidExport()))!.AsObject();
        root["manifest"]!["startedAt"] = "2026-02-30T18:15:00+00:00";
        using var document = JsonDocument.Parse(root.ToJsonString());

        var issues = new JsonSchemaNetRunExportV1SchemaEvaluator().Evaluate(document.RootElement);

        Assert.IsTrue(issues.Any(issue => issue.Code == "json-schema"));
    }

    private static VersionedRunExport ValidExport()
    {
        return new RunExportService().Create(
            Manifest(new ExecutionOutcome(ExecutionStatus.Succeeded)),
            [Sample(new ExecutionOutcome(ExecutionStatus.Succeeded))],
            [Output()]);
    }

    private static RunManifest Manifest(ExecutionOutcome outcome)
    {
        return new RunManifest(
            RunId,
            "phase-1-fixture",
            outcome,
            StartedAt,
            IsTerminal(outcome.Status) ? StartedAt.AddSeconds(1) : null,
            RunnerType.CommandLine,
            "fixture.graph-catalog",
            OperationKeys.GraphCatalog,
            new GraphRunIdentity("fixture-graph", "fixture-1", "fixture", 3, 2, 2),
            new DatasetRunIdentity(
                "fixture-generator-v1",
                "fixture-corpus",
                Digest('1'),
                Digest('2'),
                Digest('3'),
                Digest('4')),
            new AlgorithmRunIdentity(OperationKeys.GraphCatalog, AlgorithmSemanticIdentities.GraphCatalogV1),
            new StrategySelection(null, null),
            Parameters(),
            new RunTargets([], []),
            new SourceRevision("0123456789abcdef", false),
            "Release",
            "release",
            new DependencyVersions(
                "8.0.0",
                "24.0.0",
                "chromium",
                "0.2.0",
                "16.0",
                new Dictionary<string, string> { ["Npgsql"] = "8.0.6" }),
            new HostEnvironment("macOS", "arm64", "fixture-cpu", 8, 16_000_000_000),
            "fixture-environment",
            new WarmupSampleCachePolicy(0, 1, "none", "one", "post-jit", "warm"),
            new TimeoutCancellationPolicy(TimeSpan.FromSeconds(30), "cooperative then terminate", true),
            Units());
    }

    private static RunSample Sample(ExecutionOutcome outcome, string? phase = null)
    {
        return new RunSample(
            RunId,
            SampleId,
            "fixture.graph-catalog",
            OperationKeys.GraphCatalog,
            InsightMeasurementLayers.PostgreSqlRepository,
            phase ?? InsightMeasurementPhases.GraphLookup,
            1.25m,
            0,
            new IterationClassification("measured", "warm", "post-jit", "warm-cache"),
            new SampleNodeCounts(3, 3, 0, null),
            new SampleEdgeCounts(2, null, null),
            new SampleSearchCounts(null, null),
            1,
            new SampleTransportMeasurements(null, null, null, null),
            new RuntimeResourceMeasurements(null, null, null, null, null, "ms", null),
            outcome,
            VisualizationAdmission.NotRequested,
            [],
            Units());
    }

    private static CompactRunOutput Output(long totalCardinality = 1, string? resultDigest = null)
    {
        var item = JsonSerializer.SerializeToElement(new { graphCount = 1 });
        return new CompactRunOutput(
            RunId,
            SampleId,
            "fixture.graph-catalog",
            OperationKeys.GraphCatalog,
            AlgorithmSemanticIdentities.GraphCatalogV1,
            new StrategySelection(null, null),
            new GraphTargetIdentifiers("fixture-graph", "fixture-1", null, []),
            Parameters(),
            new ExecutionOutcome(ExecutionStatus.Succeeded),
            VisualizationAdmission.NotRequested,
            JsonSerializer.SerializeToElement(new { graphCount = 1 }),
            JsonSerializer.SerializeToElement(new { }),
            totalCardinality,
            [item],
            resultDigest ?? CanonicalJson.ComputeSha256(new[] { item }),
            null,
            [],
            []);
    }

    private static CompactRunOutput CopyOutput(CompactRunOutput value, Guid sampleId)
    {
        return new CompactRunOutput(
            value.RunId,
            sampleId,
            value.ScenarioKey,
            value.OperationKey,
            value.AlgorithmSemanticIdentity,
            value.Strategy,
            value.Identifiers,
            value.CanonicalParameters,
            value.Execution,
            value.VisualizationAdmission,
            value.Summary,
            value.Distribution,
            value.TotalResultCardinality,
            value.Items,
            value.ResultDigest,
            value.FullResultArtifactReference,
            value.OrderedPaths,
            value.Warnings);
    }

    private static CanonicalParameters Parameters(string? digest = null)
    {
        var value = JsonSerializer.SerializeToElement(new { });
        return new CanonicalParameters(value, digest ?? CanonicalJson.ComputeSha256(value));
    }

    private static MeasurementUnitContract Units() =>
        new("ms", "ms", "bytes", "bytes", "count", "ratio");

    private static ExecutionOutcome Outcome(string name) => name switch
    {
        "succeeded" => new ExecutionOutcome(ExecutionStatus.Succeeded),
        "failed-execution" => new ExecutionOutcome(
            ExecutionStatus.Failed,
            Failure(FailureKind.Execution)),
        "failed-validation" => ExecutionOutcome.ValidationFailed(
        [
            new ValidationFailure("fixture", "invalid", "Fixture validation failed.")
        ]),
        "timed-out" => new ExecutionOutcome(ExecutionStatus.TimedOut, Failure(FailureKind.Timeout)),
        "cancelled" => new ExecutionOutcome(ExecutionStatus.Cancelled, Failure(FailureKind.Cancellation)),
        "crashed" => new ExecutionOutcome(ExecutionStatus.Crashed, Failure(FailureKind.Crash)),
        "skipped" => new ExecutionOutcome(ExecutionStatus.Skipped, Failure(FailureKind.Skip)),
        _ => throw new ArgumentOutOfRangeException(nameof(name), name, null)
    };

    private static FailureDetails Failure(FailureKind kind) =>
        new(kind, $"fixture-{kind.ToString().ToLowerInvariant()}", "Fixture failure.", null, false, []);

    private static bool IsTerminal(ExecutionStatus status) =>
        status is not (ExecutionStatus.Queued or ExecutionStatus.Running);

    private static string Digest(char value) => $"sha256:{new string(value, 64)}";

    private sealed class RejectingSchemaEvaluator : IRunExportSchemaEvaluator
    {
        public IReadOnlyList<RunExportValidationIssue> Evaluate(JsonElement document) =>
        [
            new RunExportValidationIssue("$", "external-schema", "Rejected by injected evaluator.")
        ];
    }

    private sealed class RequiredMemberRecordingSchemaEvaluator : IRunExportSchemaEvaluator
    {
        public bool SawMissingDirtyWorktree { get; private set; }

        public IReadOnlyList<RunExportValidationIssue> Evaluate(JsonElement document)
        {
            SawMissingDirtyWorktree = !document
                .GetProperty("manifest")
                .GetProperty("sourceRevision")
                .TryGetProperty("dirtyWorktree", out _);

            return SawMissingDirtyWorktree
                ? [new RunExportValidationIssue("$", "raw-schema", "Required member is missing.")]
                : Array.Empty<RunExportValidationIssue>();
        }
    }
}
