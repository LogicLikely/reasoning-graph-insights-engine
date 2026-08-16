using System.Text.Json;
using Json.Schema;

namespace Backend.Insights.Export;

/// <summary>
/// Evaluates run exports against the checked-in Draft 2020-12 schema embedded
/// in the backend assembly. Domain and digest invariants remain the
/// responsibility of <see cref="RunExportValidator"/>.
/// </summary>
public sealed class JsonSchemaNetRunExportV1SchemaEvaluator : IRunExportSchemaEvaluator
{
    private const string SchemaResourceName =
        "Backend.Insights.Export.run-export-v1.schema.json";

    private static readonly Lazy<JsonSchema> Schema = new(LoadSchema);

    public IReadOnlyList<RunExportValidationIssue> Evaluate(JsonElement document)
    {
        var results = Schema.Value.Evaluate(document, new EvaluationOptions
        {
            OutputFormat = OutputFormat.List,
            RequireFormatValidation = true
        });

        return results.IsValid
            ? Array.Empty<RunExportValidationIssue>()
            :
            [
                new RunExportValidationIssue(
                    "$",
                    "json-schema",
                    "The document does not conform to run-export-v1.schema.json.")
            ];
    }

    private static JsonSchema LoadSchema()
    {
        using var stream = typeof(JsonSchemaNetRunExportV1SchemaEvaluator)
            .Assembly
            .GetManifestResourceStream(SchemaResourceName)
            ?? throw new InvalidOperationException(
                $"Embedded run-export schema '{SchemaResourceName}' was not found.");
        using var document = JsonDocument.Parse(stream);

        return JsonSchema.Build(document.RootElement.Clone(), new BuildOptions
        {
            Dialect = Dialect.Draft202012
        });
    }
}
