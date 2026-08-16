using Backend.Insights.Contracts;

namespace backend.Tests.Insights.Contracts;

[TestClass]
public class OperationRegistryTests
{
    [TestMethod]
    public void Registry_HasExactFrozenOrderAndMetadata()
    {
        var expected = new[]
        {
            (OperationKeys.GraphCatalog, "Measure catalog retrieval/count aggregation with all stress graphs installed.",
                AlgorithmSemanticIdentities.GraphCatalogV1,
                OperationExposure.BenchmarkDiagnostic, OperationResultSurface.TimingAndCountSummary),
            (OperationKeys.GraphFetch, "Fetch, transfer, parse, and adapt a complete graph.",
                AlgorithmSemanticIdentities.GraphFetchV1,
                OperationExposure.BenchmarkDiagnostic, OperationResultSurface.GraphAndPayloadSummary),
            (OperationKeys.GraphSearch, "Find matches and the complete ancestor union, then admit or reject visualization.",
                AlgorithmSemanticIdentities.GraphSearchV1,
                OperationExposure.BenchmarkDiagnostic, OperationResultSurface.CountsAdmissionStatusAndOptionalSafeProjection),
            (OperationKeys.PathStrongest, "Find strongest paths in the requested direction.",
                AlgorithmSemanticIdentities.StrongestPathV1,
                OperationExposure.AnalysisAndBenchmark, OperationResultSurface.SummaryRankedOrderedPathsAndGraphMapFocus),
            (OperationKeys.PathSinglePair, "Exercise the current min/max single-pair path diagnostic.",
                AlgorithmSemanticIdentities.SinglePairPathV0,
                OperationExposure.BenchmarkDiagnostic, OperationResultSurface.DiagnosticResultAndTiming),
            (OperationKeys.EvidenceImpactRanking, "Rank supporting and counter evidence by target probability impact.",
                AlgorithmSemanticIdentities.EvidenceImpactV0,
                OperationExposure.AnalysisAndBenchmark, OperationResultSurface.SummaryDistributionAndDeterministicTop100),
            (OperationKeys.CounterCriticalSet, "Find a threshold-reaching counter set with exact, greedy, or auto strategy.",
                AlgorithmSemanticIdentities.CriticalCounterV1,
                OperationExposure.AnalysisAndBenchmark, OperationResultSurface.SelectedCountersQualityAndGraphMapFocus),
            (OperationKeys.NodeRobustness, "Rank nodes by the versioned robustness calculation.",
                AlgorithmSemanticIdentities.RobustnessV0,
                OperationExposure.AnalysisAndBenchmark, OperationResultSurface.LeastRobustSummaryDistributionAndDeterministicTop100),
            (OperationKeys.LikelihoodRecalculate, "Recalculate a selected node/ancestor chain after a defined change.",
                AlgorithmSemanticIdentities.LikelihoodRecalculateV0,
                OperationExposure.BenchmarkDiagnostic, OperationResultSurface.BeforeAndAfterLikelihoodSummary)
        };

        Assert.AreEqual(9, InsightOperationRegistry.Operations.Count);
        CollectionAssert.AreEqual(
            expected.Select(item => item.Item1).ToArray(),
            InsightOperationRegistry.Operations.Select(operation => operation.Key).ToArray());

        for (var index = 0; index < expected.Length; index++)
        {
            var actual = InsightOperationRegistry.Operations[index];
            Assert.AreEqual(expected[index].Item2, actual.Purpose);
            Assert.AreEqual(expected[index].Item3, actual.SemanticIdentity);
            Assert.AreEqual(expected[index].Item4, actual.Exposure);
            Assert.AreEqual(expected[index].Item5, actual.ResultSurface);
            Assert.AreSame(actual, InsightOperationRegistry.Get(actual.Key));
            Assert.AreEqual(actual.SemanticIdentity, SemanticIdentity.Parse(actual.SemanticIdentity).Value);
        }
    }

