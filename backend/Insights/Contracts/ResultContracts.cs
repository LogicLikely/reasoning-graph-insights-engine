using System.Collections.ObjectModel;
using System.Text.Json;

namespace Backend.Insights.Contracts;

public enum ExecutionStatus
{
    Queued,
    Running,
    Succeeded,
    Failed,
    TimedOut,
    Cancelled,
    Crashed,
    Skipped
}

public enum FailureKind
{
    Validation,
    Execution,
    Timeout,
    Cancellation,
    Crash,
    Skip
}

public sealed record ValidationFailure(string Field, string Code, string Message);

public sealed record FailureDetails(
    FailureKind Kind,
    string Code,
    string Message,
    string? ExceptionType,
    bool Retryable,
    IReadOnlyList<ValidationFailure> ValidationFailures)
{
    public static FailureDetails Validation(
        IEnumerable<ValidationFailure> failures,
        string code = "validation-failed",
        string message = "One or more request values failed validation.")
    {
        ArgumentNullException.ThrowIfNull(failures);
        var frozenFailures = Array.AsReadOnly(failures.ToArray());
        if (frozenFailures.Count == 0)
        {
            throw new ArgumentException("At least one validation failure is required.", nameof(failures));
        }

        return new FailureDetails(FailureKind.Validation, code, message, null, false, frozenFailures);
    }
}

public sealed record ExecutionOutcome
{
    public ExecutionOutcome(ExecutionStatus status, FailureDetails? failure = null)
    {
        ValidateStatusAndFailure(status, failure);
        Status = status;
        Failure = failure;
    }

    public ExecutionStatus Status { get; }

    public FailureDetails? Failure { get; }

    public static ExecutionOutcome ValidationFailed(IEnumerable<ValidationFailure> failures)
    {
        return new ExecutionOutcome(ExecutionStatus.Failed, FailureDetails.Validation(failures));
    }

    private static void ValidateStatusAndFailure(ExecutionStatus status, FailureDetails? failure)
    {
        if (!Enum.IsDefined(status))
        {
            throw new ArgumentOutOfRangeException(nameof(status), status, "Unknown execution status.");
        }

        var requiredFailureKind = status switch
        {
            ExecutionStatus.Failed => (FailureKind?)null,
            ExecutionStatus.TimedOut => FailureKind.Timeout,
            ExecutionStatus.Cancelled => FailureKind.Cancellation,
            ExecutionStatus.Crashed => FailureKind.Crash,
            ExecutionStatus.Skipped => FailureKind.Skip,
            _ => null
        };

        if (status is ExecutionStatus.Queued or ExecutionStatus.Running or ExecutionStatus.Succeeded)
        {
            if (failure is not null)
            {
                throw new ArgumentException($"Execution status '{status}' cannot carry failure details.", nameof(failure));
            }

            return;
        }

        if (failure is null)
        {
            throw new ArgumentException($"Execution status '{status}' requires failure details.", nameof(failure));
        }

        if (failure.ValidationFailures is null)
        {
            throw new ArgumentException("Failure validation issues cannot be null.", nameof(failure));
        }

        if (status == ExecutionStatus.Failed && failure.Kind is not (FailureKind.Validation or FailureKind.Execution))
        {
            throw new ArgumentException(
                "Execution status 'Failed' requires failure kind 'Validation' or 'Execution'.",
                nameof(failure));
        }

        if (requiredFailureKind.HasValue && failure.Kind != requiredFailureKind.Value)
        {
            throw new ArgumentException(
                $"Execution status '{status}' requires failure kind '{requiredFailureKind.Value}'.",
                nameof(failure));
        }

        if (failure.Kind == FailureKind.Validation && failure.ValidationFailures.Count == 0)
        {
            throw new ArgumentException("Validation failure details require at least one validation issue.", nameof(failure));
        }

        if (failure.Kind != FailureKind.Validation && failure.ValidationFailures.Count != 0)
        {
            throw new ArgumentException(
                "Only failure kind 'Validation' may carry validation issues.",
                nameof(failure));
        }
    }
}

