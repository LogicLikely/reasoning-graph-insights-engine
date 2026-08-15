using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Backend.Seeding;

public sealed record StressGraphCorpus(
    string CorpusId,
    int EntryCount,
    string Json);

public static class StressGraphCorpusLoader
{
    public const int SupportedSchemaVersion = 1;
    public const int RequiredEntryCount = 10_000;
    public const int MinimumTitleWordCount = 3;
    public const int MaximumTitleWordCount = 6;
    public const int MaximumTitleLength = 35;

    // "Objection 99999 — " consumes 18 Unicode scalars, leaving 232 for an
    // excerpt while preserving the UI's 250-character body contract.
    public const int MaximumExcerptLength = 232;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    public static async Task<StressGraphCorpus> LoadAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException(
                "Database stress corpus JSON file was not found.",
                path);
        }

        var json = await File.ReadAllTextAsync(path, cancellationToken);
        StressGraphCorpusDocument document;

        try
        {
            document = JsonSerializer.Deserialize<StressGraphCorpusDocument>(json, JsonOptions)
                ?? throw Invalid("The document must contain a JSON object.");
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException(
                "Database stress corpus JSON is malformed or does not match schema version 1.",
                exception);
        }

        Validate(document, cancellationToken);

        return new StressGraphCorpus(document.CorpusId!, document.EntryCount, json);
    }

    private static void Validate(
        StressGraphCorpusDocument document,
        CancellationToken cancellationToken)
    {
        if (document.SchemaVersion != SupportedSchemaVersion)
        {
            throw Invalid(
                $"schemaVersion must be {SupportedSchemaVersion}; received {document.SchemaVersion}.");
        }

        if (string.IsNullOrWhiteSpace(document.CorpusId))
        {
            throw Invalid("corpusId must be a non-empty string.");
        }

        if (document.EntryCount != RequiredEntryCount)
        {
            throw Invalid(
                $"entryCount must be {RequiredEntryCount}; received {document.EntryCount}.");
        }

        if (document.Entries is null)
        {
            throw Invalid("entries must be an array.");
        }

        if (document.Entries.Count != document.EntryCount)
        {
            throw Invalid(
                $"entries must contain {document.EntryCount} records; received {document.Entries.Count}.");
        }

        var titles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var excerpts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (var expectedIndex = 0; expectedIndex < document.Entries.Count; expectedIndex++)
        {
            if ((expectedIndex & 255) == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            var entry = document.Entries[expectedIndex];
            if (entry is null)
            {
                throw Invalid($"entries[{expectedIndex}] must be an object.");
            }

            if (entry.Index != expectedIndex)
            {
                throw Invalid(
                    $"entries[{expectedIndex}].index must be {expectedIndex}; received {entry.Index}.");
            }

            RequireCanonicalText(entry.Title, $"entries[{expectedIndex}].title");
            RequireCanonicalText(entry.Excerpt, $"entries[{expectedIndex}].excerpt");
            RequireCanonicalText(entry.Category, $"entries[{expectedIndex}].category");

            var titleWordCount = entry.Title!
                .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
                .Length;
            if (titleWordCount is < MinimumTitleWordCount or > MaximumTitleWordCount)
            {
                throw Invalid(
                    $"entries[{expectedIndex}].title must contain {MinimumTitleWordCount} to {MaximumTitleWordCount} words; received {titleWordCount}.");
            }

            var titleLength = entry.Title.EnumerateRunes().Count();
            if (titleLength > MaximumTitleLength)
            {
                throw Invalid(
                    $"entries[{expectedIndex}].title must contain at most {MaximumTitleLength} Unicode characters; received {titleLength}.");
            }

            var excerptLength = entry.Excerpt!.EnumerateRunes().Count();
            if (excerptLength > MaximumExcerptLength)
            {
                throw Invalid(
                    $"entries[{expectedIndex}].excerpt must contain at most {MaximumExcerptLength} Unicode characters; received {excerptLength}.");
            }

            if (!titles.Add(entry.Title!))
            {
                throw Invalid($"entries[{expectedIndex}].title must be unique.");
            }

            if (!excerpts.Add(entry.Excerpt!))
            {
                throw Invalid($"entries[{expectedIndex}].excerpt must be unique.");
            }

            if (entry.Tags is null)
            {
                throw Invalid($"entries[{expectedIndex}].tags must be an array.");
            }

            if (entry.Tags.Count == 0)
            {
                throw Invalid($"entries[{expectedIndex}].tags must contain at least one tag.");
            }

            for (var tagIndex = 0; tagIndex < entry.Tags.Count; tagIndex++)
            {
                RequireCanonicalText(entry.Tags[tagIndex], $"entries[{expectedIndex}].tags[{tagIndex}]");

                if (tagIndex > 0 && StringComparer.Ordinal.Compare(
                        entry.Tags[tagIndex - 1],
                        entry.Tags[tagIndex]) >= 0)
                {
                    throw Invalid(
                        $"entries[{expectedIndex}].tags must be unique and sorted in ordinal order.");
                }
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
    }

    private static void RequireCanonicalText(string? value, string property)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw Invalid($"{property} must be a non-empty string.");
        }

        if (!string.Equals(value, value.Trim(), StringComparison.Ordinal))
        {
            throw Invalid($"{property} must not contain leading or trailing whitespace.");
        }

        if (!value.IsNormalized(NormalizationForm.FormC))
        {
            throw Invalid($"{property} must use NFC Unicode normalization.");
        }
    }

    private static InvalidDataException Invalid(string detail) =>
        new($"Database stress corpus is invalid: {detail}");

    private sealed class StressGraphCorpusDocument
    {
        public int SchemaVersion { get; init; }
        public string? CorpusId { get; init; }
        public int EntryCount { get; init; }
        public List<StressGraphCorpusEntry?>? Entries { get; init; }
    }

    private sealed class StressGraphCorpusEntry
    {
        public int Index { get; init; }
        public string? Title { get; init; }
        public string? Excerpt { get; init; }
        public string? Category { get; init; }
        public List<string?>? Tags { get; init; }
    }
}
