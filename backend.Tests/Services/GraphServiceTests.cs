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
                    Kind = "support"
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
    [DoNotParallelize]
    public async Task GetMinimalCounterSetAsync_PreservesLegacyResultWithoutConsoleOutput()
    {
        var repositoryMock = new Mock<IGraphRepository>();
        var graph = GraphWith(
            [
                Node("target"),
                Node("counter", -2m, "objection")
            ],
            [Edge("edge-counter", "counter", "target", "rebut", 0.1m)]);
        repositoryMock
            .Setup(repository => repository.GetBySlugAsync("sample-medium", It.IsAny<CancellationToken>()))
            .ReturnsAsync(graph);
        var originalOut = Console.Out;
        using var capturedOut = new StringWriter();

        try
        {
            Console.SetOut(capturedOut);
            var result = await CreateService(repositoryMock.Object)
                .GetMinimalCounterSetAsync("sample-medium", "target", CancellationToken.None);

            Assert.IsNotNull(result);
            Assert.AreEqual(0, result.Count,
                "The compatibility adapter must retain the characterized v0 baseline defect.");
            Assert.AreEqual(string.Empty, capturedOut.ToString());
        }
        finally
        {
            Console.SetOut(originalOut);
        }
    }

    [TestMethod]
    public async Task GetMinimalCounterSetAsync_ObservesCancellationBeforeLegacyCalculation()
    {
        var repositoryMock = new Mock<IGraphRepository>();
        repositoryMock
            .Setup(repository => repository.GetBySlugAsync("sample-medium", It.IsAny<CancellationToken>()))
            .ReturnsAsync(GraphWith([Node("target")], []));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsExceptionAsync<OperationCanceledException>(() =>
            CreateService(repositoryMock.Object)
                .GetMinimalCounterSetAsync("sample-medium", "target", cancellation.Token));
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
    public async Task UpdateNodeAsync_RecalculatesParentLogOdds()
    {
        var repositoryMock = new Mock<IGraphRepository>();
        var graph = GraphWith(
            [
                Node("A"),
                Node("B", 1m, "evidence")
            ],
            [Edge("E-B-A", "B", "A", "support", 10)]);
        var update = new GraphNodeUpdateDto { PriorOdds = 1m };

        repositoryMock
            .Setup(repository => repository.UpdateNodeAsync("sample-medium", "B", update, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        repositoryMock
            .Setup(repository => repository.GetBySlugAsync("sample-medium", It.IsAny<CancellationToken>()))
            .ReturnsAsync(graph);

        var result = await CreateService(repositoryMock.Object).UpdateNodeAsync("sample-medium", "B", update);

        Assert.IsTrue(result);
        VerifyBatch(repositoryMock, graph.Id, expected =>
            expected.Count == 1 && Approximately(expected["A"], (decimal)Math.Log(10d)));
    }

    [TestMethod]
    public async Task UpdateNodeAsync_RecalculatesParentUsingSiblings()
    {
        var repositoryMock = new Mock<IGraphRepository>();
        var graph = GraphWith(
            [
                Node("B"),
                Node("E", 1m, "evidence"),
                Node("F", -0.5m, "evidence")
            ],
            [
                Edge("E-E-B", "E", "B", "support", 10),
                Edge("E-F-B", "F", "B", "support", 10)
            ]);
        var update = new GraphNodeUpdateDto { PriorOdds = -0.5m };

        repositoryMock
            .Setup(repository => repository.UpdateNodeAsync("sample-medium", "F", update, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        repositoryMock
            .Setup(repository => repository.GetBySlugAsync("sample-medium", It.IsAny<CancellationToken>()))
            .ReturnsAsync(graph);

        var result = await CreateService(repositoryMock.Object).UpdateNodeAsync("sample-medium", "F", update);

        Assert.IsTrue(result);
        VerifyBatch(repositoryMock, graph.Id, expected =>
            expected.Count == 1 && Approximately(expected["B"], (decimal)Math.Log(100d)));
    }

    [TestMethod]
    public async Task UpdateNodeAsync_RecalculatesAncestors()
    {
        var repositoryMock = new Mock<IGraphRepository>();
        var graph = GraphWith(
            [
                Node("A"),
                Node("B"),
                Node("F", 1m, "evidence")
            ],
            [
                Edge("E-F-B", "F", "B", "support", 10),
                Edge("E-B-A", "B", "A", "support", 10)
            ]);
        var update = new GraphNodeUpdateDto { PriorOdds = 1m };

        repositoryMock
            .Setup(repository => repository.UpdateNodeAsync("sample-medium", "F", update, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        repositoryMock
            .Setup(repository => repository.GetBySlugAsync("sample-medium", It.IsAny<CancellationToken>()))
            .ReturnsAsync(graph);

        var result = await CreateService(repositoryMock.Object).UpdateNodeAsync("sample-medium", "F", update);

        Assert.IsTrue(result);
        VerifyBatch(repositoryMock, graph.Id, expected =>
            expected.Count == 2 &&
            Approximately(expected["B"], (decimal)Math.Log(10d)) &&
            Approximately(expected["A"], (decimal)Math.Log(100d)));
    }

    [TestMethod]
    public async Task UpdateEdgeAsync_RecalculatesParentAfterImportanceUpdate()
    {
        var repositoryMock = new Mock<IGraphRepository>();
        var graph = GraphWith(
            [
                Node("A"),
                Node("B", 1m, "evidence")
            ],
            [Edge("E-B-A", "B", "A", "support", 10)]);
        var update = new GraphEdgeUpdateDto { ImportanceToParent = 10 };

        repositoryMock
            .Setup(repository => repository.UpdateEdgeAsync("sample-medium", "E-B-A", update, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        repositoryMock
            .Setup(repository => repository.GetBySlugAsync("sample-medium", It.IsAny<CancellationToken>()))
            .ReturnsAsync(graph);

        var result = await CreateService(repositoryMock.Object).UpdateEdgeAsync("sample-medium", "E-B-A", update);

        Assert.IsTrue(result);
        VerifyBatch(repositoryMock, graph.Id, expected =>
            expected.Count == 1 && Approximately(expected["A"], (decimal)Math.Log(10d)));
    }

    [TestMethod]
    public async Task UpdateNodeAsync_RootUpdateSucceedsWithoutAncestorBatch()
    {
        var repositoryMock = new Mock<IGraphRepository>();
        var graph = GraphWith([Node("A", 1m)], []);
        var update = new GraphNodeUpdateDto { PriorOdds = 1m };

        repositoryMock
            .Setup(repository => repository.UpdateNodeAsync("sample-medium", "A", update, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        repositoryMock
            .Setup(repository => repository.GetBySlugAsync("sample-medium", It.IsAny<CancellationToken>()))
            .ReturnsAsync(graph);

        var result = await CreateService(repositoryMock.Object).UpdateNodeAsync("sample-medium", "A", update);

        Assert.IsTrue(result);
        repositoryMock.Verify(
            repository => repository.UpdateNodePosteriorOddsBatchAsync(
                It.IsAny<int>(),
                It.IsAny<IReadOnlyDictionary<string, decimal>>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [TestMethod]
    public async Task UpdateNodeAsync_UsesLrBelowOneForCounterImpact()
    {
        var repositoryMock = new Mock<IGraphRepository>();
        var graph = GraphWith(
            [
                Node("A"),
                Node("B", -1m, "evidence")
            ],
            [Edge("E-B-A", "B", "A", "rebut", 0.1m)]);
        var update = new GraphNodeUpdateDto { PriorOdds = -1m };

        repositoryMock
            .Setup(repository => repository.UpdateNodeAsync("sample-medium", "B", update, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        repositoryMock
            .Setup(repository => repository.GetBySlugAsync("sample-medium", It.IsAny<CancellationToken>()))
            .ReturnsAsync(graph);

        var result = await CreateService(repositoryMock.Object).UpdateNodeAsync("sample-medium", "B", update);

        Assert.IsTrue(result);
        VerifyBatch(repositoryMock, graph.Id, expected =>
            expected.Count == 1 && Approximately(expected["A"], (decimal)Math.Log(0.1d)));
    }

    [TestMethod]
    public async Task DeleteNodeAsync_RecalculatesParentFromRemainingChildren()
    {
        var repositoryMock = new Mock<IGraphRepository>();
        var graphBeforeDelete = GraphWith(
            [
                Node("A"),
                Node("B", 1m, "evidence"),
                Node("C", 0.5m, "evidence")
            ],
            [
                Edge("E-B-A", "B", "A", "support", 10),
                Edge("E-C-A", "C", "A", "support", 10)
            ]);
        var graphAfterDelete = GraphWith(
            [
                Node("A"),
                Node("C", 0.5m, "evidence")
            ],
            [Edge("E-C-A", "C", "A", "support", 10)]);

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
            expected.Count == 1 && Approximately(expected["A"], (decimal)Math.Log(10d)));
    }

    [TestMethod]
    public async Task DeleteNodeAsync_RecalculatesParentToZeroWhenNoChildrenRemain()
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

    private static GraphNode Node(string id, decimal logOdds = 0m, string kind = "claim")
    {
        return new GraphNode
        {
            Id = id,
            Kind = kind,
            Title = id,
            BodyText = id,
            PriorOdds = logOdds,
            PosteriorOdds = logOdds
        };
    }

    private static GraphEdge Edge(
        string id,
        string from,
        string to,
        string kind,
        decimal importanceToParent)
    {
        return new GraphEdge
        {
            Id = id,
            From = from,
            To = to,
            Kind = kind,
            ImportanceToParent = importanceToParent
        };
    }

    [TestMethod]
    public async Task GetEvidenceImpactRankingAsync_SplitsAndSortsSupportingAndCounterEvidence()
    {
        var repositoryMock = new Mock<IGraphRepository>();
        var graph = GraphWith(
            [
                Node("R"),
                Node("E1", kind: "evidence"),
                Node("E2", kind: "evidence"),
                Node("O1", kind: "objection"),
                Node("O2", kind: "objection")
            ],
            [
                Edge("E-E1-R", "E1", "R", "support", 2m),
                Edge("E-E2-R", "E2", "R", "support", 3m),
                Edge("E-O1-R", "O1", "R", "rebut", 0.25m),
                Edge("E-O2-R", "O2", "R", "rebut", 0.1m)
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
        Assert.IsTrue(Approximately(result.SupportingEvidence[0].LogLr, (decimal)Math.Log(3d)));
        Assert.IsTrue(Approximately(result.CounterEvidence[0].LogLr, (decimal)Math.Log(0.1d)));
    }

    [TestMethod]
    public void GetEvidenceImpactRanking_RanksEvidenceAcrossBranchingPaths()
    {
        var graph = GraphWith(
            [
                Node("A"),
                Node("B1"),
                Node("B2"),
                Node("C1", kind: "evidence"),
                Node("C2", kind: "objection"),
                Node("C3", kind: "evidence")
            ],
            [
                Edge("E-B1-A", "B1", "A", "support", 1.3m),
                Edge("E-B2-A", "B2", "A", "support", 1.1m),
                Edge("E-C1-B1", "C1", "B1", "support", 1.2m),
                Edge("E-C2-B1", "C2", "B1", "objection", 0.01m),
                Edge("E-C2-B2", "C2", "B2", "objection", 0.1m),
                Edge("E-C3-B2", "C3", "B2", "support", 1.5m)
            ]);

        var calculator = new GraphLikelihoodCalculator();

        var result = calculator.GetEvidenceImpactRanking(graph, "A", CancellationToken.None);

        CollectionAssert.AreEqual(
            new[] { "C3", "C1" },
            result.SupportingEvidence.Select(impact => impact.NodeId).ToArray());
        CollectionAssert.AreEqual(
            new[] { "C2" },
            result.CounterEvidence.Select(impact => impact.NodeId).ToArray());

        var c3 = result.SupportingEvidence.Single(impact => impact.NodeId == "C3");
        var c1 = result.SupportingEvidence.Single(impact => impact.NodeId == "C1");
        var counter = result.CounterEvidence.Single();

        Assert.AreEqual(2, result.SupportingEvidence.Count);
        Assert.IsTrue(Approximately(c3.LogLr, (decimal)Math.Log(1.65d)));
        Assert.IsTrue(Approximately(c1.LogLr, (decimal)Math.Log(1.56d)));
        Assert.IsTrue(Approximately(counter.LogLr, (decimal)Math.Log(0.013d)));
        Assert.IsTrue(result.SupportingEvidence.All(impact => impact.ProbabilityDifference > 0d));
        Assert.IsTrue(counter.ProbabilityDifference < 0d);
    }

    [TestMethod]
    public void GetEvidenceImpactRanking_ThrowsWhenTargetDoesNotExist()
    {
        var graph = GraphWith([Node("A")], []);
        var calculator = new GraphLikelihoodCalculator();

        var exception = Assert.ThrowsException<InvalidOperationException>(() =>
            calculator.GetEvidenceImpactRanking(graph, "missing", CancellationToken.None));

        StringAssert.Contains(exception.Message, "missing");
    }

    [TestMethod]
    public void GetEvidenceImpactRanking_ThrowsWhenCancelled()
    {
        var graph = GraphWith([Node("A")], []);
        var calculator = new GraphLikelihoodCalculator();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.ThrowsException<OperationCanceledException>(() =>
            calculator.GetEvidenceImpactRanking(graph, "A", cancellation.Token));
    }

    private static bool Approximately(decimal actual, decimal expected, decimal tolerance = 0.000001m)
    {
        return Math.Abs(actual - expected) <= tolerance;
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
