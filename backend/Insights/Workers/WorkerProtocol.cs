using System.Text.Json;
using System.Text.Json.Serialization;
using Backend.Insights.Contracts;

namespace Backend.Insights.Workers;

public static class WorkerProtocol
{
    public const string Identity = "insights-worker-protocol-v1";
    public const int Version = 1;
}

public enum WorkerMessageType
{
    Request,
    Cancel,
    Event
}

public enum WorkerCancellationReason
{
    UserCancellation,
    Timeout
}

public enum WorkerEventKind
{
    Sample,
    Output,
    Terminal
}

public sealed record WorkerRequestFrame
{
    [JsonConstructor]
    public WorkerRequestFrame(
        string protocolIdentity,
        int protocolVersion,
        WorkerMessageType messageType,
        Guid runId,
        Guid sampleId,
        string operationKey,
        string algorithmSemanticIdentity,
        CanonicalParameters canonicalParameters,
        JsonElement input)
    {
        WorkerProtocolValidation.ValidateEnvelope(
            protocolIdentity,
            protocolVersion,
            messageType,
            WorkerMessageType.Request,
            runId,
            sampleId);

        var operation = InsightOperationRegistry.Get(operationKey);
        if (!string.Equals(
                operation.SemanticIdentity,
                algorithmSemanticIdentity,
                StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"Algorithm semantic identity '{algorithmSemanticIdentity}' does not match operation '{operationKey}'.",
                nameof(algorithmSemanticIdentity));
        }

        WorkerProtocolValidation.ValidateCanonicalParameters(canonicalParameters);
        if (input.ValueKind == JsonValueKind.Undefined)
        {
            throw new ArgumentException("Worker input must be a defined JSON value.", nameof(input));
        }

        ProtocolIdentity = protocolIdentity;
        ProtocolVersion = protocolVersion;
        MessageType = messageType;
        RunId = runId;
        SampleId = sampleId;
        OperationKey = operationKey;
        AlgorithmSemanticIdentity = algorithmSemanticIdentity;
        CanonicalParameters = new CanonicalParameters(
            canonicalParameters.Value.Clone(),
            canonicalParameters.Digest);
        Input = input.Clone();
    }

    public WorkerRequestFrame(
        Guid runId,
        Guid sampleId,
        string operationKey,
        string algorithmSemanticIdentity,
        CanonicalParameters canonicalParameters,
        JsonElement input)
        : this(
            WorkerProtocol.Identity,
            WorkerProtocol.Version,
            WorkerMessageType.Request,
            runId,
            sampleId,
            operationKey,
            algorithmSemanticIdentity,
            canonicalParameters,
            input)
    {
    }

    public string ProtocolIdentity { get; }
    public int ProtocolVersion { get; }
    public WorkerMessageType MessageType { get; }
    public Guid RunId { get; }
    public Guid SampleId { get; }
    public string OperationKey { get; }
    public string AlgorithmSemanticIdentity { get; }
    public CanonicalParameters CanonicalParameters { get; }
    public JsonElement Input { get; }
}

public sealed record WorkerCancelFrame
{
    [JsonConstructor]
    public WorkerCancelFrame(
        string protocolIdentity,
        int protocolVersion,
        WorkerMessageType messageType,
        Guid runId,
        Guid sampleId,
        WorkerCancellationReason reason)
    {
        WorkerProtocolValidation.ValidateEnvelope(
            protocolIdentity,
            protocolVersion,
            messageType,
            WorkerMessageType.Cancel,
            runId,
            sampleId);

        if (!Enum.IsDefined(reason))
        {
            throw new ArgumentOutOfRangeException(nameof(reason), reason, "Unknown worker cancellation reason.");
        }

        ProtocolIdentity = protocolIdentity;
        ProtocolVersion = protocolVersion;
        MessageType = messageType;
        RunId = runId;
        SampleId = sampleId;
        Reason = reason;
    }

    public WorkerCancelFrame(Guid runId, Guid sampleId, WorkerCancellationReason reason)
        : this(
            WorkerProtocol.Identity,
            WorkerProtocol.Version,
            WorkerMessageType.Cancel,
            runId,
            sampleId,
            reason)
    {
    }

