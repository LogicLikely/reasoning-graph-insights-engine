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
    public void CanonicalRoundTrip_PreservesExplicitNullCountersAndEstimatedProvenance()
    {
        var sample = Sample(new ExecutionOutcome(ExecutionStatus.Succeeded)) with
        {
            TimingBoundaryProvenance = TimingBoundaryProvenance.Estimated,
            OperationCounters = null
        };
        var service = new RunExportService();
        var export = service.Create(
            Manifest(new ExecutionOutcome(ExecutionStatus.Succeeded)),
            [sample],
            [Output()]);

        var json = service.Serialize(export);
        StringAssert.Contains(json, "\"timingBoundaryProvenance\":\"estimated\"");
        StringAssert.Contains(json, "\"operationCounters\":null");

        var restored = service.DeserializeAndValidate(json);
        Assert.AreEqual(TimingBoundaryProvenance.Estimated, restored.Samples[0].TimingBoundaryProvenance);
        Assert.IsNull(restored.Samples[0].OperationCounters);
    }

    [TestMethod]
    public void CheckedInPreBaselineV1Example_StrictlyValidatesWithoutChangingDigests()
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

    [TestMethod]
    public void DeserializeAndValidate_ImportsGoal1V1ManifestWithConservativeDefaults()
    {
        var service = new RunExportService();
        var current = service.Create(
            Manifest(new ExecutionOutcome(ExecutionStatus.Succeeded)),
            [Sample(new ExecutionOutcome(ExecutionStatus.Succeeded))],
            [Output()]);
        var root = JsonNode.Parse(service.Serialize(current))!.AsObject();
        var manifest = root["manifest"]!.AsObject();
        manifest.Remove("profileKey");
        manifest["samplingPolicy"]!.AsObject().Remove("sampleMode");
        using var legacyManifestDocument = JsonDocument.Parse(manifest.ToJsonString());
        var legacyManifestDigest = CanonicalJson.ComputeSha256(legacyManifestDocument.RootElement);
        root["digests"]!.AsObject()["manifestDigest"] = legacyManifestDigest;

        var restored = service.DeserializeAndValidate(root.ToJsonString());

        Assert.AreEqual(RunProfileKeys.LegacyUnspecified, restored.Manifest.ProfileKey);
        Assert.AreEqual(
            RunSampleModeTokens.LegacyUnspecified,
            restored.Manifest.SamplingPolicy.SampleMode);
        Assert.AreNotEqual(legacyManifestDigest, restored.Digests.ManifestDigest);
        Assert.AreEqual(
            CanonicalJson.ComputeSha256(restored.Manifest),
            restored.Digests.ManifestDigest);
        var normalized = service.Serialize(restored);
        StringAssert.Contains(normalized, "\"profileKey\":\"legacy-unspecified\"");
        StringAssert.Contains(normalized, "\"sampleMode\":\"legacy-unspecified\"");
    }

    [TestMethod]
    public void DeserializeAndValidate_RejectsCorruptGoal1V1ManifestBeforeDefaulting()
    {
        var service = new RunExportService();
        var current = service.Create(
            Manifest(new ExecutionOutcome(ExecutionStatus.Succeeded)),
            [Sample(new ExecutionOutcome(ExecutionStatus.Succeeded))],
            [Output()]);
        var root = JsonNode.Parse(service.Serialize(current))!.AsObject();
        var manifest = root["manifest"]!.AsObject();
        manifest.Remove("profileKey");
        manifest["samplingPolicy"]!.AsObject().Remove("sampleMode");

        var exception = Assert.ThrowsException<RunExportValidationException>(() =>
            service.DeserializeAndValidate(root.ToJsonString()));

        Assert.IsTrue(exception.Issues.Any(issue =>
            issue.Path == "$.digests.manifestDigest" && issue.Code == "digest-mismatch"));
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
    public void RoundTrip_PreservesCompletedServerPhasesAndTransportBytesOnTerminalFailure()
    {
        var failure = Outcome("timed-out");
        var completedServerPhase = Sample(new ExecutionOutcome(ExecutionStatus.Succeeded));
        var terminalTransportPhase = Sample(failure) with
        {
            Layer = InsightMeasurementLayers.Transport,
            Phase = InsightMeasurementPhases.FullTransfer,
            WallClockDuration = 30_000m,
            Transport = new SampleTransportMeasurements(320, 4_096, 12.5m, 30_000m),
            TimingBoundaryProvenance = TimingBoundaryProvenance.ExternallyObserved
        };
        var service = new RunExportService();

        var restored = service.DeserializeAndValidate(service.Serialize(service.Create(
            Manifest(failure),
            [terminalTransportPhase, completedServerPhase],
            [])));

        Assert.AreEqual(2, restored.Samples.Count);
        Assert.AreEqual(ExecutionStatus.Succeeded, restored.Samples[0].Execution.Status);
        Assert.AreEqual(ExecutionStatus.TimedOut, restored.Samples[1].Execution.Status);
        Assert.AreEqual(FailureKind.Timeout, restored.Samples[1].Execution.Failure?.Kind);
        Assert.AreEqual(4_096L, restored.Samples[1].Transport.ResponseBytes);
        Assert.AreEqual(12.5m, restored.Samples[1].Transport.TimeToFirstByte);
        Assert.AreEqual(
            TimingBoundaryProvenance.ExternallyObserved,
            restored.Samples[1].TimingBoundaryProvenance);
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
            WallClockDuration = -1m,
            Transport = new SampleTransportMeasurements(null, -1, null, null)
        };
        var invalidManifest = Manifest(new ExecutionOutcome(ExecutionStatus.Succeeded)) with
        {
            ProfileKey = "",
            EnvironmentProfile = "",
            SamplingPolicy = Manifest(new ExecutionOutcome(ExecutionStatus.Succeeded))
                .SamplingPolicy with { SampleMode = "tepid" }
        };

        var exception = Assert.ThrowsException<RunExportValidationException>(() =>
            new RunExportService().Create(invalidManifest, [invalidSample], [Output()]));

        Assert.IsTrue(exception.Issues.Any(issue => issue.Path == "$.manifest.profileKey"));
        Assert.IsTrue(exception.Issues.Any(issue => issue.Path == "$.manifest.environmentProfile"));
        Assert.IsTrue(exception.Issues.Any(issue => issue.Path == "$.manifest.samplingPolicy.sampleMode"));
        Assert.IsTrue(exception.Issues.Any(issue => issue.Path == "$.samples[0].wallClockDuration"));
        Assert.IsTrue(exception.Issues.Any(issue => issue.Path == "$.samples[0].transport.responseBytes"));
    }

    [TestMethod]
    public void Create_PreservesNonemptyLegacyIterationLabelsAsStandaloneBuckets()
    {
        var original = Sample(new ExecutionOutcome(ExecutionStatus.Succeeded));
        var legacySample = original with
        {
            Classification = original.Classification with
            {
                IterationKind = "profiled",
                Temperature = "tepid"
            }
        };

        var export = new RunExportService().Create(
            Manifest(new ExecutionOutcome(ExecutionStatus.Succeeded)),
            [legacySample],
            [Output()]);

        Assert.AreEqual("profiled", export.Samples.Single().Classification.IterationKind);
        Assert.AreEqual("tepid", export.Samples.Single().Classification.Temperature);
    }

    [TestMethod]
    public void Create_RejectsUnknownProvenanceAndNegativeOperationCounters()
    {
        var original = Sample(new ExecutionOutcome(ExecutionStatus.Succeeded));
        var invalidSample = original with
        {
            OperationCounters = original.OperationCounters! with { CandidateCount = -1 }
        };

        var exception = Assert.ThrowsException<RunExportValidationException>(() =>
            new RunExportService().Create(
                Manifest(new ExecutionOutcome(ExecutionStatus.Succeeded)),
                [invalidSample],
                [Output()]));

        Assert.IsTrue(exception.Issues.Any(issue =>
            issue.Path == "$.samples[0].operationCounters.candidateCount" &&
            issue.Code == "minimum"));

        var validExport = ValidExport();
        var invalidProvenanceExport = new VersionedRunExport(
            validExport.SchemaIdentity,
            validExport.SchemaVersion,
            validExport.Manifest,
            [original with { TimingBoundaryProvenance = (TimingBoundaryProvenance)999 }],
            validExport.Outputs,
            validExport.Digests);
        var validation = new RunExportValidator().Validate(invalidProvenanceExport);
        Assert.IsTrue(validation.Issues.Any(issue =>
            issue.Path == "$.samples[0].timingBoundaryProvenance" && issue.Code == "enum"));
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

    [DataTestMethod]
    [DataRow("timingBoundaryProvenance")]
    [DataRow("operationCounters")]
    public void DeserializeAndValidate_RequiresExplicitSampleMeasurementEvidence(string member)
    {
        var service = new RunExportService();
        var root = JsonNode.Parse(service.Serialize(ValidExport()))!.AsObject();
        Assert.IsTrue(root["samples"]![0]!.AsObject().Remove(member));

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
            new WarmupSampleCachePolicy(
                0, 1, "none", "one", "post-jit", "warm", RunSampleModeTokens.Warm),
            new TimeoutCancellationPolicy(TimeSpan.FromSeconds(30), "cooperative then terminate", true),
            Units(),
            "quick");
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
            new IterationClassification(
                IterationClassificationTokens.Measured,
                IterationClassificationTokens.Warm,
                IterationClassificationTokens.PostJit,
                IterationClassificationTokens.WarmCache),
            new SampleNodeCounts(3, 3, 0, null),
            new SampleEdgeCounts(2, null, null),
            new SampleSearchCounts(null, null),
            1,
            new SampleTransportMeasurements(null, null, null, null),
            new RuntimeResourceMeasurements(null, null, null, null, null, "ms", null),
            outcome,
            Units(),
            TimingBoundaryProvenance.DirectlyInstrumented,
            new SampleOperationCounters(null, 3, 2, 1, null, null));
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
            JsonSerializer.SerializeToElement(new { graphCount = 1 }),
            JsonSerializer.SerializeToElement(new { }),
            totalCardinality,
            [item],
            resultDigest ?? CanonicalJson.ComputeSha256(new[] { item }),
            null,
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
            value.Summary,
            value.Distribution,
            value.TotalResultCardinality,
            value.Items,
            value.ResultDigest,
            value.FullResultArtifactReference,
            value.OrderedPaths);
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