    [TestMethod]
    public void Registry_DeclaresOnlySupportedRequestedStrategies()
    {
        var singlePair = InsightOperationRegistry.Get(OperationKeys.PathSinglePair);
        CollectionAssert.AreEqual(
            new[] { OperationStrategyNames.Minimum, OperationStrategyNames.Maximum },
            singlePair.SupportedRequestedStrategies.ToArray());

        var criticalCounter = InsightOperationRegistry.Get(OperationKeys.CounterCriticalSet);
        CollectionAssert.AreEqual(
            new[] { OperationStrategyNames.Exact, OperationStrategyNames.Greedy, OperationStrategyNames.Auto },
            criticalCounter.SupportedRequestedStrategies.ToArray());
        CollectionAssert.AreEqual(
            new[] { OperationStrategyNames.Exact, OperationStrategyNames.Greedy },
            criticalCounter.SupportedUsedStrategies.ToArray());
        Assert.IsTrue(criticalCounter.SupportsRequestedStrategy(OperationStrategyNames.Auto));
        Assert.IsFalse(criticalCounter.SupportsRequestedStrategy("legacy"));

        Assert.IsTrue(InsightOperationRegistry.Operations
            .Where(operation => operation.Key is not OperationKeys.PathSinglePair and not OperationKeys.CounterCriticalSet)
            .All(operation => operation.SupportedRequestedStrategies.Count == 0));
    }

    [TestMethod]
    public void LegacyCriticalCounterIdentity_IsCharacterizationOnly()
    {
        Assert.AreEqual("critical-counter-heuristic-v0", AlgorithmSemanticIdentities.LegacyCriticalCounterHeuristicV0);
        Assert.IsFalse(InsightOperationRegistry.Operations.Any(operation =>
            operation.SemanticIdentity == AlgorithmSemanticIdentities.LegacyCriticalCounterHeuristicV0));
    }

    [TestMethod]
    public void StrategyValidation_IsStatusAwareAndFreezesRequestedToUsedTransitions()
    {
        InsightOperationRegistry.ValidateResultStrategySelection(
            OperationKeys.PathStrongest,
            new StrategySelection(null, null),
            ExecutionStatus.Succeeded);
        InsightOperationRegistry.ValidateResultStrategySelection(
            OperationKeys.PathSinglePair,
            new StrategySelection(OperationStrategyNames.Minimum, OperationStrategyNames.Minimum),
            ExecutionStatus.Succeeded);
        InsightOperationRegistry.ValidateResultStrategySelection(
            OperationKeys.CounterCriticalSet,
            new StrategySelection(OperationStrategyNames.Auto, OperationStrategyNames.Exact),
            ExecutionStatus.Succeeded);
        InsightOperationRegistry.ValidateResultStrategySelection(
            OperationKeys.PathSinglePair,
            new StrategySelection("invalid-request", null),
            ExecutionStatus.Failed);

        Assert.ThrowsException<ArgumentException>(() =>
            InsightOperationRegistry.ValidateResultStrategySelection(
                OperationKeys.PathStrongest,
                new StrategySelection(OperationStrategyNames.Maximum, OperationStrategyNames.Maximum),
                ExecutionStatus.Succeeded));
        Assert.ThrowsException<ArgumentException>(() =>
            InsightOperationRegistry.ValidateResultStrategySelection(
                OperationKeys.PathSinglePair,
                new StrategySelection(OperationStrategyNames.Minimum, OperationStrategyNames.Maximum),
                ExecutionStatus.Succeeded));
        Assert.ThrowsException<ArgumentException>(() =>
            InsightOperationRegistry.ValidateResultStrategySelection(
                OperationKeys.CounterCriticalSet,
                new StrategySelection(OperationStrategyNames.Auto, OperationStrategyNames.Auto),
                ExecutionStatus.Succeeded));
    }

    [TestMethod]
    public void RegistryAndStrategyCollections_AreReadOnly()
    {
        var operations = (IList<OperationContract>)InsightOperationRegistry.Operations;
        var strategies = (IList<string>)InsightOperationRegistry
            .Get(OperationKeys.CounterCriticalSet)
            .SupportedRequestedStrategies;

        Assert.ThrowsException<NotSupportedException>(() => operations.Clear());
        Assert.ThrowsException<NotSupportedException>(() => strategies.Add("legacy"));
        Assert.AreEqual(AlgorithmSemanticIdentities.GraphCatalogV1,
            InsightOperationRegistry.Operations[0].SemanticIdentity);
    }
}