    public string ProtocolIdentity { get; }
    public int ProtocolVersion { get; }
    public WorkerMessageType MessageType { get; }
    public Guid RunId { get; }
    public Guid SampleId { get; }
    public WorkerCancellationReason Reason { get; }
}

public sealed record WorkerEventFrame
{
    [JsonConstructor]
    public WorkerEventFrame(
        string protocolIdentity,
        int protocolVersion,
        WorkerMessageType messageType,
        Guid runId,
        Guid sampleId,
        long sequence,
        WorkerEventKind eventKind,
        RunSample? sample,
        CompactRunOutput? output,
        ExecutionOutcome? terminal)
    {
        WorkerProtocolValidation.ValidateEnvelope(
            protocolIdentity,
            protocolVersion,
            messageType,
            WorkerMessageType.Event,
            runId,
            sampleId);

        ArgumentOutOfRangeException.ThrowIfNegative(sequence);
        if (!Enum.IsDefined(eventKind))
        {
            throw new ArgumentOutOfRangeException(nameof(eventKind), eventKind, "Unknown worker event kind.");
        }

        var payloadCount = (sample is null ? 0 : 1) +
                           (output is null ? 0 : 1) +
                           (terminal is null ? 0 : 1);
        if (payloadCount != 1)
        {
            throw new ArgumentException("A worker event must contain exactly one event payload.");
        }

        switch (eventKind)
        {
            case WorkerEventKind.Sample when sample is not null:
                WorkerProtocolValidation.ValidateCorrelation(runId, sampleId, sample.RunId, sample.SampleId);
                break;

            case WorkerEventKind.Output when output is not null:
                WorkerProtocolValidation.ValidateCorrelation(runId, sampleId, output.RunId, output.SampleId);
                break;

            case WorkerEventKind.Terminal when terminal is not null:
                if (terminal.Status is ExecutionStatus.Queued or ExecutionStatus.Running)
                {
                    throw new ArgumentException(
                        $"Execution status '{terminal.Status}' is not terminal.",
                        nameof(terminal));
                }

                break;

            default:
                throw new ArgumentException($"Worker event kind '{eventKind}' does not match its payload.");
        }

        ProtocolIdentity = protocolIdentity;
        ProtocolVersion = protocolVersion;
        MessageType = messageType;
        RunId = runId;
        SampleId = sampleId;
        Sequence = sequence;
        EventKind = eventKind;
        Sample = sample;
        Output = output;
        Terminal = terminal;
    }

    public string ProtocolIdentity { get; }
    public int ProtocolVersion { get; }
    public WorkerMessageType MessageType { get; }
    public Guid RunId { get; }
    public Guid SampleId { get; }
    public long Sequence { get; }
    public WorkerEventKind EventKind { get; }
    public RunSample? Sample { get; }
    public CompactRunOutput? Output { get; }
    public ExecutionOutcome? Terminal { get; }

    public static WorkerEventFrame ForSample(long sequence, RunSample sample)
    {
        ArgumentNullException.ThrowIfNull(sample);
        return new WorkerEventFrame(
            WorkerProtocol.Identity,
            WorkerProtocol.Version,
            WorkerMessageType.Event,
            sample.RunId,
            sample.SampleId,
            sequence,
            WorkerEventKind.Sample,
            sample,
            null,
            null);
    }

    public static WorkerEventFrame ForOutput(long sequence, CompactRunOutput output)
    {
        ArgumentNullException.ThrowIfNull(output);
        return new WorkerEventFrame(
            WorkerProtocol.Identity,
            WorkerProtocol.Version,
            WorkerMessageType.Event,
            output.RunId,
            output.SampleId,
            sequence,
            WorkerEventKind.Output,
            null,
            output,
            null);
    }

    public static WorkerEventFrame ForTerminal(
        long sequence,
        Guid runId,
        Guid sampleId,
        ExecutionOutcome terminal)
    {
        ArgumentNullException.ThrowIfNull(terminal);
        return new WorkerEventFrame(
            WorkerProtocol.Identity,
            WorkerProtocol.Version,
            WorkerMessageType.Event,
            runId,
            sampleId,
            sequence,
            WorkerEventKind.Terminal,
            null,
            null,
            terminal);
    }
}

