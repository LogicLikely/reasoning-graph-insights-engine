using System.Text.Json;
using Backend.Insights.Contracts;

namespace Backend.Insights.Export;

public sealed class RunExportService
{
    private static readonly IRunExportSchemaEvaluator IngressLexicalEvaluator =
        new BuiltInRunExportV1SchemaEvaluator();

    private readonly IRunExportSchemaEvaluator _schemaEvaluator;
    private readonly RunExportValidator _validator;

    public RunExportService(
        IRunExportSchemaEvaluator? schemaEvaluator = null,
        RunExportValidator? validator = null)
    {
        _schemaEvaluator = schemaEvaluator ?? new JsonSchemaNetRunExportV1SchemaEvaluator();
        _validator = validator ?? new RunExportValidator();
    }

    public VersionedRunExport Create(
        RunManifest manifest,
        IEnumerable<RunSample> samples,
        IEnumerable<CompactRunOutput> outputs)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(samples);
        ArgumentNullException.ThrowIfNull(outputs);

        var normalizedSamples = RunExportOrdering.Normalize(samples);
        var normalizedOutputs = RunExportOrdering.Normalize(outputs);
        var export = new VersionedRunExport(
            VersionedRunExport.CurrentSchemaIdentity,
            VersionedRunExport.CurrentSchemaVersion,
            manifest,
            normalizedSamples,
            normalizedOutputs,
            new RunExportDigests(
                CanonicalJson.ComputeSha256(manifest),
                CanonicalJson.ComputeSha256(normalizedSamples),
                CanonicalJson.ComputeSha256(normalizedOutputs)));

        var creationIssues = _validator.ValidateForCreation(export);
        if (creationIssues.Count > 0)
        {
            throw new RunExportValidationException(creationIssues);
        }

        ValidateOrThrow(export);
        return export;
    }

    public string Serialize(VersionedRunExport export)
    {
        ValidateOrThrow(export);
        return CanonicalJson.Canonicalize(export);
    }

    public VersionedRunExport DeserializeAndValidate(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json, new JsonDocumentOptions
            {
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow
            });
        }
        catch (JsonException exception)
        {
            throw InvalidDocument("json", exception.Message);
        }

        using (document)
        {
            try
            {
                _ = CanonicalJson.Canonicalize(document.RootElement);
            }
            catch (FormatException exception)
            {
                throw InvalidDocument("canonical-json", exception.Message);
            }

            // Preserve precise ingress diagnostics for lexical rules that typed
            // deserialization would normalize (notably an offset timestamp
            // written with Z). The authoritative full schema is evaluated by
            // ValidateOrThrow after strict typed ingestion succeeds.
            var schemaIssues = IngressLexicalEvaluator.Evaluate(document.RootElement);
            if (schemaIssues.Count > 0)
            {
                throw new RunExportValidationException(schemaIssues);
            }

            // Evaluate the source document before typed ingestion can supply
            // CLR defaults for omitted required members or normalize lexical
            // representations. Validation after reserialization is still used
            // for in-memory exports, but it cannot prove the original import
            // conformed to the checked-in schema.
            schemaIssues = _schemaEvaluator.Evaluate(document.RootElement);
            if (schemaIssues.Count > 0)
            {
                throw new RunExportValidationException(schemaIssues);
            }
        }

        VersionedRunExport export;
        try
        {
            export = JsonSerializer.Deserialize<VersionedRunExport>(
                    json,
                    CreateDeserializationOptions())
                ?? throw new JsonException("The export document deserialized to null.");
        }
        catch (Exception exception) when (exception is JsonException or ArgumentException or InvalidOperationException)
        {
            throw InvalidDocument("deserialization", exception.Message);
        }

        ValidateOrThrow(export);
        return export;
    }

    public RunExportValidationResult Validate(VersionedRunExport export)
    {
        ArgumentNullException.ThrowIfNull(export);
        var schemaIssues = EvaluateSerializedShape(export);
        if (schemaIssues.Count > 0)
        {
            return new RunExportValidationResult(schemaIssues);
        }

        return _validator.Validate(export);
    }

    public void ValidateOrThrow(VersionedRunExport export)
    {
        var result = Validate(export);
        if (!result.IsValid)
        {
            throw new RunExportValidationException(result.Issues);
        }
    }

    private IReadOnlyList<RunExportValidationIssue> EvaluateSerializedShape(VersionedRunExport export)
    {
        try
        {
            var element = JsonSerializer.SerializeToElement(export, CanonicalJson.CreateSerializerOptions());
            return _schemaEvaluator.Evaluate(element);
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException)
        {
            return new[]
            {
                new RunExportValidationIssue("$", "serialization", exception.Message)
            };
        }
    }

    private static RunExportValidationException InvalidDocument(string code, string message) =>
        new([new RunExportValidationIssue("$", code, message)]);

    private static JsonSerializerOptions CreateDeserializationOptions()
        => CanonicalJson.CreateSerializerOptions();
}
