using Backend.Calculation;
using Backend.Models.Domain;
using Backend.Models.Dto;
using Backend.Repositories;
using Backend.Seeding;
using Backend.Services;
using Moq;

namespace backend.Tests.Services;

[TestClass]
public class GraphServiceTests
{
    [TestMethod]
    public async Task GetSummariesAsync_MapsSummariesWithoutChangingOrder()
    {
        var repositoryMock = new Mock<IGraphRepository>();
        repositoryMock
            .Setup(repository => repository.GetSummariesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(
                new List<GraphSummary>
                {
                    new()
                    {
                        Slug = "sample-medium",
                        Title = "Sample Medium Reasoning Graph",
                        Description = "Seed graph",
                        NodeCount = 18,
                        EdgeCount = 17
                    },
                    new()
                    {
                        Slug = "flat-earth-large",
                        Title = "Large Flat-Earth Reasoning Graph",
                        NodeCount = 105,
                        EdgeCount = 112
                    }
                });

        var service = CreateService(repositoryMock.Object);

        var result = await service.GetSummariesAsync(CancellationToken.None);

        Assert.AreEqual(2, result.Count);
        Assert.AreEqual("sample-medium", result[0].Slug);
        Assert.AreEqual("Sample Medium Reasoning Graph", result[0].Title);
        Assert.AreEqual("Seed graph", result[0].Description);
        Assert.AreEqual(18, result[0].NodeCount);
        Assert.AreEqual(17, result[0].EdgeCount);
        Assert.AreEqual("flat-earth-large", result[1].Slug);
        Assert.AreEqual("Large Flat-Earth Reasoning Graph", result[1].Title);
        Assert.IsNull(result[1].Description);
        Assert.AreEqual(105, result[1].NodeCount);
        Assert.AreEqual(112, result[1].EdgeCount);
    }

    [TestMethod]
    public async Task GetSummariesAsync_ReturnsEmptyList_WhenRepositoryIsEmpty()
    {
        var repositoryMock = new Mock<IGraphRepository>();
        repositoryMock
            .Setup(repository => repository.GetSummariesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<GraphSummary>());

        var service = CreateService(repositoryMock.Object);

        var result = await service.GetSummariesAsync(CancellationToken.None);

        Assert.IsNotNull(result);
        Assert.AreEqual(0, result.Count);
    }

    [TestMethod]
    public async Task ResetDatabaseAsync_DeduplicatesAndUsesCanonicalCatalogOrder()
    {
        var repositoryMock = new Mock<IGraphRepository>();
        var service = CreateService(repositoryMock.Object);

        await service.ResetDatabaseAsync(
            [
                StressGraphSeedIds.SharedDiamond10K,
                StressGraphSeedIds.Balanced1K,
                StressGraphSeedIds.SharedDiamond10K,
                StressGraphSeedIds.Deep1K
            ],
            CancellationToken.None);

        repositoryMock.Verify(repository => repository.ResetDatabaseAsync(
            It.Is<IReadOnlyList<StressGraphSeedSpec>>(specs =>
                specs.Select(spec => spec.Id).SequenceEqual(new[]
                {
                    StressGraphSeedIds.Balanced1K,
                    StressGraphSeedIds.Deep1K,
                    StressGraphSeedIds.SharedDiamond10K
                })),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestMethod]
    public async Task ResetDatabaseAsync_RejectsMixedUnknownSelectionBeforeRepositoryCall()
    {
        var repositoryMock = new Mock<IGraphRepository>();
        var service = CreateService(repositoryMock.Object);

        var exception = await Assert.ThrowsExceptionAsync<InvalidStressGraphSeedSelectionException>(() =>
            service.ResetDatabaseAsync(
                [StressGraphSeedIds.Balanced1K, "stress-unknown"],
                CancellationToken.None));

        CollectionAssert.AreEqual(
            new[] { "stress-unknown" },
            exception.UnknownIds.ToArray());
        repositoryMock.Verify(repository => repository.ResetDatabaseAsync(
            It.IsAny<IReadOnlyList<StressGraphSeedSpec>>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public async Task ResetDatabaseAsync_EmptySelectionInstallsOnlyBaseSeed()
    {
        var repositoryMock = new Mock<IGraphRepository>();
        var service = CreateService(repositoryMock.Object);

        await service.ResetDatabaseAsync([], CancellationToken.None);

        repositoryMock.Verify(repository => repository.ResetDatabaseAsync(
            It.Is<IReadOnlyList<StressGraphSeedSpec>>(specs => specs.Count == 0),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestMethod]
    public async Task GetBySlugAsync_ReturnsNull_WhenRepositoryReturnsNull()
    {
        var repositoryMock = new Mock<IGraphRepository>();

        repositoryMock
            .Setup(repository => repository.GetBySlugAsync("missing", It.IsAny<CancellationToken>()))
            .ReturnsAsync((Graph?)null);

        var service = CreateService(repositoryMock.Object);

        var result = await service.GetBySlugAsync("missing", CancellationToken.None);

        Assert.IsNull(result);
    }

    [TestMethod]
    public async Task GetBySlugAsync_MapsGraphToDtoCorrectly()
    {
        var repositoryMock = new Mock<IGraphRepository>();
        var graph = new Graph
        {
            Id = 1,
            Slug = "sample-medium",
            Title = "Sample Medium Reasoning Graph",
            Description = "Seed graph",
            Nodes =
            [
                new GraphNode
                {
                    Id = "R1",
                    Kind = "root",
                    Title = "Earth is flat",
                    BodyText = "The Earth is flat."
                }
            ],
            Edges =
            [
                new GraphEdge
                {
                    Id = "E-R-C1",
                    From = "C1",
                    To = "R1",
                    Kind = "support",
                    ProbabilityGivenParent = 0.82m,
                    ProbabilityGivenNotParent = 0.18m
                }
            ]
        };

        repositoryMock
            .Setup(repository => repository.GetBySlugAsync("sample-medium", It.IsAny<CancellationToken>()))
            .ReturnsAsync(graph);

        var service = CreateService(repositoryMock.Object);

        var result = await service.GetBySlugAsync("sample-medium", CancellationToken.None);

        Assert.IsNotNull(result);
        Assert.AreEqual(graph.Slug, result.Slug);
        Assert.AreEqual(graph.Title, result.Title);
        Assert.AreEqual(graph.Description, result.Description);
        Assert.AreEqual(1, result.Nodes.Count);
        Assert.AreEqual("R1", result.Nodes[0].Id);
        Assert.AreEqual("root", result.Nodes[0].Kind);
        Assert.AreEqual("Earth is flat", result.Nodes[0].Title);
        Assert.AreEqual("The Earth is flat.", result.Nodes[0].BodyText);
        Assert.AreEqual(1, result.Edges.Count);
        Assert.AreEqual("E-R-C1", result.Edges[0].Id);
        Assert.AreEqual("C1", result.Edges[0].From);
        Assert.AreEqual("R1", result.Edges[0].To);
        Assert.AreEqual("support", result.Edges[0].Kind);
        Assert.AreEqual(0.82m, result.Edges[0].ProbabilityGivenParent);
        Assert.AreEqual(0.18m, result.Edges[0].ProbabilityGivenNotParent);

        var toDomainGraph = typeof(GraphService).GetMethod(
            "ToDomainGraph",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        Assert.IsNotNull(toDomainGraph);

        var roundTrippedGraph = toDomainGraph.Invoke(null, new object?[] { result }) as Graph;
        Assert.IsNotNull(roundTrippedGraph);
        Assert.AreEqual(0.82m, roundTrippedGraph.Edges[0].ProbabilityGivenParent);
        Assert.AreEqual(0.18m, roundTrippedGraph.Edges[0].ProbabilityGivenNotParent);
    }

    [TestMethod]
    public async Task GetBySlugAsync_PassesSlugThroughToRepository()
    {
        var repositoryMock = new Mock<IGraphRepository>();

        repositoryMock
            .Setup(repository => repository.GetBySlugAsync("sample-medium", It.IsAny<CancellationToken>()))
            .ReturnsAsync((Graph?)null);

        var service = CreateService(repositoryMock.Object);

        await service.GetBySlugAsync("sample-medium", CancellationToken.None);

        repositoryMock.Verify(
            repository => repository.GetBySlugAsync("sample-medium", It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [TestMethod]
    public async Task GetMinimalCounterSetAsync_HandlesTargetThatIsACounterNode()
    {
        var repositoryMock = new Mock<IGraphRepository>();
        var graph = GraphWith(
            [Node("O3", kind: "objection")],
            []);

        repositoryMock
            .Setup(repository => repository.GetBySlugAsync("sample-medium", It.IsAny<CancellationToken>()))
            .ReturnsAsync(graph);

        var result = await CreateService(repositoryMock.Object)
            .GetMinimalCounterSetAsync("sample-medium", "O3", CancellationToken.None);

        Assert.IsNull(result);
    }

    [TestMethod]
    public async Task GetMinimalCounterSetAsync_UsesBfImpactToSelectStrongestCounterFirst()
    {
        var repositoryMock = new Mock<IGraphRepository>();
        decimal counterLogBayesFactor = (decimal)Math.Log(4d);
        var graph = GraphWith(
            [
                Node("H", priorOdds: 0.2m),
                Node("weak", kind: "objection", posteriorOdds: counterLogBayesFactor),
                Node("strong", kind: "objection", posteriorOdds: counterLogBayesFactor)
            ],
            [
                Edge(
                    "E-weak-H",
                    "weak",
                    "H",
                    "counter",
                    0.25m,
                    probabilityGivenParent: 0.2m,
                    probabilityGivenNotParent: 0.8m),
                Edge(
                    "E-strong-H",
                    "strong",
                    "H",
                    "counter",
                    0.000000001m,
                    probabilityGivenParent: 0.000000001m,
                    probabilityGivenNotParent: 0.999999999m)
            ]);

        repositoryMock
            .Setup(repository => repository.GetBySlugAsync(
                "sample-medium",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(graph);

        var result = await CreateService(repositoryMock.Object)
            .GetMinimalCounterSetAsync("sample-medium", "H", CancellationToken.None);

        CollectionAssert.AreEqual(new[] { "strong" }, result);
    }

    [TestMethod]
    public async Task GetMinimalCounterSetAsync_AddsRankedCountersUntilBfLogOddsCrossThreshold()
    {
        var repositoryMock = new Mock<IGraphRepository>();
        var graph = GraphWith(
            [
                Node("H", priorOdds: 0.4m),
                Node("third", kind: "objection", posteriorOdds: 0.6m),
                Node("second", kind: "objection", posteriorOdds: 0.7m),
                Node("first", kind: "objection", posteriorOdds: 0.8m)
            ],
            [
                Edge("E-third-H", "third", "H", "counter", 0.000000001m,
                    probabilityGivenParent: 0.000000001m,
                    probabilityGivenNotParent: 0.999999999m),
                Edge("E-second-H", "second", "H", "counter", 0.000000001m,
                    probabilityGivenParent: 0.000000001m,
                    probabilityGivenNotParent: 0.999999999m),
                Edge("E-first-H", "first", "H", "counter", 0.000000001m,
                    probabilityGivenParent: 0.000000001m,
                    probabilityGivenNotParent: 0.999999999m)
            ]);

        repositoryMock
            .Setup(repository => repository.GetBySlugAsync(
                "sample-medium",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(graph);

        var result = await CreateService(repositoryMock.Object)
            .GetMinimalCounterSetAsync("sample-medium", "H", CancellationToken.None);

        CollectionAssert.AreEqual(new[] { "first", "second" }, result);
    }

    [TestMethod]
    public void GetNodeRobustnessRanking_UsesBfProbabilityImpactOfRemovingEvidence()
    {
        decimal evidenceLogBayesFactor = (decimal)Math.Log(4d);
        var graph = GraphWith(
            [
                Node("H", priorOdds: 0m, posteriorOdds: -90m),
                Node(
                    "E",
                    kind: "evidence",
                    posteriorOdds: evidenceLogBayesFactor)
            ],
            [
                Edge(
                    "E-E-H",
                    "E",
                    "H",
                    "support",
                    4m,
                    probabilityGivenParent: 0.8m,
                    probabilityGivenNotParent: 0.2m)
            ]);

        var result = CreateService(new Mock<IGraphRepository>().Object)
            .GetNodeRobustnessRanking(graph);

        decimal expectedTargetLogOdds = TransformLogBayesFactor(
            evidenceLogBayesFactor,
            0.8m,
            0.2m);
        double targetProbability =
            1d / (1d + Math.Exp(-(double)expectedTargetLogOdds));
        decimal expectedRobustness =
            (decimal)Math.Exp(-Math.Abs(targetProbability - 0.5d));

        CollectionAssert.AreEqual(
            new[] { "H", "E" },
            result.Select(entry => entry.NodeId).ToArray());
        Assert.IsTrue(Approximately(result[0].Robustness, expectedRobustness));
        Assert.AreEqual(1m, result[1].Robustness);
    }

    [TestMethod]
    public async Task AddNodeAsync_RootClaimResetsStalePosteriorToPrior()
    {
        var repositoryMock = new Mock<IGraphRepository>();
        var node = new GraphNodeDto
        {
            Id = "A",
            Kind = "claim",
            Title = "A",
            BodyText = "A",
            PriorOdds = 0.7m,
            PosteriorOdds = 9m
        };
        var graph = GraphWith(
            [Node("A", 0.7m, "claim", 9m)],
            []);

        repositoryMock
            .Setup(repository => repository.AddNodeAsync(
                "sample-medium",
                node,
                null,
                "support",
                0.5m,
                0.5m,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        repositoryMock
            .Setup(repository => repository.GetBySlugAsync(
                "sample-medium",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(graph);

        var result = await CreateService(repositoryMock.Object).AddNodeAsync(
            "sample-medium",
            node,
            cancellationToken: CancellationToken.None);

        Assert.IsTrue(result);
        repositoryMock.Verify(
            repository => repository.GetBySlugAsync(
                "sample-medium",
                It.IsAny<CancellationToken>()),
            Times.Once);
        VerifyBatch(repositoryMock, graph.Id, expected =>
            expected.Count == 1 && expected["A"] == 0.7m);
    }

    [TestMethod]
    public async Task AddNodeAsync_ClaimWithParentResetsStalePosteriorAndRecalculatesParent()
    {
        var repositoryMock = new Mock<IGraphRepository>();
        var node = new GraphNodeDto
        {
            Id = "B",
            Kind = "claim",
            Title = "B",
            BodyText = "B",
            PriorOdds = 0.6m,
            PosteriorOdds = 9m
        };
        var graph = GraphWith(
            [
                Node("A", 0.4m, "claim", 8m),
                Node("B", 0.6m, "claim", 9m)
            ],
            [Edge("E-B-A", "B", "A", "support", 1m, 0.8m, 0.2m)]);

        repositoryMock
            .Setup(repository => repository.AddNodeAsync(
                "sample-medium",
                node,
                "A",
                "support",
                0.8m,
                0.2m,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        repositoryMock
            .Setup(repository => repository.GetBySlugAsync(
                "sample-medium",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(graph);

        var result = await CreateService(repositoryMock.Object).AddNodeAsync(
            "sample-medium",
            node,
            "A",
            "support",
            0.8m,
            0.2m,
            CancellationToken.None);

        Assert.IsTrue(result);
        VerifyBatch(repositoryMock, graph.Id, expected =>
            expected.Count == 2 &&
            expected["B"] == 0.6m &&
            expected["A"] == 0.4m);
    }

    [TestMethod]
    public async Task AddNodeAsync_EvidenceWithParentPreservesAuthoredPosteriorAndRecalculatesParent()
    {
        var repositoryMock = new Mock<IGraphRepository>();
        decimal leafLogBayesFactor = (decimal)Math.Log(4d);
        var node = new GraphNodeDto
        {
            Id = "B",
            Kind = "evidence",
            Title = "B",
            BodyText = "B",
            PriorOdds = 5m,
            PosteriorOdds = leafLogBayesFactor
        };
        var graph = GraphWith(
            [
                Node("A", 0.4m),
                Node("B", 0m, "evidence", leafLogBayesFactor)
            ],
            [Edge("E-B-A", "B", "A", "support", 1m, 0.8m, 0.2m)]);

        repositoryMock
            .Setup(repository => repository.AddNodeAsync(
                "sample-medium",
                node,
                "A",
                "support",
                0.8m,
                0.2m,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        repositoryMock
            .Setup(repository => repository.GetBySlugAsync(
                "sample-medium",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(graph);

        var result = await CreateService(repositoryMock.Object).AddNodeAsync(
            "sample-medium",
            node,
            "A",
            "support",
            0.8m,
            0.2m,
            CancellationToken.None);

        Assert.IsTrue(result);
        Assert.AreEqual(0m, node.PriorOdds);
        decimal expectedParentPosterior = 0.4m + TransformLogBayesFactor(
            leafLogBayesFactor,
            0.8m,
            0.2m);
        VerifyBatch(repositoryMock, graph.Id, expected =>
            expected.Count == 2 &&
            Approximately(expected["B"], leafLogBayesFactor) &&
            Approximately(expected["A"], expectedParentPosterior));
    }

    [TestMethod]
    public async Task UpdateNodeAsync_PassesUpdateThroughToRepository()
    {
        var repositoryMock = new Mock<IGraphRepository>();
        var update = new GraphNodeUpdateDto
        {
            Kind = "claim",
            Title = "Updated title",
            BodyText = "Updated body",
            PriorOdds = 0.75m
        };

        repositoryMock
            .Setup(repository => repository.UpdateNodeAsync(
                "sample-medium",
                "P1",
                update,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var service = CreateService(repositoryMock.Object);

        var result = await service.UpdateNodeAsync("sample-medium", "P1", update, CancellationToken.None);

        Assert.IsTrue(result);
        repositoryMock.Verify(
            repository => repository.UpdateNodeAsync("sample-medium", "P1", update, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [TestMethod]
    public async Task UpdateNodeAsync_KindOnlyChangeRecalculatesNodeAndAncestors()
    {
        var repositoryMock = new Mock<IGraphRepository>();
        var update = new GraphNodeUpdateDto { Kind = "claim" };
        var graph = GraphWith(
            [
                Node("A", 0.4m, "claim", 8m),
                Node("B", 0.6m, "claim", 9m)
            ],
            [Edge("E-B-A", "B", "A", "support", 1m, 0.8m, 0.2m)]);

        repositoryMock
            .Setup(repository => repository.UpdateNodeAsync(
                "sample-medium",
                "B",
                update,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        repositoryMock
            .Setup(repository => repository.GetBySlugAsync(
                "sample-medium",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(graph);

        var result = await CreateService(repositoryMock.Object)
            .UpdateNodeAsync("sample-medium", "B", update);

        Assert.IsTrue(result);
        repositoryMock.Verify(
            repository => repository.GetBySlugAsync(
                "sample-medium",
                It.IsAny<CancellationToken>()),
            Times.Once);
        VerifyBatch(repositoryMock, graph.Id, expected =>
            expected.Count == 2 &&
            expected["B"] == 0.6m &&
            expected["A"] == 0.4m);
    }

    [TestMethod]
    public async Task UpdateNodeAsync_RecalculatesParentAsPriorPlusLogBayesFactor()
    {
        var repositoryMock = new Mock<IGraphRepository>();
        decimal leafLogBayesFactor = (decimal)Math.Log(4d);
        const decimal probabilityGivenParent = 0.8m;
        const decimal probabilityGivenNotParent = 0.2m;
        var graph = GraphWith(
            [
                Node("A", 0.4m),
                Node("B", 0m, "evidence", leafLogBayesFactor)
            ],
            [
                Edge(
                    "E-B-A",
                    "B",
                    "A",
                    "support",
                    10m,
                    probabilityGivenParent,
                    probabilityGivenNotParent)
            ]);
        var update = new GraphNodeUpdateDto { PriorOdds = 0m };

        repositoryMock
            .Setup(repository => repository.UpdateNodeAsync("sample-medium", "B", update, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        repositoryMock
            .Setup(repository => repository.GetBySlugAsync("sample-medium", It.IsAny<CancellationToken>()))
            .ReturnsAsync(graph);

        var result = await CreateService(repositoryMock.Object).UpdateNodeAsync("sample-medium", "B", update);

        Assert.IsTrue(result);
        decimal expectedPosterior = 0.4m + TransformLogBayesFactor(
            leafLogBayesFactor,
            probabilityGivenParent,
            probabilityGivenNotParent);
        VerifyBatch(repositoryMock, graph.Id, expected =>
            expected.Count == 2 &&
            Approximately(expected["B"], leafLogBayesFactor) &&
            Approximately(expected["A"], expectedPosterior));
    }

    [TestMethod]
    public async Task UpdateNodeAsync_RecalculatesParentUsingSiblings()
    {
        var repositoryMock = new Mock<IGraphRepository>();
        decimal firstLeafLogBayesFactor = (decimal)Math.Log(4d);
        decimal secondLeafLogBayesFactor = (decimal)Math.Log(2d);
        var graph = GraphWith(
            [
                Node("B", 0.3m),
                Node("E", 0m, "evidence", firstLeafLogBayesFactor),
                Node("F", 0m, "evidence", secondLeafLogBayesFactor)
            ],
            [
                Edge("E-E-B", "E", "B", "support", 10m, 0.8m, 0.2m),
                Edge("E-F-B", "F", "B", "support", 10m, 0.7m, 0.4m)
            ]);
        var update = new GraphNodeUpdateDto { PriorOdds = 0m };

        repositoryMock
            .Setup(repository => repository.UpdateNodeAsync("sample-medium", "F", update, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        repositoryMock
            .Setup(repository => repository.GetBySlugAsync("sample-medium", It.IsAny<CancellationToken>()))
            .ReturnsAsync(graph);

        var result = await CreateService(repositoryMock.Object).UpdateNodeAsync("sample-medium", "F", update);

        Assert.IsTrue(result);
        decimal expectedPosterior = 0.3m +
            TransformLogBayesFactor(firstLeafLogBayesFactor, 0.8m, 0.2m) +
            TransformLogBayesFactor(secondLeafLogBayesFactor, 0.7m, 0.4m);
        VerifyBatch(repositoryMock, graph.Id, expected =>
            expected.Count == 2 &&
            Approximately(expected["F"], secondLeafLogBayesFactor) &&
            Approximately(expected["B"], expectedPosterior));
    }

    [TestMethod]
    public async Task UpdateNodeAsync_RecalculatesAncestors()
    {
        var repositoryMock = new Mock<IGraphRepository>();
        decimal leafLogBayesFactor = (decimal)Math.Log(4d);
        var graph = GraphWith(
            [
                Node("A", 0.4m),
                Node("B", 0.3m),
                Node("F", 0m, "evidence", leafLogBayesFactor)
            ],
            [
                Edge("E-F-B", "F", "B", "support", 10m, 0.8m, 0.2m),
                Edge("E-B-A", "B", "A", "support", 10m, 0.7m, 0.3m)
            ]);
        var update = new GraphNodeUpdateDto { PriorOdds = 0m };

        repositoryMock
            .Setup(repository => repository.UpdateNodeAsync("sample-medium", "F", update, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        repositoryMock
            .Setup(repository => repository.GetBySlugAsync("sample-medium", It.IsAny<CancellationToken>()))
            .ReturnsAsync(graph);

        var result = await CreateService(repositoryMock.Object).UpdateNodeAsync("sample-medium", "F", update);

        Assert.IsTrue(result);
        decimal intermediateLogBayesFactor = TransformLogBayesFactor(
            leafLogBayesFactor,
            0.8m,
            0.2m);
        decimal expectedIntermediatePosterior = 0.3m + intermediateLogBayesFactor;
        decimal expectedRootPosterior = 0.4m + TransformLogBayesFactor(
            intermediateLogBayesFactor,
            0.7m,
            0.3m);
        VerifyBatch(repositoryMock, graph.Id, expected =>
            expected.Count == 3 &&
            Approximately(expected["F"], leafLogBayesFactor) &&
            Approximately(expected["B"], expectedIntermediatePosterior) &&
            Approximately(expected["A"], expectedRootPosterior));
    }

    [TestMethod]
    public async Task UpdateNodeAsync_InternalClaimPriorUpdatePersistsClaimAndAncestors()
    {
        var repositoryMock = new Mock<IGraphRepository>();
        decimal leafLogBayesFactor = (decimal)Math.Log(4d);
        var graph = GraphWith(
            [
                Node("A", 0.4m),
                Node("B", 0.6m),
                Node("F", 0m, "evidence", leafLogBayesFactor)
            ],
            [
                Edge("E-F-B", "F", "B", "support", 10m, 0.8m, 0.2m),
                Edge("E-B-A", "B", "A", "support", 10m, 0.7m, 0.3m)
            ]);
        var update = new GraphNodeUpdateDto { PriorOdds = 0.6m };

        repositoryMock
            .Setup(repository => repository.UpdateNodeAsync(
                "sample-medium",
                "B",
                update,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        repositoryMock
            .Setup(repository => repository.GetBySlugAsync(
                "sample-medium",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(graph);

        var result = await CreateService(repositoryMock.Object)
            .UpdateNodeAsync("sample-medium", "B", update);

        Assert.IsTrue(result);
        decimal intermediateLogBayesFactor = TransformLogBayesFactor(
            leafLogBayesFactor,
            0.8m,
            0.2m);
        decimal expectedIntermediatePosterior = 0.6m + intermediateLogBayesFactor;
        decimal expectedRootPosterior = 0.4m + TransformLogBayesFactor(
            intermediateLogBayesFactor,
            0.7m,
            0.3m);
        VerifyBatch(repositoryMock, graph.Id, expected =>
            expected.Count == 2 &&
            Approximately(expected["B"], expectedIntermediatePosterior) &&
            Approximately(expected["A"], expectedRootPosterior));
    }

    [TestMethod]
    public async Task UpdateEdgeAsync_RecalculatesParentAfterConditionalProbabilityUpdate()
    {
        var repositoryMock = new Mock<IGraphRepository>();
        decimal leafLogBayesFactor = (decimal)Math.Log(4d);
        var graph = GraphWith(
            [
                Node("A", 0.4m),
                Node("B", 0m, "evidence", leafLogBayesFactor)
            ],
            [Edge("E-B-A", "B", "A", "support", 10m, 0.8m, 0.2m)]);
        var update = new GraphEdgeUpdateDto { ProbabilityGivenParent = 0.8m };

        repositoryMock
            .Setup(repository => repository.UpdateEdgeAsync("sample-medium", "E-B-A", update, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        repositoryMock
            .Setup(repository => repository.GetBySlugAsync("sample-medium", It.IsAny<CancellationToken>()))
            .ReturnsAsync(graph);

        var result = await CreateService(repositoryMock.Object).UpdateEdgeAsync("sample-medium", "E-B-A", update);

        Assert.IsTrue(result);
        decimal expectedPosterior = 0.4m + TransformLogBayesFactor(
            leafLogBayesFactor,
            0.8m,
            0.2m);
        VerifyBatch(repositoryMock, graph.Id, expected =>
            expected.Count == 1 && Approximately(expected["A"], expectedPosterior));
    }

    [TestMethod]
    public async Task UpdateEdgeAsync_ProbabilityOnlyUpdateRecalculatesAndPersistsPosteriorOdds()
    {
        var repositoryMock = new Mock<IGraphRepository>();
        decimal leafLogBayesFactor = (decimal)Math.Log(4d);
        var graph = GraphWith(
            [
                Node("A", 0.4m),
                Node("B", 0m, "evidence", leafLogBayesFactor)
            ],
            [Edge("E-B-A", "B", "A", "support", 1m, 0.8m, 0.2m)]);
        var update = new GraphEdgeUpdateDto
        {
            ProbabilityGivenParent = 0.8m,
            ProbabilityGivenNotParent = 0.2m
        };

        repositoryMock
            .Setup(repository => repository.UpdateEdgeAsync(
                "sample-medium",
                "E-B-A",
                update,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        repositoryMock
            .Setup(repository => repository.GetBySlugAsync(
                "sample-medium",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(graph);

        var result = await CreateService(repositoryMock.Object)
            .UpdateEdgeAsync("sample-medium", "E-B-A", update);

        Assert.IsTrue(result);
        repositoryMock.Verify(
            repository => repository.GetBySlugAsync(
                "sample-medium",
                It.IsAny<CancellationToken>()),
            Times.Once);
        decimal expectedPosterior = 0.4m + TransformLogBayesFactor(
            leafLogBayesFactor,
            0.8m,
            0.2m);
        VerifyBatch(repositoryMock, graph.Id, expected =>
            expected.Count == 1 && Approximately(expected["A"], expectedPosterior));
    }

    [TestMethod]
    public async Task UpdateNodeAsync_PosteriorOnlyUpdateRecalculatesParent()
    {
        var repositoryMock = new Mock<IGraphRepository>();
        decimal leafLogBayesFactor = (decimal)Math.Log(4d);
        var graph = GraphWith(
            [
                Node("A", 0.4m),
                Node("B", 0m, "evidence", leafLogBayesFactor)
            ],
            [Edge("E-B-A", "B", "A", "support", 1m, 0.8m, 0.2m)]);
        var update = new GraphNodeUpdateDto
        {
            PosteriorOdds = leafLogBayesFactor
        };

        repositoryMock
            .Setup(repository => repository.UpdateNodeAsync(
                "sample-medium",
                "B",
                update,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        repositoryMock
            .Setup(repository => repository.GetBySlugAsync(
                "sample-medium",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(graph);

        var result = await CreateService(repositoryMock.Object)
            .UpdateNodeAsync("sample-medium", "B", update);

        Assert.IsTrue(result);
        decimal expectedPosterior = 0.4m + TransformLogBayesFactor(
            leafLogBayesFactor,
            0.8m,
            0.2m);
        VerifyBatch(repositoryMock, graph.Id, expected =>
            expected.Count == 2 &&
            Approximately(expected["B"], leafLogBayesFactor) &&
            Approximately(expected["A"], expectedPosterior));
    }

    [TestMethod]
    public async Task UpdateNodeAsync_RootWithoutEvidencePersistsPriorAsPosterior()
    {
        var repositoryMock = new Mock<IGraphRepository>();
        var graph = GraphWith([Node("A", 1m, "claim", 9m)], []);
        var update = new GraphNodeUpdateDto { PriorOdds = 1m };

        repositoryMock
            .Setup(repository => repository.UpdateNodeAsync("sample-medium", "A", update, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        repositoryMock
            .Setup(repository => repository.GetBySlugAsync("sample-medium", It.IsAny<CancellationToken>()))
            .ReturnsAsync(graph);

        var result = await CreateService(repositoryMock.Object).UpdateNodeAsync("sample-medium", "A", update);

        Assert.IsTrue(result);
        VerifyBatch(repositoryMock, graph.Id, expected =>
            expected.Count == 1 && expected["A"] == 1m);
    }

    [TestMethod]
    public async Task UpdateNodeAsync_UsesBayesFactorBelowOneForCounterImpact()
    {
        var repositoryMock = new Mock<IGraphRepository>();
        decimal leafLogBayesFactor = (decimal)Math.Log(4d);
        var graph = GraphWith(
            [
                Node("A", 0.25m),
                Node("B", 0m, "objection", leafLogBayesFactor)
            ],
            [Edge("E-B-A", "B", "A", "rebut", 0.1m, 0.2m, 0.8m)]);
        var update = new GraphNodeUpdateDto { PosteriorOdds = leafLogBayesFactor };

        repositoryMock
            .Setup(repository => repository.UpdateNodeAsync("sample-medium", "B", update, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        repositoryMock
            .Setup(repository => repository.GetBySlugAsync("sample-medium", It.IsAny<CancellationToken>()))
            .ReturnsAsync(graph);

        var result = await CreateService(repositoryMock.Object).UpdateNodeAsync("sample-medium", "B", update);

        Assert.IsTrue(result);
        decimal expectedPosterior = 0.25m + TransformLogBayesFactor(
            leafLogBayesFactor,
            0.2m,
            0.8m);
        VerifyBatch(repositoryMock, graph.Id, expected =>
            expected.Count == 2 &&
            Approximately(expected["B"], leafLogBayesFactor) &&
            Approximately(expected["A"], expectedPosterior));
    }

    [TestMethod]
    public async Task DeleteNodeAsync_RecalculatesParentFromRemainingChildren()
    {
        var repositoryMock = new Mock<IGraphRepository>();
        decimal remainingLeafLogBayesFactor = (decimal)Math.Log(2d);
        var graphBeforeDelete = GraphWith(
            [
                Node("A", 0.4m),
                Node("B", 0m, "evidence", (decimal)Math.Log(4d)),
                Node("C", 0m, "evidence", remainingLeafLogBayesFactor)
            ],
            [
                Edge("E-B-A", "B", "A", "support", 10m, 0.8m, 0.2m),
                Edge("E-C-A", "C", "A", "support", 10m, 0.7m, 0.4m)
            ]);
        var graphAfterDelete = GraphWith(
            [
                Node("A", 0.4m),
                Node("C", 0m, "evidence", remainingLeafLogBayesFactor)
            ],
            [Edge("E-C-A", "C", "A", "support", 10m, 0.7m, 0.4m)]);

        repositoryMock
            .SetupSequence(repository => repository.GetBySlugAsync("sample-medium", It.IsAny<CancellationToken>()))
            .ReturnsAsync(graphBeforeDelete)
            .ReturnsAsync(graphAfterDelete);
        repositoryMock
            .Setup(repository => repository.DeleteNodeAsync("sample-medium", "B", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var result = await CreateService(repositoryMock.Object).DeleteNodeAsync("sample-medium", "B");

        Assert.IsTrue(result);
        decimal expectedPosterior = 0.4m + TransformLogBayesFactor(
            remainingLeafLogBayesFactor,
            0.7m,
            0.4m);
        VerifyBatch(repositoryMock, graphAfterDelete.Id, expected =>
            expected.Count == 1 && Approximately(expected["A"], expectedPosterior));
    }

    [TestMethod]
    public async Task DeleteNodeAsync_ResetsParentPosteriorToPriorWhenNoChildrenRemain()
    {
        var repositoryMock = new Mock<IGraphRepository>();
        var graphBeforeDelete = GraphWith(
            [
                Node("A", 1m),
                Node("B", 1m)
            ],
            [Edge("E-B-A", "B", "A", "support", 10)]);
        var graphAfterDelete = GraphWith([Node("A", 1m)], []);

        repositoryMock
            .SetupSequence(repository => repository.GetBySlugAsync("sample-medium", It.IsAny<CancellationToken>()))
            .ReturnsAsync(graphBeforeDelete)
            .ReturnsAsync(graphAfterDelete);
        repositoryMock
            .Setup(repository => repository.DeleteNodeAsync("sample-medium", "B", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var result = await CreateService(repositoryMock.Object).DeleteNodeAsync("sample-medium", "B");

        Assert.IsTrue(result);
        VerifyBatch(repositoryMock, graphAfterDelete.Id, expected =>
            expected.Count == 1 && expected["A"] == 1m);
    }

    [TestMethod]
    public async Task DeleteNodeAsync_RecalculatesAncestorsAfterParentChanges()
    {
        var repositoryMock = new Mock<IGraphRepository>();
        var graphBeforeDelete = GraphWith(
            [
                Node("A", 1m),
                Node("B", 1m),
                Node("F", 1m)
            ],
            [
                Edge("E-F-B", "F", "B", "support", 10),
                Edge("E-B-A", "B", "A", "support", 10)
            ]);
        var graphAfterDelete = GraphWith(
            [
                Node("A", 1m),
                Node("B", 1m)
            ],
            [Edge("E-B-A", "B", "A", "support", 10)]);

        repositoryMock
            .SetupSequence(repository => repository.GetBySlugAsync("sample-medium", It.IsAny<CancellationToken>()))
            .ReturnsAsync(graphBeforeDelete)
            .ReturnsAsync(graphAfterDelete);
        repositoryMock
            .Setup(repository => repository.DeleteNodeAsync("sample-medium", "F", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var result = await CreateService(repositoryMock.Object).DeleteNodeAsync("sample-medium", "F");

        Assert.IsTrue(result);
        VerifyBatch(repositoryMock, graphAfterDelete.Id, expected =>
            expected.Count == 2 && expected["B"] == 1m && expected["A"] == 1m);
    }

    private static GraphService CreateService(IGraphRepository repository)
    {
        return new GraphService(repository, new GraphLikelihoodCalculator());
    }

    private static Graph GraphWith(
        List<GraphNode> nodes,
        List<GraphEdge> edges)
    {
        return new Graph
        {
            Id = 10,
            Slug = "sample-medium",
            Title = "Sample",
            Nodes = nodes,
            Edges = edges
        };
    }

    private static GraphNode Node(
        string id,
        decimal priorOdds = 0m,
        string kind = "claim",
        decimal? posteriorOdds = null)
    {
        return new GraphNode
        {
            Id = id,
            Kind = kind,
            Title = id,
            BodyText = id,
            PriorOdds = priorOdds,
            PosteriorOdds = posteriorOdds ?? priorOdds
        };
    }

    private static GraphEdge Edge(
        string id,
        string from,
        string to,
        string kind,
        decimal likelihoodRatio,
        decimal probabilityGivenParent = 0.5m,
        decimal probabilityGivenNotParent = 0.5m)
    {
        bool hasExplicitProbabilities =
            probabilityGivenParent != 0.5m ||
            probabilityGivenNotParent != 0.5m;

        return new GraphEdge
        {
            Id = id,
            From = from,
            To = to,
            Kind = kind,
            ProbabilityGivenParent = hasExplicitProbabilities
                ? probabilityGivenParent
                : likelihoodRatio >= 1m ? 1m : likelihoodRatio,
            ProbabilityGivenNotParent = hasExplicitProbabilities
                ? probabilityGivenNotParent
                : likelihoodRatio >= 1m ? 1m / likelihoodRatio : 1m
        };
    }

    [TestMethod]
    public async Task GetEvidenceImpactRankingAsync_SplitsAndSortsSupportingAndCounterEvidence()
    {
        var repositoryMock = new Mock<IGraphRepository>();
        var graph = GraphWith(
            [
                Node("R"),
                Node("E1", kind: "evidence", posteriorOdds: (decimal)Math.Log(2d)),
                Node("E2", kind: "evidence", posteriorOdds: (decimal)Math.Log(4d)),
                Node("O1", kind: "objection", posteriorOdds: (decimal)Math.Log(2d)),
                Node("O2", kind: "objection", posteriorOdds: (decimal)Math.Log(4d))
            ],
            [
                Edge("E-E1-R", "E1", "R", "support", 1m, 0.8m, 0.2m),
                Edge("E-E2-R", "E2", "R", "support", 1m, 0.8m, 0.2m),
                Edge("E-O1-R", "O1", "R", "rebut", 1m, 0.2m, 0.8m),
                Edge("E-O2-R", "O2", "R", "rebut", 1m, 0.2m, 0.8m)
            ]);

        repositoryMock
            .Setup(repository => repository.GetBySlugAsync("sample-medium", It.IsAny<CancellationToken>()))
            .ReturnsAsync(graph);

        var result = await CreateService(repositoryMock.Object)
            .GetEvidenceImpactRankingAsync("sample-medium", "R", CancellationToken.None);

        Assert.IsNotNull(result);
        CollectionAssert.AreEqual(
            new[] { "E2", "E1" },
            result.SupportingEvidence.Select(impact => impact.NodeId).ToArray());
        CollectionAssert.AreEqual(
            new[] { "O2", "O1" },
            result.CounterEvidence.Select(impact => impact.NodeId).ToArray());
        Assert.IsTrue(result.SupportingEvidence.All(impact => impact.ProbabilityDifference > 0d));
        Assert.IsTrue(result.CounterEvidence.All(impact => impact.ProbabilityDifference < 0d));
        decimal strongestImpact = TransformLogBayesFactor(
            (decimal)Math.Log(4d),
            0.8m,
            0.2m);
        Assert.IsTrue(Approximately(result.SupportingEvidence[0].LogLr, strongestImpact));
        Assert.IsTrue(Approximately(result.CounterEvidence[0].LogLr, -strongestImpact));
    }

    [TestMethod]
    public void GetEvidenceImpactRanking_RanksEvidenceAcrossBranchingPaths()
    {
        var graph = GraphWith(
            [
                Node("A"),
                Node("B1"),
                Node("B2"),
                Node("C1", kind: "evidence", posteriorOdds: (decimal)Math.Log(2d)),
                Node("C2", kind: "objection", posteriorOdds: (decimal)Math.Log(3d)),
                Node("C3", kind: "evidence", posteriorOdds: (decimal)Math.Log(4d))
            ],
            [
                Edge("E-B1-A", "B1", "A", "support", 1m, 0.7m, 0.3m),
                Edge("E-B2-A", "B2", "A", "support", 1m, 0.6m, 0.4m),
                Edge("E-C1-B1", "C1", "B1", "support", 1m, 0.8m, 0.2m),
                Edge("E-C2-B1", "C2", "B1", "objection", 1m, 0.2m, 0.8m),
                Edge("E-C2-B2", "C2", "B2", "objection", 1m, 0.3m, 0.7m),
                Edge("E-C3-B2", "C3", "B2", "support", 1m, 0.75m, 0.25m)
            ]);

        var calculator = new GraphPosteriorOddsCalculator();

        var result = calculator.GetEvidenceImpactRanking(graph, "A", CancellationToken.None);

        CollectionAssert.AreEqual(
            new[] { "C1", "C3" },
            result.SupportingEvidence.Select(impact => impact.NodeId).ToArray());
        CollectionAssert.AreEqual(
            new[] { "C2" },
            result.CounterEvidence.Select(impact => impact.NodeId).ToArray());

        var c3 = result.SupportingEvidence.Single(impact => impact.NodeId == "C3");
        var c1 = result.SupportingEvidence.Single(impact => impact.NodeId == "C1");
        var counter = result.CounterEvidence.Single();

        Assert.AreEqual(2, result.SupportingEvidence.Count);
        decimal c1AtB1 = TransformLogBayesFactor((decimal)Math.Log(2d), 0.8m, 0.2m);
        decimal c2AtB1 = TransformLogBayesFactor((decimal)Math.Log(3d), 0.2m, 0.8m);
        decimal b1WithBoth = TransformLogBayesFactor(c1AtB1 + c2AtB1, 0.7m, 0.3m);
        decimal b1WithoutC1 = TransformLogBayesFactor(c2AtB1, 0.7m, 0.3m);
        decimal b1WithoutC2 = TransformLogBayesFactor(c1AtB1, 0.7m, 0.3m);
        decimal c3AtB2 = TransformLogBayesFactor((decimal)Math.Log(4d), 0.75m, 0.25m);
        decimal b2WithC3 = TransformLogBayesFactor(c3AtB2, 0.6m, 0.4m);

        Assert.IsTrue(Approximately(c3.LogLr, b2WithC3));
        Assert.IsTrue(Approximately(c1.LogLr, b1WithBoth - b1WithoutC1));
        Assert.IsTrue(Approximately(counter.LogLr, b1WithBoth - b1WithoutC2));
        Assert.IsTrue(result.SupportingEvidence.All(impact => impact.ProbabilityDifference > 0d));
        Assert.IsTrue(counter.ProbabilityDifference < 0d);
    }

    [TestMethod]
    public void GetEvidenceImpactRanking_GroupsByNodeKindRatherThanImpactDirection()
    {
        decimal leafLogBayesFactor = (decimal)Math.Log(4d);
        var graph = GraphWith(
            [
                Node("H"),
                Node("E", kind: "evidence", posteriorOdds: leafLogBayesFactor),
                Node("O", kind: "objection", posteriorOdds: leafLogBayesFactor)
            ],
            [
                Edge("E-E-H", "E", "H", "rebut", 1m, 0.2m, 0.8m),
                Edge("E-O-H", "O", "H", "support", 1m, 0.8m, 0.2m)
            ]);

        var result = new GraphPosteriorOddsCalculator()
            .GetEvidenceImpactRanking(graph, "H", CancellationToken.None);

        CollectionAssert.AreEqual(
            new[] { "E" },
            result.SupportingEvidence.Select(impact => impact.NodeId).ToArray());
        CollectionAssert.AreEqual(
            new[] { "O" },
            result.CounterEvidence.Select(impact => impact.NodeId).ToArray());
        Assert.IsTrue(result.SupportingEvidence.Single().ProbabilityDifference < 0d);
        Assert.IsTrue(result.CounterEvidence.Single().ProbabilityDifference > 0d);
    }

    [TestMethod]
    public void GetEvidenceImpactRanking_ThrowsWhenTargetDoesNotExist()
    {
        var graph = GraphWith([Node("A")], []);
        var calculator = new GraphPosteriorOddsCalculator();

        var exception = Assert.ThrowsException<InvalidOperationException>(() =>
            calculator.GetEvidenceImpactRanking(graph, "missing", CancellationToken.None));

        StringAssert.Contains(exception.Message, "missing");
    }

    [TestMethod]
    public void GetEvidenceImpactRanking_ThrowsWhenCancelled()
    {
        var graph = GraphWith([Node("A")], []);
        var calculator = new GraphPosteriorOddsCalculator();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.ThrowsException<OperationCanceledException>(() =>
            calculator.GetEvidenceImpactRanking(graph, "A", cancellation.Token));
    }

    private static bool Approximately(decimal actual, decimal expected, decimal tolerance = 0.000001m)
    {
        return Math.Abs(actual - expected) <= tolerance;
    }

    private static decimal TransformLogBayesFactor(
        decimal childLogBayesFactor,
        decimal probabilityGivenParent,
        decimal probabilityGivenNotParent)
    {
        double childBayesFactor = Math.Exp((double)childLogBayesFactor);
        double numerator =
            childBayesFactor * (double)probabilityGivenParent +
            (1d - (double)probabilityGivenParent);
        double denominator =
            childBayesFactor * (double)probabilityGivenNotParent +
            (1d - (double)probabilityGivenNotParent);

        return (decimal)Math.Log(numerator / denominator);
    }

    private static void VerifyBatch(
        Mock<IGraphRepository> repositoryMock,
        int graphId,
        Func<IReadOnlyDictionary<string, decimal>, bool> matches)
    {
        repositoryMock.Verify(
            repository => repository.UpdateNodePosteriorOddsBatchAsync(
                graphId,
                It.Is<IReadOnlyDictionary<string, decimal>>(values => matches(values)),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
