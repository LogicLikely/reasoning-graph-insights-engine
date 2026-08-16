using Backend.Calculation;
using Backend.Configuration;
using Backend.Data;
using Backend.Insights.Measurement;
using Backend.Repositories;
using Backend.Services;
using backend.Tests.Repositories;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Moq;

namespace backend.Tests.Insights.Measurement;

[TestClass]
public sealed class ServerPhaseSeamCharacterizationTests
{
    [TestMethod]
    public async Task GraphFetch_RecordsReconciledRepositoryThenDtoMappingSequence()
    {
        var connection = new FakeDbConnection();
        connection.WhenCommandContains(
            "FROM graphs",
            [
                new Dictionary<string, object?>
                {
                    ["id"] = 1,
                    ["slug"] = "fixture",
                    ["title"] = "Fixture",
                    ["description"] = "Fixture graph"
                }
            ]);
        connection.WhenCommandContains(
            "FROM nodes",
            [
                new Dictionary<string, object?>
                {
                    ["id"] = "node-1",
                    ["kind"] = "evidence",
                    ["title"] = "Evidence",
                    ["BodyText"] = "Evidence body",
                    ["category"] = null,
                    ["tags"] = Array.Empty<string>(),
                    ["PriorOdds"] = 0m,
                    ["PosteriorOdds"] = 0m,
                    ["evidence"] = "{\"type\":\"fixture\",\"score\":50}"
                }
            ]);
        connection.WhenCommandContains("FROM edges", []);

        var collector = new InsightPhaseTimingCollector();
        var repository = Repository(connection, collector);
        var service = new GraphService(repository, new GraphLikelihoodCalculator(), collector);

        var graph = await service.GetBySlugAsync("fixture", CancellationToken.None);

        Assert.IsNotNull(graph);
        CollectionAssert.AreEqual(
            new[]
            {
                InsightMeasurementPhases.ConnectionOpenWait,
                InsightMeasurementPhases.GraphLookup,
                InsightMeasurementPhases.NodeQuery,
                InsightMeasurementPhases.EvidenceJsonMaterialization,
                InsightMeasurementPhases.EdgeQuery,
                InsightMeasurementPhases.DtoMapping
            },
            collector.Snapshot().Select(timing => timing.Phase).ToArray());
        Assert.IsTrue(collector.Snapshot().All(timing => timing.Duration >= 0));
    }

    [TestMethod]
    public async Task CatalogFetch_RecordsConnectionAggregationThenDtoMapping()
    {
        var connection = new FakeDbConnection();
        connection.WhenCommandContains(
            "FROM graphs AS graph",
            [
                new Dictionary<string, object?>
                {
                    ["slug"] = "fixture",
                    ["title"] = "Fixture",
                    ["description"] = "Fixture graph",
                    ["NodeCount"] = 3,
                    ["EdgeCount"] = 2
                }
            ]);
        var collector = new InsightPhaseTimingCollector();
        var repository = Repository(connection, collector);
        var service = new GraphService(repository, new GraphLikelihoodCalculator(), collector);

        var summaries = await service.GetSummariesAsync(CancellationToken.None);

        Assert.AreEqual(1, summaries.Count);
        CollectionAssert.AreEqual(
            new[]
            {
                InsightMeasurementPhases.ConnectionOpenWait,
                InsightMeasurementPhases.CatalogAggregation,
                InsightMeasurementPhases.DtoMapping
            },
            collector.Snapshot().Select(timing => timing.Phase).ToArray());
    }

    private static GraphRepository Repository(
        FakeDbConnection connection,
        IInsightPhaseTimingCollector collector)
    {
        var factory = new Mock<DbConnectionFactory>(Mock.Of<IOptions<DatabaseOptions>>());
        factory.Setup(value => value.CreateConnection()).Returns(connection);
        var environment = new Mock<IHostEnvironment>();
        environment.SetupGet(value => value.ContentRootPath).Returns(Path.GetTempPath());
        return new GraphRepository(factory.Object, environment.Object, collector);
    }
}
