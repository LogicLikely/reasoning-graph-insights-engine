using Backend.Calculation;
using Backend.Configuration;
using Backend.Data;
using Backend.Insights.Measurement;
using Backend.Models.Domain;
using Backend.Models.Dto;
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
                InsightMeasurementPhases.EdgeQuery,
                InsightMeasurementPhases.EvidenceJsonMaterialization,
                InsightMeasurementPhases.GraphConstruction,
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

    [TestMethod]
    public async Task EvidenceImpact_DatabaseAndSuppliedGraphPathsRemainEqualAndExposeAnalysisPhases()
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
                NodeRow("R", "claim"),
                NodeRow("E", "evidence"),
                NodeRow("O", "objection")
            ]);
        connection.WhenCommandContains(
            "FROM edges",
            [
                EdgeRow("edge-e", "E", "R", "support", 2m),
                EdgeRow("edge-o", "O", "R", "rebut", 0.5m)
            ]);

        var databaseCollector = new InsightPhaseTimingCollector();
        var databaseService = new GraphService(
            Repository(connection, databaseCollector),
            new GraphLikelihoodCalculator(),
            databaseCollector);

        var databaseResult = await databaseService.GetEvidenceImpactRankingAsync(
            "fixture",
            "R",
            CancellationToken.None);

        Assert.IsNotNull(databaseResult);
        CollectionAssert.AreEqual(
            new[]
            {
                InsightMeasurementPhases.ConnectionOpenWait,
                InsightMeasurementPhases.GraphLookup,
                InsightMeasurementPhases.NodeQuery,
                InsightMeasurementPhases.EdgeQuery,
                InsightMeasurementPhases.EvidenceJsonMaterialization,
                InsightMeasurementPhases.GraphConstruction,
                InsightMeasurementPhases.Validation,
                InsightMeasurementPhases.CalculationContextConstruction,
                InsightMeasurementPhases.Algorithm,
                InsightMeasurementPhases.AlgorithmSubphase("strongest-path"),
                InsightMeasurementPhases.AlgorithmSubphase("counterfactual-evaluation"),
                InsightMeasurementPhases.Ranking,
                InsightMeasurementPhases.ResultShaping,
                InsightMeasurementPhases.DigestGeneration,
                InsightMeasurementPhases.DtoMapping
            },
            databaseCollector.Snapshot().Select(timing => timing.Phase).ToArray());

        var suppliedCollector = new InsightPhaseTimingCollector();
        var suppliedService = new GraphService(
            Mock.Of<IGraphRepository>(),
            new GraphLikelihoodCalculator(),
            suppliedCollector);
        var suppliedResult = await suppliedService.GetEvidenceImpactRankingAsync(
            "fixture",
            "R",
            SuppliedGraph(),
            CancellationToken.None);

        Assert.IsNotNull(suppliedResult);
        CollectionAssert.AreEqual(
            databaseResult.SupportingEvidence.Select(item => item.NodeId).ToArray(),
            suppliedResult.SupportingEvidence.Select(item => item.NodeId).ToArray());
        CollectionAssert.AreEqual(
            databaseResult.CounterEvidence.Select(item => item.NodeId).ToArray(),
            suppliedResult.CounterEvidence.Select(item => item.NodeId).ToArray());
        CollectionAssert.AreEqual(
            new[]
            {
                InsightMeasurementPhases.DtoMapping,
                InsightMeasurementPhases.Validation,
                InsightMeasurementPhases.CalculationContextConstruction,
                InsightMeasurementPhases.Algorithm,
                InsightMeasurementPhases.AlgorithmSubphase("strongest-path"),
                InsightMeasurementPhases.AlgorithmSubphase("counterfactual-evaluation"),
                InsightMeasurementPhases.Ranking,
                InsightMeasurementPhases.ResultShaping,
                InsightMeasurementPhases.DigestGeneration,
                InsightMeasurementPhases.DtoMapping
            },
            suppliedCollector.Snapshot().Select(timing => timing.Phase).ToArray());
    }

    [TestMethod]
    public async Task Robustness_RecordsContextAlgorithmShapingAndDigestAsDistinctPhases()
    {
        var graph = new Graph
        {
            Id = 1,
            Slug = "fixture",
            Title = "Fixture",
            Nodes =
            [
                new GraphNode
                {
                    Id = "R",
                    Kind = "claim",
                    Title = "Root",
                    BodyText = "Root",
                    PriorOdds = 0m,
                    PosteriorOdds = 0m
                },
                new GraphNode
                {
                    Id = "E",
                    Kind = "evidence",
                    Title = "Evidence",
                    BodyText = "Evidence",
                    PriorOdds = 0m,
                    PosteriorOdds = 0m
                }
            ],
            Edges =
            [
                new GraphEdge
                {
                    Id = "edge-e",
                    From = "E",
                    To = "R",
                    Kind = "support",
                    ImportanceToParent = 2m
                }
            ]
        };
        var repository = new Mock<IGraphRepository>();
        repository.Setup(value => value.GetBySlugAsync("fixture", It.IsAny<CancellationToken>()))
            .ReturnsAsync(graph);
        var collector = new InsightPhaseTimingCollector();
        var service = new GraphService(
            repository.Object,
            new GraphLikelihoodCalculator(),
            collector);

        var result = await service.GetNodeRobustnessRankingAsync(
            "fixture",
            CancellationToken.None);

        Assert.IsNotNull(result);
        CollectionAssert.AreEqual(
            new[]
            {
                InsightMeasurementPhases.Validation,
                InsightMeasurementPhases.CalculationContextConstruction,
                InsightMeasurementPhases.Algorithm,
                InsightMeasurementPhases.AlgorithmSubphase("maximum-path-evaluation"),
                InsightMeasurementPhases.Ranking,
                InsightMeasurementPhases.ResultShaping,
                InsightMeasurementPhases.DigestGeneration,
                InsightMeasurementPhases.DtoMapping
            },
            collector.Snapshot().Select(timing => timing.Phase).ToArray());
    }

    private static Dictionary<string, object?> NodeRow(string id, string kind) => new()
    {
        ["id"] = id,
        ["kind"] = kind,
        ["title"] = id,
        ["BodyText"] = id,
        ["category"] = null,
        ["tags"] = Array.Empty<string>(),
        ["PriorOdds"] = 0m,
        ["PosteriorOdds"] = 0m,
        ["evidence"] = null
    };

    private static Dictionary<string, object?> EdgeRow(
        string id,
        string from,
        string to,
        string kind,
        decimal importanceToParent) => new()
    {
        ["id"] = id,
        ["From"] = from,
        ["To"] = to,
        ["kind"] = kind,
        ["ImportanceToParent"] = importanceToParent
    };

    private static GraphDto SuppliedGraph() => new()
    {
        Slug = "fixture",
        Title = "Fixture",
        Description = "Fixture graph",
        Nodes =
        [
            new GraphNodeDto { Id = "R", Kind = "claim", Title = "R", BodyText = "R" },
            new GraphNodeDto { Id = "E", Kind = "evidence", Title = "E", BodyText = "E" },
            new GraphNodeDto { Id = "O", Kind = "objection", Title = "O", BodyText = "O" }
        ],
        Edges =
        [
            new GraphEdgeDto
            {
                Id = "edge-e",
                From = "E",
                To = "R",
                Kind = "support",
                ImportanceToParent = 2m
            },
            new GraphEdgeDto
            {
                Id = "edge-o",
                From = "O",
                To = "R",
                Kind = "rebut",
                ImportanceToParent = 0.5m
            }
        ]
    };

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
