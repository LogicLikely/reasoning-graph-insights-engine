using System.Collections.ObjectModel;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Backend.Insights.Export;

public sealed record RunExportValidationIssue(string Path, string Code, string Message);

public sealed record RunExportValidationResult(IReadOnlyList<RunExportValidationIssue> Issues)
{
    public bool IsValid => Issues.Count == 0;

    public static RunExportValidationResult Valid { get; } =
        new(Array.Empty<RunExportValidationIssue>());
}

public sealed class RunExportValidationException : Exception
{
    public RunExportValidationException(IEnumerable<RunExportValidationIssue> issues)
        : base(CreateMessage(issues, out var frozen))
    {
        Issues = frozen;
    }

    public IReadOnlyList<RunExportValidationIssue> Issues { get; }

    private static string CreateMessage(
        IEnumerable<RunExportValidationIssue> issues,
        out IReadOnlyList<RunExportValidationIssue> frozen)
    {
        ArgumentNullException.ThrowIfNull(issues);
        var values = issues.ToArray();
        if (values.Length == 0)
        {
            throw new ArgumentException("At least one export validation issue is required.", nameof(issues));
        }

        frozen = new ReadOnlyCollection<RunExportValidationIssue>(values);
        return $"Insights run export validation failed with {values.Length} issue(s): " +
               string.Join("; ", values.Select(issue => $"{issue.Path}: {issue.Message}"));
    }
}

/// <summary>
/// Injection seam for JSON Schema evaluation. Production uses the checked-in
/// Draft 2020-12 schema; the built-in evaluator preserves precise diagnostics
/// for lexical constraints before typed ingestion.
/// </summary>
public interface IRunExportSchemaEvaluator
{
    IReadOnlyList<RunExportValidationIssue> Evaluate(JsonElement document);
}

public sealed partial class BuiltInRunExportV1SchemaEvaluator : IRunExportSchemaEvaluator
{
    private static readonly string[] RequiredRootMembers =
    [
        "schemaIdentity",
        "schemaVersion",
        "manifest",
        "samples",
        "outputs",
        "digests"
    ];

    public IReadOnlyList<RunExportValidationIssue> Evaluate(JsonElement document)
    {
        var issues = new List<RunExportValidationIssue>();
        if (document.ValueKind != JsonValueKind.Object)
        {
            issues.Add(Issue("$", "type", "The export root must be an object."));
            return issues.AsReadOnly();
        }

        foreach (var member in RequiredRootMembers)
        {
            if (!document.TryGetProperty(member, out _))
            {
                issues.Add(Issue($"$.{member}", "required", $"Required member '{member}' is missing."));
            }
        }

        if (document.TryGetProperty("schemaIdentity", out var schemaIdentity) &&
            (schemaIdentity.ValueKind != JsonValueKind.String ||
             !string.Equals(schemaIdentity.GetString(), "insights-run-export-v1", StringComparison.Ordinal)))
        {
            issues.Add(Issue("$.schemaIdentity", "const", "Schema identity must be 'insights-run-export-v1'."));
        }

        if (document.TryGetProperty("schemaVersion", out var schemaVersion) &&
            (schemaVersion.ValueKind != JsonValueKind.Number ||
             !schemaVersion.TryGetInt32(out var version) ||
             version != 1))
        {
            issues.Add(Issue("$.schemaVersion", "const", "Schema version must be 1."));
        }

        if (document.TryGetProperty("manifest", out var manifest))
        {
            ValidateManifestLexicalValues(manifest, issues);
        }

        ValidateArrayCorrelationValues(document, "samples", issues);
        ValidateArrayCorrelationValues(document, "outputs", issues);

        return issues.AsReadOnly();
    }

