using Backend.Insights.Contracts;
using Microsoft.AspNetCore.Http;

namespace Backend.Insights.Measurement;

public static class InsightCorrelationHeaders
{
    public const string RunId = "X-Insights-Run-Id";
    public const string SampleId = "X-Insights-Sample-Id";
}

public sealed record InsightCorrelationContext(Guid RunId, Guid SampleId)
{
    public static InsightCorrelationValidationResult FromHeaders(IHeaderDictionary headers)
    {
        ArgumentNullException.ThrowIfNull(headers);

        var hasRunId = headers.TryGetValue(InsightCorrelationHeaders.RunId, out var rawRunIds);
        var hasSampleId = headers.TryGetValue(InsightCorrelationHeaders.SampleId, out var rawSampleIds);

        if (!hasRunId && !hasSampleId)
        {
            return InsightCorrelationValidationResult.Ambient;
        }

        var failures = new List<ValidationFailure>();
        if (!hasRunId)
        {
            failures.Add(Missing(InsightCorrelationHeaders.RunId));
        }

        if (!hasSampleId)
        {
            failures.Add(Missing(InsightCorrelationHeaders.SampleId));
        }

        Guid runId = default;
        Guid sampleId = default;
        if (hasRunId && !TryParseSingle(rawRunIds, out runId))
        {
            failures.Add(Invalid(InsightCorrelationHeaders.RunId));
        }

        if (hasSampleId && !TryParseSingle(rawSampleIds, out sampleId))
        {
            failures.Add(Invalid(InsightCorrelationHeaders.SampleId));
        }

        return failures.Count == 0
            ? InsightCorrelationValidationResult.Correlated(new InsightCorrelationContext(runId, sampleId))
            : InsightCorrelationValidationResult.Invalid(failures);
    }

    private static bool TryParseSingle(
        Microsoft.Extensions.Primitives.StringValues values,
        out Guid value)
    {
        value = default;
        if (values.Count != 1)
        {
            return false;
        }

        var text = values[0];
        return text is not null &&
               text.Length == 36 &&
               string.Equals(text, text.Trim(), StringComparison.Ordinal) &&
               Guid.TryParseExact(text, "D", out value) &&
               value != Guid.Empty;
    }

    private static ValidationFailure Missing(string header) => new(
        header,
        "missing-correlation-header",
        $"Header '{header}' is required when either Insights correlation header is supplied.");

    private static ValidationFailure Invalid(string header) => new(
        header,
        "invalid-correlation-header",
        $"Header '{header}' must contain exactly one non-empty UUID in canonical hyphenated form.");
}

public sealed record InsightCorrelationValidationResult
{
    private InsightCorrelationValidationResult(
        InsightCorrelationContext? context,
        IReadOnlyList<ValidationFailure> failures)
    {
        Context = context;
        Failures = failures;
    }

    public InsightCorrelationContext? Context { get; }

    public IReadOnlyList<ValidationFailure> Failures { get; }

    public bool IsAmbient => Context is null && Failures.Count == 0;

    public bool IsValid => Failures.Count == 0;

    public static InsightCorrelationValidationResult Ambient { get; } =
        new(null, Array.Empty<ValidationFailure>());

    public static InsightCorrelationValidationResult Correlated(InsightCorrelationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return new InsightCorrelationValidationResult(context, Array.Empty<ValidationFailure>());
    }

    public static InsightCorrelationValidationResult Invalid(IEnumerable<ValidationFailure> failures)
    {
        ArgumentNullException.ThrowIfNull(failures);
        var frozen = Array.AsReadOnly(failures.ToArray());
        if (frozen.Count == 0)
        {
            throw new ArgumentException("At least one validation failure is required.", nameof(failures));
        }

        return new InsightCorrelationValidationResult(null, frozen);
    }
}

public interface IInsightCorrelationAccessor
{
    InsightCorrelationContext? Current { get; set; }
}

/// <summary>
/// Register as scoped. The middleware restores the prior value after every request.
/// </summary>
public sealed class InsightCorrelationAccessor : IInsightCorrelationAccessor
{
    public InsightCorrelationContext? Current { get; set; }
}
