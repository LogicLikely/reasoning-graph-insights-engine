using System.Text.Json;
using System.Text.Json.Nodes;
using Backend.Insights.Contracts;

namespace backend.Tests.Insights.Contracts;

[TestClass]
public class RunExportArtifactContractTests
{
    [TestMethod]
    public void SchemaAndExample_AreCopiedWithFrozenRootIdentityAndSections()
    {
        var schemaPath = ArtifactPath("run-export-v1.schema.json");
        var examplePath = ArtifactPath("run-export-v1.example.json");
        Assert.IsTrue(File.Exists(schemaPath), schemaPath);
        Assert.IsTrue(File.Exists(examplePath), examplePath);

        using var schema = JsonDocument.Parse(File.ReadAllText(schemaPath));
        using var example = JsonDocument.Parse(File.ReadAllText(examplePath));

        Assert.AreEqual(
            VersionedRunExport.CurrentSchemaIdentity,
            schema.RootElement.GetProperty("properties").GetProperty("schemaIdentity").GetProperty("const").GetString());
        CollectionAssert.AreEquivalent(
            new[] { "schemaIdentity", "schemaVersion", "manifest", "samples", "outputs", "digests" },
            schema.RootElement.GetProperty("required").EnumerateArray().Select(item => item.GetString()).ToArray());
        Assert.AreEqual(
            VersionedRunExport.CurrentSchemaIdentity,
            example.RootElement.GetProperty("schemaIdentity").GetString());
        Assert.AreEqual(VersionedRunExport.CurrentSchemaVersion,
            example.RootElement.GetProperty("schemaVersion").GetInt32());
        Assert.AreEqual(JsonValueKind.Object, example.RootElement.GetProperty("manifest").ValueKind);
        Assert.AreEqual(JsonValueKind.Array, example.RootElement.GetProperty("samples").ValueKind);
        Assert.AreEqual(JsonValueKind.Array, example.RootElement.GetProperty("outputs").ValueKind);
        Assert.AreEqual(JsonValueKind.Object, example.RootElement.GetProperty("digests").ValueKind);

        var digests = example.RootElement.GetProperty("digests");
        var output = example.RootElement.GetProperty("outputs")[0];
        Assert.AreEqual(
            CanonicalJson.ComputeSha256(example.RootElement.GetProperty("manifest")),
            digests.GetProperty("manifestDigest").GetString());
        Assert.AreEqual(
            CanonicalJson.ComputeSha256(example.RootElement.GetProperty("samples")),
            digests.GetProperty("samplesDigest").GetString());
        Assert.AreEqual(
            CanonicalJson.ComputeSha256(example.RootElement.GetProperty("outputs")),
            digests.GetProperty("outputsDigest").GetString());

        var manifestParameters = example.RootElement
            .GetProperty("manifest")
            .GetProperty("canonicalParameters");
        Assert.AreEqual(
            CanonicalJson.ComputeSha256(manifestParameters.GetProperty("value")),
            manifestParameters.GetProperty("digest").GetString());
        var outputParameters = output.GetProperty("canonicalParameters");
        Assert.AreEqual(
            CanonicalJson.ComputeSha256(outputParameters.GetProperty("value")),
            outputParameters.GetProperty("digest").GetString());
        Assert.AreEqual(
            CanonicalJson.ComputeSha256(output.GetProperty("items")),
            output.GetProperty("resultDigest").GetString(),
            "The example retains all three logical result items, so its full-result digest is directly checkable.");
    }