    private static void ValidateManifestLexicalValues(
        JsonElement manifest,
        ICollection<RunExportValidationIssue> issues)
    {
        if (manifest.ValueKind != JsonValueKind.Object)
        {
            issues.Add(Issue("$.manifest", "type", "Manifest must be an object."));
            return;
        }

        ValidateUuid(manifest, "runId", "$.manifest.runId", issues);
        ValidateOffsetDateTime(manifest, "startedAt", "$.manifest.startedAt", allowNull: false, issues);
        ValidateOffsetDateTime(manifest, "completedAt", "$.manifest.completedAt", allowNull: true, issues);

        if (manifest.TryGetProperty("executionPolicy", out var policy) &&
            policy.ValueKind == JsonValueKind.Object &&
            policy.TryGetProperty("timeout", out var timeout) &&
            (timeout.ValueKind != JsonValueKind.String ||
             !TimeoutPattern().IsMatch(timeout.GetString() ?? string.Empty)))
        {
            issues.Add(Issue(
                "$.manifest.executionPolicy.timeout",
                "format",
                "Timeout must use the frozen invariant TimeSpan representation."));
        }
    }

    private static void ValidateArrayCorrelationValues(
        JsonElement root,
        string propertyName,
        ICollection<RunExportValidationIssue> issues)
    {
        if (!root.TryGetProperty(propertyName, out var values))
        {
            return;
        }

        if (values.ValueKind != JsonValueKind.Array)
        {
            issues.Add(Issue($"$.{propertyName}", "type", $"'{propertyName}' must be an array."));
            return;
        }

        var index = 0;
        foreach (var value in values.EnumerateArray())
        {
            if (value.ValueKind != JsonValueKind.Object)
            {
                issues.Add(Issue($"$.{propertyName}[{index}]", "type", "Array item must be an object."));
            }
            else
            {
                ValidateUuid(value, "runId", $"$.{propertyName}[{index}].runId", issues);
                ValidateUuid(value, "sampleId", $"$.{propertyName}[{index}].sampleId", issues);
            }

            index++;
        }
    }

    private static void ValidateUuid(
        JsonElement container,
        string propertyName,
        string path,
        ICollection<RunExportValidationIssue> issues)
    {
        if (!container.TryGetProperty(propertyName, out var value) ||
            value.ValueKind != JsonValueKind.String ||
            !Guid.TryParseExact(value.GetString(), "D", out var parsed) ||
            parsed == Guid.Empty)
        {
            issues.Add(Issue(path, "format", "Value must be a non-empty hyphenated UUID."));
        }
    }

    private static void ValidateOffsetDateTime(
        JsonElement container,
        string propertyName,
        string path,
        bool allowNull,
        ICollection<RunExportValidationIssue> issues)
    {
        if (!container.TryGetProperty(propertyName, out var value))
        {
            issues.Add(Issue(path, "required", $"Required member '{propertyName}' is missing."));
            return;
        }

        if (allowNull && value.ValueKind == JsonValueKind.Null)
        {
            return;
        }

        if (value.ValueKind != JsonValueKind.String ||
            !OffsetDateTimePattern().IsMatch(value.GetString() ?? string.Empty))
        {
            issues.Add(Issue(
                path,
                "format",
                "Date-time must include seconds and an explicit numeric offset; UTC 'Z' is not permitted."));
        }
    }

    private static RunExportValidationIssue Issue(string path, string code, string message) =>
        new(path, code, message);

    [GeneratedRegex(
        "^[0-9]{4}-[0-9]{2}-[0-9]{2}T[0-9]{2}:[0-9]{2}:[0-9]{2}(?:\\.[0-9]{1,7})?[+-][0-9]{2}:[0-9]{2}$",
        RegexOptions.CultureInvariant)]
    private static partial Regex OffsetDateTimePattern();

    [GeneratedRegex(
        "^-?(?:[0-9]+\\.)?[0-9]{2}:[0-9]{2}:[0-9]{2}(?:\\.[0-9]{1,7})?$",
        RegexOptions.CultureInvariant)]
    private static partial Regex TimeoutPattern();
}
