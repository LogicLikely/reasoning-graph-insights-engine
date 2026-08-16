using Backend.Insights.Measurement;

namespace backend.Tests.Insights.Measurement;

[TestClass]
public sealed class InsightPhaseRegistryTests
{
    [TestMethod]
    public void Definitions_FreezeTheCompleteOrderedPlanVocabulary()
    {
        var actual = InsightPhaseRegistry.Definitions
            .Select(definition =>
                $"{definition.Layer}/{definition.Phase}/{definition.ServerSideMeasurable}/{definition.IsPhasePrefix}")
            .ToArray();

        CollectionAssert.AreEqual(
            new[]
            {
                "postgresql-repository/connection-open-wait/True/False",
                "postgresql-repository/graph-lookup/True/False",
                "postgresql-repository/node-query/True/False",
                "postgresql-repository/edge-query/True/False",
                "postgresql-repository/evidence-json-materialization/True/False",
                "postgresql-repository/graph-construction/True/False",
                "postgresql-repository/catalog-aggregation/True/False",
                "backend-service-api/dto-mapping/True/False",
                "backend-service-api/validation/True/False",
                "backend-service-api/calculation-context-construction/True/False",
                "backend-service-api/algorithm/True/True",
                "backend-service-api/ranking/True/False",
                "backend-service-api/result-shaping/True/False",
                "backend-service-api/digest-generation/True/False",
                "backend-service-api/serialization/True/False",
                "benchmark-orchestration/fixture-construction/False/False",
                "benchmark-orchestration/operation-execution/False/False",
                "benchmark-orchestration/worker-supervision/False/False",
                "benchmark-orchestration/exact-greedy-quality-comparison/False/False",
                "benchmark-orchestration/persistence/False/False",
                "benchmark-orchestration/export-validation/False/False",
                "transport/response-bytes/True/False",
                "transport/time-to-first-byte/False/False",
                "transport/full-transfer/False/False",
                "browser-data/axios-receipt-parse/False/False",
                "browser-data/json-parse/False/False",
                "browser-data/domain-mapping/False/False",
                "browser-data/graph-map-adapter/False/False",
                "browser-data/search-index-construction/False/False",
                "browser-data/search-completion/False/False",
                "graph-map/node-edge-materialization/False/False",
                "graph-map/dagre-layout/False/False",
                "graph-map/react-commit/False/False",
                "graph-map/deferred-edge-commit/False/False",
                "graph-map/viewport-fit/False/False",
                "lab-result/result-render/False/False",
                "lab-result/react-commit/False/False",
                "end-to-end/action-to-stable-result-and-view/False/False"
            },
            actual);

        CollectionAssert.AreEqual(
            Enumerable.Range(0, actual.Length).ToArray(),
            InsightPhaseRegistry.Definitions.Select(definition => definition.Order).ToArray());
    }

    [TestMethod]
    public void AlgorithmSubphases_AreNamespacedAndDeterministicallyOrdered()
    {
        var traversal = InsightMeasurementPhases.AlgorithmSubphase("traversal");
        var reconstruction = InsightMeasurementPhases.AlgorithmSubphase("result-reconstruction.path");

        Assert.AreEqual("algorithm.traversal", traversal);
        Assert.AreEqual("algorithm.result-reconstruction.path", reconstruction);
        Assert.IsTrue(InsightPhaseRegistry.IsKnown(InsightMeasurementLayers.BackendServiceApi, traversal));
        Assert.IsTrue(InsightPhaseRegistry.Compare(
            InsightMeasurementLayers.BackendServiceApi,
            reconstruction,
            InsightMeasurementLayers.BackendServiceApi,
            traversal) < 0);
        Assert.ThrowsException<ArgumentException>(() =>
            InsightMeasurementPhases.AlgorithmSubphase("Not Valid"));
    }
}