    [TestMethod]
    public void SchemaAndExample_OmitDroppedGraphMapAdmissionFields()
    {
        using var schema = JsonDocument.Parse(
            File.ReadAllText(ArtifactPath("run-export-v1.schema.json")));
        using var example = JsonDocument.Parse(
            File.ReadAllText(ArtifactPath("run-export-v1.example.json")));

        var definitions = schema.RootElement.GetProperty("$defs");
        Assert.IsFalse(definitions.TryGetProperty("visualizationAdmission", out _));

        foreach (var definitionName in new[] { "runSample", "compactRunOutput" })
        {
            var definition = definitions.GetProperty(definitionName);
            var required = definition.GetProperty("required")
                .EnumerateArray()
                .Select(value => value.GetString())
                .ToArray();
            CollectionAssert.DoesNotContain(required, "visualizationAdmission");
            CollectionAssert.DoesNotContain(required, "warnings");
            Assert.IsFalse(definition.GetProperty("properties")
                .TryGetProperty("visualizationAdmission", out _));
            Assert.IsFalse(definition.GetProperty("properties")
                .TryGetProperty("warnings", out _));
        }

        foreach (var sample in example.RootElement.GetProperty("samples").EnumerateArray())
        {
            Assert.IsFalse(sample.TryGetProperty("visualizationAdmission", out _));
            Assert.IsFalse(sample.TryGetProperty("warnings", out _));
        }

        foreach (var output in example.RootElement.GetProperty("outputs").EnumerateArray())
        {
            Assert.IsFalse(output.TryGetProperty("visualizationAdmission", out _));
            Assert.IsFalse(output.TryGetProperty("warnings", out _));
        }
    }

    [TestMethod]
    public void Schema_FreezesSampleClassificationProvenanceAndCounterEvidence()
    {
        using var schema = JsonDocument.Parse(
            File.ReadAllText(ArtifactPath("run-export-v1.schema.json")));
        var definitions = schema.RootElement.GetProperty("$defs");
        var sample = definitions.GetProperty("runSample");
        var required = sample.GetProperty("required")
            .EnumerateArray()
            .Select(value => value.GetString())
            .ToArray();

        CollectionAssert.Contains(required, "timingBoundaryProvenance");
        CollectionAssert.Contains(required, "operationCounters");
        CollectionAssert.AreEqual(
            new[] { "directly-instrumented", "externally-observed", "estimated" },
            definitions.GetProperty("timingBoundaryProvenance")
                .GetProperty("enum")
                .EnumerateArray()
                .Select(value => value.GetString())
                .ToArray());

        var classification = definitions.GetProperty("iterationClassification")
            .GetProperty("properties");
        Assert.AreEqual(
            "string",
            classification.GetProperty("iterationKind").GetProperty("type").GetString());
        Assert.AreEqual(
            1,
            classification.GetProperty("iterationKind").GetProperty("minLength").GetInt32());
        Assert.AreEqual(
            "string",
            classification.GetProperty("temperature").GetProperty("type").GetString());
        Assert.AreEqual(
            1,
            classification.GetProperty("temperature").GetProperty("minLength").GetInt32());

        CollectionAssert.AreEquivalent(
            new[]
            {
                "candidateCount",
                "visitedNodeCount",
                "visitedEdgeCount",
                "algorithmIterationCount",
                "cancellationCheckCount",
                "thresholdAttained"
            },
            definitions.GetProperty("sampleOperationCounters")
                .GetProperty("required")
                .EnumerateArray()
                .Select(value => value.GetString())
                .ToArray());
    }

