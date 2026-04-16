using Backend.Data;
using Backend.Repositories;
using Moq;

namespace backend.Tests.Repositories;

[TestClass]
public class GraphRepositoryTests
{
    [TestMethod]
    public async Task GetBySlugAsync_ReturnsNull_WhenGraphIsNotFound()
    {
        var connection = new FakeDbConnection();
        connection.WhenCommandContains("FROM graphs", []);

        var connectionFactoryMock = new Mock<DbConnectionFactory>(Mock.Of<Microsoft.Extensions.Options.IOptions<Backend.Configuration.DatabaseOptions>>());
        connectionFactoryMock
            .Setup(factory => factory.CreateConnection())
            .Returns(connection);

        var repository = new GraphRepository(connectionFactoryMock.Object);

        var result = await repository.GetBySlugAsync("missing", CancellationToken.None);

        Assert.IsNull(result);
        Assert.AreEqual(1, connection.ExecutedCommands.Count);
        Assert.AreEqual("missing", connection.ExecutedCommands[0].Parameters["Slug"]);
    }

    [TestMethod]
    public async Task GetBySlugAsync_ReturnsGraphWithNodesAndEdges_WhenGraphExists()
    {
        var connection = new FakeDbConnection();

        connection.WhenCommandContains(
            "FROM graphs",
            [
                new Dictionary<string, object?>
                {
                    ["id"] = 1,
                    ["slug"] = "sample-medium",
                    ["title"] = "Sample Medium Reasoning Graph",
                    ["description"] = "Seed graph"
                }
            ]);

        connection.WhenCommandContains(
            "FROM nodes",
            [
                new Dictionary<string, object?>
                {
                    ["id"] = "R1",
                    ["kind"] = "root",
                    ["title"] = "Earth is flat",
                    ["BodyText"] = "The Earth is flat."
                },
                new Dictionary<string, object?>
                {
                    ["id"] = "C1",
                    ["kind"] = "claim",
                    ["title"] = "The horizon looks flat",
                    ["BodyText"] = "The horizon appears flat."
                }
            ]);

        connection.WhenCommandContains(
            "FROM edges",
            [
                new Dictionary<string, object?>
                {
                    ["id"] = "E-R-C1",
                    ["From"] = "C1",
                    ["To"] = "R1",
                    ["kind"] = "support"
                }
            ]);

        var connectionFactoryMock = new Mock<DbConnectionFactory>(Mock.Of<Microsoft.Extensions.Options.IOptions<Backend.Configuration.DatabaseOptions>>());
        connectionFactoryMock
            .Setup(factory => factory.CreateConnection())
            .Returns(connection);

        var repository = new GraphRepository(connectionFactoryMock.Object);

        var result = await repository.GetBySlugAsync("sample-medium", CancellationToken.None);

        Assert.IsNotNull(result);
        Assert.AreEqual(1, result.Id);
        Assert.AreEqual("sample-medium", result.Slug);
        Assert.AreEqual("Sample Medium Reasoning Graph", result.Title);
        Assert.AreEqual("Seed graph", result.Description);
        Assert.AreEqual(2, result.Nodes.Count);
        Assert.AreEqual("R1", result.Nodes[0].Id);
        Assert.AreEqual("root", result.Nodes[0].Kind);
        Assert.AreEqual("Earth is flat", result.Nodes[0].Title);
        Assert.AreEqual("The Earth is flat.", result.Nodes[0].BodyText);
        Assert.AreEqual(1, result.Edges.Count);
        Assert.AreEqual("E-R-C1", result.Edges[0].Id);
        Assert.AreEqual("C1", result.Edges[0].From);
        Assert.AreEqual("R1", result.Edges[0].To);
        Assert.AreEqual("support", result.Edges[0].Kind);
        Assert.AreEqual(3, connection.ExecutedCommands.Count);
        Assert.AreEqual("sample-medium", connection.ExecutedCommands[0].Parameters["Slug"]);
        Assert.AreEqual(1, connection.ExecutedCommands[1].Parameters["GraphId"]);
        Assert.AreEqual(1, connection.ExecutedCommands[2].Parameters["GraphId"]);
    }
}
