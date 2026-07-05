using Backend.Data;
using Backend.Models.Dto;
using Backend.Repositories;
using Microsoft.Extensions.Hosting;
using Moq;
using System.Text.Json;

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

        var repository = CreateRepository(connectionFactoryMock.Object);

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

        var repository = CreateRepository(connectionFactoryMock.Object);

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

    [TestMethod]
    public async Task UpdateNodeAsync_UpdatesEditableFieldsForGraphNode()
    {
        var connection = new FakeDbConnection();

        var connectionFactoryMock = new Mock<DbConnectionFactory>(Mock.Of<Microsoft.Extensions.Options.IOptions<Backend.Configuration.DatabaseOptions>>());
        connectionFactoryMock
            .Setup(factory => factory.CreateConnection())
            .Returns(connection);

        var repository = CreateRepository(connectionFactoryMock.Object);
        var update = new GraphNodeUpdateDto
        {
            Kind = "objection",
            Title = "Updated title",
            BodyText = "Updated body",
            LogOdds = 0.75m
        };

        var result = await repository.UpdateNodeAsync("sample-medium", "P1", update, CancellationToken.None);

        Assert.IsTrue(result);
        Assert.AreEqual(1, connection.ExecutedCommands.Count);
        Assert.IsTrue(connection.ExecutedCommands[0].CommandText.Contains("UPDATE nodes"));
        Assert.IsFalse(connection.ExecutedCommands[0].CommandText.Contains("kind ="));
        Assert.AreEqual("sample-medium", connection.ExecutedCommands[0].Parameters["Slug"]);
        Assert.AreEqual("P1", connection.ExecutedCommands[0].Parameters["NodeId"]);
        Assert.IsFalse(connection.ExecutedCommands[0].Parameters.ContainsKey("Kind"));
        Assert.AreEqual("Updated title", connection.ExecutedCommands[0].Parameters["Title"]);
        Assert.AreEqual("Updated body", connection.ExecutedCommands[0].Parameters["BodyText"]);
        Assert.AreEqual(0.75m, connection.ExecutedCommands[0].Parameters["LogOdds"]);
    }

    [TestMethod]
    public async Task UpdateNodeAsync_UpdatesEvidenceScoreFromLogOddsForEvidenceNode()
    {
        var connection = new FakeDbConnection();

        var connectionFactoryMock = new Mock<DbConnectionFactory>(Mock.Of<Microsoft.Extensions.Options.IOptions<Backend.Configuration.DatabaseOptions>>());
        connectionFactoryMock
            .Setup(factory => factory.CreateConnection())
            .Returns(connection);

        var repository = CreateRepository(connectionFactoryMock.Object);
        var update = new GraphNodeUpdateDto
        {
            Kind = "evidence",
            LogOdds = 0m
        };

        var result = await repository.UpdateNodeAsync("sample-medium", "E1", update, CancellationToken.None);

        Assert.IsTrue(result);
        Assert.AreEqual(1, connection.ExecutedCommands.Count);
        Assert.IsTrue(connection.ExecutedCommands[0].CommandText.Contains("jsonb_set"));
        Assert.AreEqual(50.00m, connection.ExecutedCommands[0].Parameters["EvidenceScore"]);
    }

    [TestMethod]
    public async Task AddNodeAsync_SerializesEvidenceScoreFromLogOddsForEvidenceNode()
    {
        var connection = new FakeDbConnection();

        var connectionFactoryMock = new Mock<DbConnectionFactory>(Mock.Of<Microsoft.Extensions.Options.IOptions<Backend.Configuration.DatabaseOptions>>());
        connectionFactoryMock
            .Setup(factory => factory.CreateConnection())
            .Returns(connection);

        var repository = CreateRepository(connectionFactoryMock.Object);
        var node = new GraphNodeDto
        {
            Id = "E-new",
            Kind = "evidence",
            Title = "New evidence",
            BodyText = "New evidence body",
            LogOdds = 0m,
            Evidence = new GraphEvidenceDto
            {
                Type = "observational",
                Rationale = "A rationale"
            }
        };

        var result = await repository.AddNodeAsync("sample-medium", node, cancellationToken: CancellationToken.None);

        Assert.IsTrue(result);
        Assert.AreEqual(1, connection.ExecutedCommands.Count);

        var evidenceJson = connection.ExecutedCommands[0].Parameters["Evidence"] as string;
        Assert.IsNotNull(evidenceJson);

        using var evidence = JsonDocument.Parse(evidenceJson);
        Assert.AreEqual("observational", evidence.RootElement.GetProperty("type").GetString());
        Assert.AreEqual(50.00m, evidence.RootElement.GetProperty("score").GetDecimal());
        Assert.AreEqual("A rationale", evidence.RootElement.GetProperty("rationale").GetString());
    }

    private static GraphRepository CreateRepository(DbConnectionFactory connectionFactory)
    {
        var hostEnvironmentMock = new Mock<IHostEnvironment>();
        hostEnvironmentMock
            .Setup(environment => environment.ContentRootPath)
            .Returns(Directory.GetCurrentDirectory());

        return new GraphRepository(connectionFactory, hostEnvironmentMock.Object);
    }
}
