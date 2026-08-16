using System.Text.Json;
using System.Text.Json.Nodes;
using Backend.Insights.Contracts;
using Backend.Insights.Workers;

namespace backend.Tests.Insights.Workers;

[TestClass]
public class WorkerProtocolTests
{
    [TestMethod]
    public void RequestFrame_FreezesIdentityVersionAndCanonicalJson()
    {
        var request = CreateRequest();

        var serialized = WorkerProtocolJson.Serialize(request);
        var roundTripped = WorkerProtocolJson.DeserializeRequest(serialized);

        Assert.AreEqual(WorkerProtocol.Identity, roundTripped.ProtocolIdentity);
        Assert.AreEqual(WorkerProtocol.Version, roundTripped.ProtocolVersion);
        Assert.AreEqual(WorkerMessageType.Request, roundTripped.MessageType);
        Assert.AreEqual(request.RunId, roundTripped.RunId);
        Assert.AreEqual(request.SampleId, roundTripped.SampleId);
        Assert.AreEqual(CanonicalJson.Canonicalize(request), serialized);
        Assert.IsFalse(serialized.Contains('\n'));
    }

    [TestMethod]
    public void ProtocolJson_RejectsNonCanonicalUnknownAndWrongVersionFrames()
    {
        var canonical = WorkerProtocolJson.Serialize(CreateRequest());

        Assert.ThrowsException<WorkerProtocolException>(() =>
            WorkerProtocolJson.DeserializeRequest($" {canonical}"));

        var withUnknown = JsonNode.Parse(canonical)!.AsObject();
        withUnknown["unexpected"] = true;
        var unknownCanonical = CanonicalJson.Canonicalize(
            JsonSerializer.SerializeToElement(withUnknown));
        Assert.ThrowsException<WorkerProtocolException>(() =>
            WorkerProtocolJson.DeserializeRequest(unknownCanonical));

        var wrongVersion = JsonNode.Parse(canonical)!.AsObject();
        wrongVersion["protocolVersion"] = 2;
        var wrongVersionCanonical = CanonicalJson.Canonicalize(
            JsonSerializer.SerializeToElement(wrongVersion));
        Assert.ThrowsException<WorkerProtocolException>(() =>
            WorkerProtocolJson.DeserializeRequest(wrongVersionCanonical));

        var unknownOperation = JsonNode.Parse(canonical)!.AsObject();
        unknownOperation["operationKey"] = "unknown-operation";
        var unknownOperationCanonical = CanonicalJson.Canonicalize(
            JsonSerializer.SerializeToElement(unknownOperation));
        Assert.ThrowsException<WorkerProtocolException>(() =>
            WorkerProtocolJson.DeserializeRequest(unknownOperationCanonical));
    }

    [TestMethod]
    public void RequestFrame_RejectsMismatchedCanonicalParameterDigest()
    {
        var value = JsonSerializer.SerializeToElement(new { fixture = true });

        Assert.ThrowsException<ArgumentException>(() => new WorkerRequestFrame(
            Guid.NewGuid(),
            Guid.NewGuid(),
            OperationKeys.GraphFetch,
            AlgorithmSemanticIdentities.GraphFetchV1,
            new CanonicalParameters(value, CanonicalJson.ComputeSha256(new { fixture = false })),
            JsonSerializer.SerializeToElement(new { mode = "success" })));
    }

    [TestMethod]
    public void EventFrame_RequiresOneMatchingPayloadAndATerminalStatus()
    {
        var request = CreateRequest();
        var sample = CreateSample(request.RunId, request.SampleId);

        Assert.ThrowsException<ArgumentException>(() => new WorkerEventFrame(
            WorkerProtocol.Identity,
            WorkerProtocol.Version,
            WorkerMessageType.Event,
            request.RunId,
            request.SampleId,
            0,
            WorkerEventKind.Sample,
            sample,
            null,
            new ExecutionOutcome(ExecutionStatus.Succeeded)));

        Assert.ThrowsException<ArgumentException>(() => WorkerEventFrame.ForTerminal(
            0,
            request.RunId,
            request.SampleId,
            new ExecutionOutcome(ExecutionStatus.Running)));

        Assert.ThrowsException<ArgumentException>(() => new WorkerEventFrame(
            WorkerProtocol.Identity,
            WorkerProtocol.Version,
            WorkerMessageType.Event,
            Guid.NewGuid(),
            request.SampleId,
            0,
            WorkerEventKind.Sample,
            sample,
            null,
            null));
    }

    [TestMethod]
    public void CancelFrame_RoundTripsExactCancellationReason()
    {
        var runId = Guid.NewGuid();
        var sampleId = Guid.NewGuid();
        var frame = new WorkerCancelFrame(
            runId,
            sampleId,
            WorkerCancellationReason.UserCancellation);

        var roundTripped = WorkerProtocolJson.DeserializeCancel(
            WorkerProtocolJson.Serialize(frame));

        Assert.AreEqual(runId, roundTripped.RunId);
        Assert.AreEqual(sampleId, roundTripped.SampleId);
        Assert.AreEqual(WorkerCancellationReason.UserCancellation, roundTripped.Reason);
    }

    private static WorkerRequestFrame CreateRequest()
    {
        var parametersValue = JsonSerializer.SerializeToElement(new { fixture = true });
        return new WorkerRequestFrame(
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            Guid.Parse("22222222-2222-2222-2222-222222222222"),
            OperationKeys.GraphFetch,
            AlgorithmSemanticIdentities.GraphFetchV1,
            new CanonicalParameters(
                parametersValue,
                CanonicalJson.ComputeSha256(parametersValue)),
            JsonSerializer.SerializeToElement(new { mode = "success" }));
    }

    private static RunSample CreateSample(Guid runId, Guid sampleId)
    {
        var units = new MeasurementUnitContract("ms", "ms", "bytes", "bytes", "count", "ratio");
        return new RunSample(
            runId,
            sampleId,
            "worker.fixture",
            OperationKeys.GraphFetch,
            "isolated-worker",
            "worker.fixture",
            1m,
            0,
            new IterationClassification(
                IterationClassificationTokens.Measured,
                IterationClassificationTokens.Warm,
                IterationClassificationTokens.PostJit,
                IterationClassificationTokens.WarmCache),
            new SampleNodeCounts(null, null, null, null),
            new SampleEdgeCounts(null, null, null),
            new SampleSearchCounts(null, null),
            null,
            new SampleTransportMeasurements(null, null, null, null),
            new RuntimeResourceMeasurements(null, null, null, null, null, "ms", null),
            new ExecutionOutcome(ExecutionStatus.Succeeded),
            units,
            TimingBoundaryProvenance.DirectlyInstrumented,
            null);
    }
}
