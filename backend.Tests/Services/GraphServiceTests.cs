using Backend.Models.Domain;
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

        var service = new GraphService(repositoryMock.Object);

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

        var service = new GraphService(repositoryMock.Object);

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

        var service = new GraphService(repositoryMock.Object);

        await service.GetBySlugAsync("sample-medium", CancellationToken.None);

        repositoryMock.Verify(
            repository => repository.GetBySlugAsync("sample-medium", It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
