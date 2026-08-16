using System.Text.Json;
using Backend.Insights.Contracts;

namespace Backend.Insights.Benchmarking;

public sealed record BenchmarkSkipReason(string Code, string Message);

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
        BenchmarkSkipReason? skipReason = null)
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
    public bool IsSkipped => SkipReason is not null;
}