public static class WorkerProtocolJson
{
    private static readonly JsonSerializerOptions SerializerOptions = CanonicalJson.CreateSerializerOptions();

    public static string Serialize(WorkerRequestFrame frame) => SerializeCanonical(frame);

    public static string Serialize(WorkerCancelFrame frame) => SerializeCanonical(frame);

    public static string Serialize(WorkerEventFrame frame) => SerializeCanonical(frame);

    public static WorkerRequestFrame DeserializeRequest(string line) =>
        DeserializeCanonical<WorkerRequestFrame>(line, "worker request");

    public static WorkerCancelFrame DeserializeCancel(string line) =>
        DeserializeCanonical<WorkerCancelFrame>(line, "worker cancellation");

    public static WorkerEventFrame DeserializeEvent(string line) =>
        DeserializeCanonical<WorkerEventFrame>(line, "worker event");

    private static string SerializeCanonical<T>(T frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        return CanonicalJson.Canonicalize(frame, SerializerOptions);
    }

    private static T DeserializeCanonical<T>(string line, string description)
    {
        if (string.IsNullOrEmpty(line))
        {
            throw new WorkerProtocolException($"The {description} frame is empty.");
        }

        try
        {
            using var document = JsonDocument.Parse(line);
            var canonical = CanonicalJson.Canonicalize(document.RootElement);
            if (!string.Equals(line, canonical, StringComparison.Ordinal))
            {
                throw new WorkerProtocolException($"The {description} frame is not canonical JSON.");
            }

            return JsonSerializer.Deserialize<T>(line, SerializerOptions)
                   ?? throw new WorkerProtocolException($"The {description} frame deserialized to null.");
        }
        catch (WorkerProtocolException)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is JsonException or FormatException or ArgumentException or KeyNotFoundException)
        {
            throw new WorkerProtocolException($"The {description} frame is invalid.", exception);
        }
    }
}

public sealed class WorkerProtocolException : Exception
{
    public WorkerProtocolException(string message)
        : base(message)
    {
    }

    public WorkerProtocolException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

internal static class WorkerProtocolValidation
{
    public static void ValidateEnvelope(
        string protocolIdentity,
        int protocolVersion,
        WorkerMessageType messageType,
        WorkerMessageType expectedMessageType,
        Guid runId,
        Guid sampleId)
    {
        if (!string.Equals(protocolIdentity, WorkerProtocol.Identity, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"Worker protocol identity must be '{WorkerProtocol.Identity}'.",
                nameof(protocolIdentity));
        }

        if (protocolVersion != WorkerProtocol.Version)
        {
            throw new ArgumentOutOfRangeException(
                nameof(protocolVersion),
                protocolVersion,
                $"Worker protocol version must be {WorkerProtocol.Version}.");
        }

        if (messageType != expectedMessageType)
        {
            throw new ArgumentException(
                $"Worker message type must be '{expectedMessageType}'.",
                nameof(messageType));
        }

        if (runId == Guid.Empty)
        {
            throw new ArgumentException("Worker run ID must not be empty.", nameof(runId));
        }

        if (sampleId == Guid.Empty)
        {
            throw new ArgumentException("Worker sample ID must not be empty.", nameof(sampleId));
        }
    }

    public static void ValidateCanonicalParameters(CanonicalParameters canonicalParameters)
    {
        ArgumentNullException.ThrowIfNull(canonicalParameters);
        if (canonicalParameters.Value.ValueKind == JsonValueKind.Undefined)
        {
            throw new ArgumentException(
                "Canonical parameter value must be defined.",
                nameof(canonicalParameters));
        }

        var actualDigest = CanonicalJson.ComputeSha256(canonicalParameters.Value);
        if (!string.Equals(actualDigest, canonicalParameters.Digest, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "Canonical parameter digest does not match its value.",
                nameof(canonicalParameters));
        }
    }

    public static void ValidateCorrelation(
        Guid frameRunId,
        Guid frameSampleId,
        Guid payloadRunId,
        Guid payloadSampleId)
    {
        if (frameRunId != payloadRunId || frameSampleId != payloadSampleId)
        {
            throw new ArgumentException("Worker event payload correlation does not match the frame.");
        }
    }
}