public sealed record StrategySelection(string? Requested, string? Used);

public sealed record GraphTargetIdentifiers(
    string GraphSlug,
    string? GraphId,
    string? TargetNodeId,
    IReadOnlyList<string> TargetPathIds);

public sealed record CanonicalParameters(JsonElement Value, string Digest);

public sealed record OrderedPathProjection(
    IReadOnlyList<string> NodeIds,
    IReadOnlyList<string> EdgeIds,
    decimal AccumulatedScore);

public sealed record PhaseTimingMeasurement(
    string Layer,
    string Phase,
    decimal Duration,
    string Unit);

public sealed record RuntimeResourceMeasurements(
    long? AllocatedBytes,
    int? Generation0Collections,
    int? Generation1Collections,
    int? Generation2Collections,
    decimal? CpuTime,
    string CpuTimeUnit,
    long? WorkingSetChangeBytes);

public sealed record OperationResultEnvelope
{
    public const int MaximumRetainedItems = 100;

    public OperationResultEnvelope(
        Guid runId,
        Guid sampleId,
        string operationKey,
        string algorithmSemanticIdentity,
        StrategySelection strategy,
        GraphTargetIdentifiers identifiers,
        CanonicalParameters canonicalParameters,
        ExecutionOutcome execution,
        IReadOnlyDictionary<string, JsonElement> summaryMetrics,
        long totalResultCardinality,
        IReadOnlyList<JsonElement> items,
        string? resultDigest,
        IReadOnlyList<OrderedPathProjection> orderedPaths,
        IReadOnlyList<PhaseTimingMeasurement> phaseTimings,
        RuntimeResourceMeasurements resources)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operationKey);
        _ = SemanticIdentity.Parse(algorithmSemanticIdentity);

        InsightOperationRegistry.ValidateResultStrategySelection(
            operationKey,
            strategy,
            execution.Status);
        ArgumentOutOfRangeException.ThrowIfNegative(totalResultCardinality);

        var retainedItems = items.ToArray();
        if (retainedItems.Length > MaximumRetainedItems)
        {
            throw new ArgumentException(
                $"A compact result envelope may retain at most {MaximumRetainedItems} items.",
                nameof(items));
        }

        if (retainedItems.LongLength > totalResultCardinality)
        {
            throw new ArgumentException("Retained item count cannot exceed total result cardinality.", nameof(items));
        }

        RunId = runId;
        SampleId = sampleId;
        OperationKey = operationKey;
        AlgorithmSemanticIdentity = algorithmSemanticIdentity;
        Strategy = strategy;
        Identifiers = identifiers;
        CanonicalParameters = canonicalParameters;
        Execution = execution;
        SummaryMetrics = new ReadOnlyDictionary<string, JsonElement>(
            new Dictionary<string, JsonElement>(summaryMetrics, StringComparer.Ordinal));
        TotalResultCardinality = totalResultCardinality;
        Items = Array.AsReadOnly(retainedItems);
        ResultDigest = resultDigest;
        OrderedPaths = Array.AsReadOnly(orderedPaths.ToArray());
        PhaseTimings = Array.AsReadOnly(phaseTimings.ToArray());
        Resources = resources;
    }

    public Guid RunId { get; }
    public Guid SampleId { get; }
    public string OperationKey { get; }
    public string AlgorithmSemanticIdentity { get; }
    public StrategySelection Strategy { get; }
    public GraphTargetIdentifiers Identifiers { get; }
    public CanonicalParameters CanonicalParameters { get; }
    public ExecutionOutcome Execution { get; }
    public IReadOnlyDictionary<string, JsonElement> SummaryMetrics { get; }
    public long TotalResultCardinality { get; }
    public IReadOnlyList<JsonElement> Items { get; }
    public string? ResultDigest { get; }
    public IReadOnlyList<OrderedPathProjection> OrderedPaths { get; }
    public IReadOnlyList<PhaseTimingMeasurement> PhaseTimings { get; }
    public RuntimeResourceMeasurements Resources { get; }
}
