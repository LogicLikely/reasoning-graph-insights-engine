using System.Text.Json;
using Backend.Insights.Benchmarking;
using Backend.Insights.Contracts;
using Backend.Insights.Workers;

namespace backend.Tests.Insights.Workers;

[TestClass]
public sealed class Goal1WorkerDispatcherTests
{
    [TestMethod]
    public void SinglePair_UsesRequestedStrategyAndProducesStableLogicalDigest()
    {
        // Dispatcher contract coverage must stay in-process safe. The quick
        // runner exercises the deep fixture only through the isolated worker.
        var fixture = DeterministicStressGraphFixtureFactory.Create("stress-balanced-1k");
        var parameters = JsonSerializer.SerializeToElement(new
        {
            startNodeId = fixture.DeepestNodeId,
            targetNodeId = fixture.RootNodeId,
            requestedStrategy = OperationStrategyNames.Maximum
        });
        var request = Request(
            OperationKeys.PathSinglePair,
            parameters,
            new SinglePairPathV0WorkerInput(
                "test.single-pair", fixture.CreateGraph(), fixture.DeepestNodeId,
                fixture.RootNodeId, OperationStrategyNames.Maximum));

        var output = new AnalysisWorkerDispatcher().Dispatch(request);

        Assert.AreEqual(OperationStrategyNames.Maximum, output.Strategy.Used);
        Assert.AreEqual(1L, output.TotalResultCardinality);
        Assert.AreEqual(CanonicalJson.ComputeSha256(output.Items), output.ResultDigest);
    }

    [TestMethod]
    public void LikelihoodRecalculation_ProducesOrderedAffectedNodeDigest()
    {
        var fixture = DeterministicStressGraphFixtureFactory.Create("stress-balanced-1k");
        var parameters = JsonSerializer.SerializeToElement(new { changedNodeId = fixture.DeepestNodeId });
        var request = Request(
            OperationKeys.LikelihoodRecalculate,
            parameters,
            new LikelihoodRecalculateV0WorkerInput(
                "test.likelihood", fixture.CreateGraph(), fixture.DeepestNodeId));

        var output = new AnalysisWorkerDispatcher().Dispatch(request);

        Assert.IsTrue(output.TotalResultCardinality > 0);
        Assert.AreEqual(CanonicalJson.ComputeSha256(output.Items), output.ResultDigest);
        Assert.IsNull(output.Strategy.Requested);
        Assert.IsNull(output.Strategy.Used);
    }

    [TestMethod]
    public void CriticalCounter_LegacyFourFieldParametersRemainCompatible()
    {
        var fixture = DeterministicStressGraphFixtureFactory.Create("stress-balanced-1k");
        var parameters = JsonSerializer.SerializeToElement(new
        {
            targetNodeId = "n-00015",
            requestedStrategy = OperationStrategyNames.Exact,
            thresholdLogOdds = -1m,
            autoCandidateCutoff = (int?)null
        });
        var request = Request(
            OperationKeys.CounterCriticalSet,
            parameters,
            new CriticalCounterV1WorkerInput(
                "test.critical-legacy",
                fixture.CreateGraph(),
                "n-00015",
                OperationStrategyNames.Exact,
                -1m,
                null));

        var output = new AnalysisWorkerDispatcher().Dispatch(request);

        Assert.AreEqual(ExecutionStatus.Succeeded, output.Execution.Status);
        Assert.AreEqual(OperationStrategyNames.Exact, output.Strategy.Used);
    }

    private static WorkerRequestFrame Request<TInput>(
        string operationKey,
        JsonElement parameters,
        TInput input) => new(
            Guid.NewGuid(),
            Guid.NewGuid(),
            operationKey,
            InsightOperationRegistry.Get(operationKey).SemanticIdentity,
            new CanonicalParameters(parameters, CanonicalJson.ComputeSha256(parameters)),
            JsonSerializer.SerializeToElement(input, CanonicalJson.CreateSerializerOptions()));
}