    [TestMethod]
    public void Example_DeserializesIntoTypedContractWithAuditedFields()
    {
        var json = File.ReadAllText(ArtifactPath("run-export-v1.example.json"));

        var export = JsonSerializer.Deserialize<VersionedRunExport>(
            json,
            CanonicalJson.CreateSerializerOptions());

        Assert.IsNotNull(export);
        Assert.AreEqual(VersionedRunExport.CurrentSchemaIdentity, export.SchemaIdentity);
        Assert.AreEqual(VersionedRunExport.CurrentSchemaVersion, export.SchemaVersion);
        Assert.IsNull(export.Manifest.Graph.GraphId);
        Assert.IsFalse(string.IsNullOrWhiteSpace(export.Manifest.BuildConfiguration));
        Assert.IsFalse(string.IsNullOrWhiteSpace(export.Manifest.BuildMode));
        Assert.IsFalse(string.IsNullOrWhiteSpace(export.Manifest.EnvironmentProfile));
        Assert.IsTrue(export.Samples.Count > 0);
        Assert.IsFalse(string.IsNullOrWhiteSpace(export.Samples[0].Classification.IterationKind));
        Assert.AreEqual(
            TimingBoundaryProvenance.DirectlyInstrumented,
            export.Samples[0].TimingBoundaryProvenance);
        Assert.AreEqual(3L, export.Samples[0].OperationCounters?.VisitedNodeCount);
        Assert.AreEqual(2L, export.Samples[0].OperationCounters?.VisitedEdgeCount);
        Assert.IsNotNull(export.Samples[0].Transport.TimeToFirstByte);
        Assert.IsNotNull(export.Samples[0].Transport.FullTransferDuration);
        Assert.IsTrue(export.Outputs.Count > 0);
        Assert.IsFalse(string.IsNullOrWhiteSpace(export.Outputs[0].AlgorithmSemanticIdentity));
        Assert.AreEqual(ExecutionStatus.Succeeded, export.Outputs[0].Execution.Status);
        Assert.AreEqual(export.Outputs[0].Items.Count, export.Outputs[0].OrderedPaths.Count);
        Assert.IsTrue(export.Outputs[0].Items.All(item =>
            item.TryGetProperty("nodeIds", out _) && item.TryGetProperty("edgeIds", out _)));
        Assert.IsFalse(string.IsNullOrWhiteSpace(export.Digests.ManifestDigest));
        Assert.IsFalse(string.IsNullOrWhiteSpace(export.Digests.SamplesDigest));
        Assert.IsFalse(string.IsNullOrWhiteSpace(export.Digests.OutputsDigest));
        Assert.AreEqual(
            export.Digests.ManifestDigest,
            CanonicalJson.ComputeSha256(export.Manifest));
        Assert.AreEqual(
            export.Digests.SamplesDigest,
            CanonicalJson.ComputeSha256(export.Samples));
        Assert.AreEqual(
            export.Digests.OutputsDigest,
            CanonicalJson.ComputeSha256(export.Outputs));
    }

    [DataTestMethod]
    [DataRow("wrong-schema", 1)]
    [DataRow("insights-run-export-v1", 2)]
    public void Example_RejectsAnUnexpectedSchemaIdentityOrVersion(
        string schemaIdentity,
        int schemaVersion)
    {
        using var document = JsonDocument.Parse(
            File.ReadAllText(ArtifactPath("run-export-v1.example.json")));
        var root = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(
            document.RootElement.GetRawText())!;
        root["schemaIdentity"] = JsonSerializer.SerializeToElement(schemaIdentity);
        root["schemaVersion"] = JsonSerializer.SerializeToElement(schemaVersion);

        Exception? thrown = null;
        try
        {
            _ = JsonSerializer.Deserialize<VersionedRunExport>(
                JsonSerializer.Serialize(root),
                CanonicalJson.CreateSerializerOptions());
        }
        catch (Exception exception)
        {
            thrown = exception;
        }

        Assert.IsNotNull(thrown);
        Assert.IsInstanceOfType<ArgumentException>(thrown);
    }

    [TestMethod]
    public void Example_RejectsUnknownPropertiesDuringTypedContractIngestion()
    {
        using var document = JsonDocument.Parse(
            File.ReadAllText(ArtifactPath("run-export-v1.example.json")));
        var root = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(
            document.RootElement.GetRawText())!;
        root["unexpected"] = JsonSerializer.SerializeToElement(true);

        Assert.ThrowsException<JsonException>(() =>
            JsonSerializer.Deserialize<VersionedRunExport>(
                JsonSerializer.Serialize(root),
                CanonicalJson.CreateSerializerOptions()));
    }

    [TestMethod]
    public void Example_RejectsAnInvalidSucceededManifestStrategy()
    {
        var root = JsonNode.Parse(
            File.ReadAllText(ArtifactPath("run-export-v1.example.json")))!.AsObject();
        var strategy = root["manifest"]!["strategy"]!.AsObject();
        strategy["requested"] = OperationStrategyNames.Maximum;
        strategy["used"] = OperationStrategyNames.Maximum;

        Assert.ThrowsException<ArgumentException>(() =>
            JsonSerializer.Deserialize<VersionedRunExport>(
                root.ToJsonString(),
                CanonicalJson.CreateSerializerOptions()));
    }

    private static string ArtifactPath(string fileName)
    {
        return Path.Combine(AppContext.BaseDirectory, "Contracts", "InsightsLab", fileName);
    }
}
