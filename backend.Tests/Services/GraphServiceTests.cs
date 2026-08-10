using Backend.Calculation;
using Backend.Models.Domain;
using Backend.Models.Dto;
using Backend.Repositories;
using Backend.Services;
using Moq;

namespace backend.Tests.Services;

[TestClass]
public class GraphServiceTests
{
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
    public async Task UpdateNodeAsync_PassesUpdateThroughToRepository()
    {
        var repositoryMock = new Mock<IGraphRepository>();
        var update = new GraphNodeUpdateDto
        {
            Kind = "claim",
            Title = "Updated title",
            BodyText = "Updated body",
            LogOdds = 0.75m
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
        var update = new GraphNodeUpdateDto { LogOdds = 1m };

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
        var update = new GraphNodeUpdateDto { LogOdds = -0.5m };

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
        var update = new GraphNodeUpdateDto { LogOdds = 1m };

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
        var update = new GraphNodeUpdateDto { LogOdds = 1m };

        repositoryMock
            .Setup(repository => repository.UpdateNodeAsync("sample-medium", "A", update, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        repositoryMock
            .Setup(repository => repository.GetBySlugAsync("sample-medium", It.IsAny<CancellationToken>()))
            .ReturnsAsync(graph);

        var result = await CreateService(repositoryMock.Object).UpdateNodeAsync("sample-medium", "A", update);

        Assert.IsTrue(result);
        repositoryMock.Verify(
            repository => repository.UpdateNodeLogOddsBatchAsync(
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
        var update = new GraphNodeUpdateDto { LogOdds = 1m };

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

    private static GraphNode Node(string id, decimal logOdds = 0m)
    {
        return new GraphNode
        {
            Id = id,
            Kind = "claim",
            Title = id,
            BodyText = id,
            LogOdds = logOdds
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
            repository => repository.UpdateNodeLogOddsBatchAsync(
                graphId,
                It.Is<IReadOnlyDictionary<string, decimal>>(values => matches(values)),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
