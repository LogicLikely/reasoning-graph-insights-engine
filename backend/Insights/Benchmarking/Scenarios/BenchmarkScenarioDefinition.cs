using System.Text.Json;
using Backend.Insights.Contracts;

namespace Backend.Insights.Benchmarking;

public sealed record BenchmarkSkipReason(string Code, string Message);

public enum BenchmarkScenarioExecutionTarget
{
    InMemory,
    RestDatabaseLoaded,
    RestSuppliedGraph,
    Browser
}

public sealed class BenchmarkScenarioDefinition
{
    public BenchmarkScenarioDefinition(
        string key,
        string description,
        string profileKey,
        string operationKey,
        string datasetId,
        JsonElement parameters,
        string? requestedStrategy,
        bool requiresIsolation,
        BenchmarkSkipReason? skipReason = null,
        BenchmarkScenarioExecutionTarget executionTarget = BenchmarkScenarioExecutionTarget.InMemory,
        BrowserJourneyDefinition? browserJourney = null,
        bool measureQualityComparison = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentException.ThrowIfNullOrWhiteSpace(description);
        ArgumentException.ThrowIfNullOrWhiteSpace(profileKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(operationKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(datasetId);
        _ = InsightOperationRegistry.Get(operationKey);
        if (parameters.ValueKind != JsonValueKind.Object)
        {
            throw new ArgumentException("Scenario parameters must be a JSON object.", nameof(parameters));
        }

        Key = key;
        Description = description;
        ProfileKey = profileKey;
        OperationKey = operationKey;
        DatasetId = datasetId;
        Parameters = parameters.Clone();
        RequestedStrategy = requestedStrategy;
        RequiresIsolation = requiresIsolation;
        SkipReason = skipReason;
        ExecutionTarget = executionTarget;
        BrowserJourney = browserJourney?.Validate();
        if ((executionTarget == BenchmarkScenarioExecutionTarget.Browser) != (BrowserJourney is not null))
        {
            throw new ArgumentException(
                "Browser execution targets require exactly one browser journey definition.",
                nameof(browserJourney));
        }

        if (measureQualityComparison)
        {
            if (!string.Equals(operationKey, OperationKeys.CounterCriticalSet, StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    "Exact-versus-greedy quality comparison is only valid for critical-counter-v1 scenarios.",
                    nameof(measureQualityComparison));
            }

            if (!parameters.TryGetProperty("candidateLimit", out var candidateLimit) ||
                candidateLimit.ValueKind != JsonValueKind.Number ||
                !candidateLimit.TryGetInt32(out var limit) ||
                limit <= 0)
            {
                throw new ArgumentException(
                    "Quality-comparison scenarios require a positive candidateLimit so exact execution is explicitly bounded.",
                    nameof(parameters));
            }
        }

        MeasureQualityComparison = measureQualityComparison;
    }

    public string Key { get; }
    public string Description { get; }
    public string ProfileKey { get; }
    public string OperationKey { get; }
    public string DatasetId { get; }
    public JsonElement Parameters { get; }
    public string? RequestedStrategy { get; }
    public bool RequiresIsolation { get; }
    public BenchmarkSkipReason? SkipReason { get; }
    public BenchmarkScenarioExecutionTarget ExecutionTarget { get; }
    public BrowserJourneyDefinition? BrowserJourney { get; }
    public bool MeasureQualityComparison { get; }
    public bool IsSkipped => SkipReason is not null;
}
