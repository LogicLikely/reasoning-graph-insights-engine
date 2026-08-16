using System.Text.Json;
using Backend.Insights.Analysis;
using Backend.Insights.Benchmarking;
using Backend.Insights.Contracts;
using Backend.Insights.Measurement;
using Backend.Insights.Workers;
using Backend.Seeding;

namespace backend.Tests.Insights.Benchmarking;

[TestClass]
public sealed class BenchmarkOperationExecutorQualityTests
{
    [TestMethod]
    [Timeout(15_000)]
    public async Task TractableQualityFlag_RecordsDeterministicComparisonWithoutChangingCanonicalResult()
    {
        var fixture = DeterministicStressGraphFixtureFactory.Create(StressGraphSeedIds.Balanced1K);
        var scenario = QualityScenario();
        var operation = BenchmarkOperationRequestFactory.Create(
            scenario,
            fixture,
            scenario.Parameters,
            Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"));
        var canonical = new AnalysisWorkerDispatcher().Dispatch(operation.Request);

        var first = await new BenchmarkOperationExecutor().ExecuteAsync(
            operation,
            scenario,
            fixture,
            BenchmarkProfiles.Quick,
            TimeSpan.FromSeconds(10),
            CancellationToken.None);
        var second = await new BenchmarkOperationExecutor().ExecuteAsync(
            operation,
            scenario,
            fixture,
            BenchmarkProfiles.Quick,
            TimeSpan.FromSeconds(10),
            CancellationToken.None);

        Assert.AreEqual(ExecutionStatus.Succeeded, first.Execution.Status);
        Assert.AreEqual(2, first.Samples.Count);
        var qualitySample = first.Samples.Single(sample =>
            sample.Phase == InsightMeasurementPhases.ExactGreedyQualityComparison);
        Assert.AreEqual(InsightMeasurementLayers.BenchmarkOrchestration, qualitySample.Layer);
        Assert.AreEqual(TimingBoundaryProvenance.DirectlyInstrumented,
            qualitySample.TimingBoundaryProvenance);
        Assert.AreEqual(ExecutionStatus.Succeeded, qualitySample.Execution.Status);
        Assert.IsTrue(qualitySample.OperationCounters?.CandidateCount <= 8);

        var output = first.Outputs.Single();
        Assert.AreEqual(canonical.ResultDigest, output.ResultDigest);
        CollectionAssert.AreEqual(
            canonical.Items.Select(CanonicalJson.Canonicalize).ToArray(),
            output.Items.Select(CanonicalJson.Canonicalize).ToArray());
        Assert.AreEqual(canonical.TotalResultCardinality, output.TotalResultCardinality);
        Assert.AreEqual(canonical.AlgorithmSemanticIdentity, output.AlgorithmSemanticIdentity);
        Assert.IsTrue(output.Summary.GetProperty("qualityComparisonRecorded").GetBoolean());

        var quality = output.Distribution.GetProperty("exactGreedyQuality");
        Assert.AreEqual("CriticalCounterV1Analyzer.CompareExactAndGreedy",
            quality.GetProperty("method").GetString());
        Assert.AreEqual("shared-runner-process", quality.GetProperty("executionBoundary").GetString());
        Assert.AreEqual("directly-instrumented",
            quality.GetProperty("timingBoundaryProvenance").GetString());
        Assert.AreEqual(-1m, quality.GetProperty("thresholdLogOdds").GetDecimal());
        Assert.IsTrue(quality.GetProperty("exactResultDigest").GetString()!.StartsWith("sha256:"));
        Assert.IsTrue(quality.GetProperty("greedyResultDigest").GetString()!.StartsWith("sha256:"));
        Assert.IsTrue(quality.TryGetProperty("cardinalityGapFromOptimal", out _));
        Assert.IsTrue(quality.TryGetProperty("selectedSetJaccardSimilarity", out _));
        Assert.IsTrue(quality.TryGetProperty("exactBelowThresholdMargin", out _));
        Assert.IsTrue(quality.TryGetProperty("greedyBelowThresholdMargin", out _));
        Assert.IsTrue(quality.GetProperty("tractability")
            .GetProperty("actualCandidateCount").GetInt32() <= 8);

        Assert.AreEqual(
            CanonicalJson.Canonicalize(quality),
            CanonicalJson.Canonicalize(second.Outputs.Single().Distribution
                .GetProperty("exactGreedyQuality")));
        Assert.AreEqual(output.ResultDigest, second.Outputs.Single().ResultDigest);
    }

    [TestMethod]
    [Timeout(30_000)]
    public async Task RegisteredQuickQualityScenario_RoundTripsThroughVersionedExport()
    {
        var run = (await new SerialBenchmarkRunner(new BenchmarkOperationExecutor()).RunAsync(
            new BenchmarkRunSelection(
                BenchmarkProfiles.QuickKey,
                ScenarioKey: "quick.counter.exact.balanced-1k"))).Runs.Single();

        Assert.AreEqual(ExecutionStatus.Succeeded, run.Manifest.Execution.Status);
        var output = run.Outputs.Single();
        var quality = output.Distribution.GetProperty("exactGreedyQuality");
        Assert.AreEqual(output.ResultDigest, quality.GetProperty("exactResultDigest").GetString());
        Assert.AreEqual(
            CanonicalJson.Canonicalize(quality),
            CanonicalJson.Canonicalize(run.DeserializedExport.Outputs.Single()
                .Distribution.GetProperty("exactGreedyQuality")));
        Assert.AreEqual(run.Export.Digests.OutputsDigest,
            run.DeserializedExport.Digests.OutputsDigest);
        Assert.IsTrue(run.Samples.Any(sample =>
            sample.Phase == InsightMeasurementPhases.ExactGreedyQualityComparison &&
            sample.Classification.Temperature == IterationClassificationTokens.Warm));
    }

    [TestMethod]
    public void QualityFlag_RejectsNonCriticalAndUnboundedCriticalScenarios()
    {
        var strongestParameters = JsonSerializer.SerializeToElement(new
        {
            startNodeId = "n-00000",
            direction = "down"
        });
        Assert.ThrowsException<ArgumentException>(() => new BenchmarkScenarioDefinition(
            "invalid-quality-strongest",
            "invalid",
            BenchmarkProfiles.QuickKey,
            OperationKeys.PathStrongest,
            StressGraphSeedIds.Balanced1K,
            strongestParameters,
            null,
            requiresIsolation: false,
            measureQualityComparison: true));

        var unboundedCriticalParameters = JsonSerializer.SerializeToElement(new
        {
            targetNodeId = "n-00015",
            requestedStrategy = OperationStrategyNames.Exact,
            thresholdLogOdds = -1m,
            autoCandidateCutoff = (int?)null,
            candidateLimit = (int?)null
        });
        Assert.ThrowsException<ArgumentException>(() => new BenchmarkScenarioDefinition(
            "invalid-quality-unbounded",
            "invalid",
            BenchmarkProfiles.QuickKey,
            OperationKeys.CounterCriticalSet,
            StressGraphSeedIds.Balanced1K,
            unboundedCriticalParameters,
            OperationStrategyNames.Exact,
            requiresIsolation: true,
            measureQualityComparison: true));
    }

    [TestMethod]
    public async Task QualityDisabled_DoesNotAddComparisonPhaseOrOutputEvidence()
    {
        var fixture = DeterministicStressGraphFixtureFactory.Create(StressGraphSeedIds.Balanced1K);
        var source = QualityScenario();
        var scenario = new BenchmarkScenarioDefinition(
            "quality-disabled",
            "quality disabled",
            BenchmarkProfiles.QuickKey,
            source.OperationKey,
            source.DatasetId,
            source.Parameters,
            source.RequestedStrategy,
            requiresIsolation: false);
        var operation = BenchmarkOperationRequestFactory.Create(
            scenario,
            fixture,
            scenario.Parameters,
            Guid.NewGuid(),
            Guid.NewGuid());

        var result = await new BenchmarkOperationExecutor().ExecuteAsync(
            operation,
            scenario,
            fixture,
            BenchmarkProfiles.Quick,
            TimeSpan.FromSeconds(10),
            CancellationToken.None);

        Assert.AreEqual(ExecutionStatus.Succeeded, result.Execution.Status);
        Assert.IsFalse(result.Samples.Any(sample =>
            sample.Phase == InsightMeasurementPhases.ExactGreedyQualityComparison));
        Assert.IsFalse(result.Outputs.Single().Summary.TryGetProperty(
            "qualityComparisonRecorded", out _));
        Assert.IsFalse(result.Outputs.Single().Distribution.TryGetProperty(
            "exactGreedyQuality", out _));
    }

    [TestMethod]
    public void QualityPhase_IsRegisteredAfterPrimaryExecutionBoundaries()
    {
        Assert.IsTrue(InsightPhaseRegistry.IsKnown(
            InsightMeasurementLayers.BenchmarkOrchestration,
            InsightMeasurementPhases.ExactGreedyQualityComparison));
        Assert.IsTrue(InsightPhaseRegistry.Compare(
            InsightMeasurementLayers.BenchmarkOrchestration,
            InsightMeasurementPhases.WorkerSupervision,
            InsightMeasurementLayers.BenchmarkOrchestration,
            InsightMeasurementPhases.ExactGreedyQualityComparison) < 0);
    }

    private static BenchmarkScenarioDefinition QualityScenario()
    {
        var parameters = JsonSerializer.SerializeToElement(new
        {
            targetNodeId = "n-00015",
            requestedStrategy = OperationStrategyNames.Exact,
            thresholdLogOdds = CriticalCounterV1Contract.DefaultThresholdLogOdds,
            autoCandidateCutoff = (int?)null,
            candidateLimit = 8
        });
        return new BenchmarkScenarioDefinition(
            "test.counter.quality.balanced-1k",
            "Tractable exact-versus-greedy quality comparison.",
            BenchmarkProfiles.QuickKey,
            OperationKeys.CounterCriticalSet,
            StressGraphSeedIds.Balanced1K,
            parameters,
            OperationStrategyNames.Exact,
            requiresIsolation: false,
            measureQualityComparison: true);
    }
}
