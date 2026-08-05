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
                    ["kind"] = "support",
                    ["ImportanceToParent"] = 8
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
        Assert.AreEqual(8, result.Edges[0].ImportanceToParent);
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

    [TestMethod]
    public async Task AddEdgeAsync_InsertsParentRelation()
    {
        var connection = new FakeDbConnection();

        var connectionFactoryMock = new Mock<DbConnectionFactory>(Mock.Of<Microsoft.Extensions.Options.IOptions<Backend.Configuration.DatabaseOptions>>());
        connectionFactoryMock
            .Setup(factory => factory.CreateConnection())
            .Returns(connection);

        var repository = CreateRepository(connectionFactoryMock.Object);
        var edge = new GraphEdgeDto
        {
            From = "E1",
            To = "C2",
            Kind = "rebut",
            ImportanceToParent = 3
        };

        var result = await repository.AddEdgeAsync("sample-medium", edge, CancellationToken.None);

        Assert.IsTrue(result);
        Assert.AreEqual(1, connection.ExecutedCommands.Count);
        Assert.IsTrue(connection.ExecutedCommands[0].CommandText.Contains("INSERT INTO edges"));
        Assert.AreEqual("e-E1-C2", connection.ExecutedCommands[0].Parameters["Id"]);
        Assert.AreEqual("E1", connection.ExecutedCommands[0].Parameters["From"]);
        Assert.AreEqual("C2", connection.ExecutedCommands[0].Parameters["To"]);
        Assert.AreEqual("rebut", connection.ExecutedCommands[0].Parameters["Kind"]);
        Assert.AreEqual(3m, connection.ExecutedCommands[0].Parameters["ImportanceToParent"]);
    }

    [TestMethod]
    public async Task UpdateEdgeAsync_UpdatesImportanceToParent()
    {
        var connection = new FakeDbConnection();

        var connectionFactoryMock = new Mock<DbConnectionFactory>(Mock.Of<Microsoft.Extensions.Options.IOptions<Backend.Configuration.DatabaseOptions>>());
        connectionFactoryMock
            .Setup(factory => factory.CreateConnection())
            .Returns(connection);

        var repository = CreateRepository(connectionFactoryMock.Object);
        var edge = new GraphEdgeUpdateDto
        {
            ImportanceToParent = 7
        };

        var result = await repository.UpdateEdgeAsync("sample-medium", "E-C1-E1", edge, CancellationToken.None);

        Assert.IsTrue(result);
        Assert.AreEqual(1, connection.ExecutedCommands.Count);
        Assert.IsTrue(connection.ExecutedCommands[0].CommandText.Contains("UPDATE edges"));
        Assert.AreEqual("sample-medium", connection.ExecutedCommands[0].Parameters["Slug"]);
        Assert.AreEqual("E-C1-E1", connection.ExecutedCommands[0].Parameters["EdgeId"]);
        Assert.AreEqual(7m, connection.ExecutedCommands[0].Parameters["ImportanceToParent"]);
    }

    [TestMethod]
    public async Task UpdateNodeLogOddsBatchAsync_DoesNothingWhenDictionaryIsEmpty()
    {
        var connection = new FakeDbConnection();

        var connectionFactoryMock = new Mock<DbConnectionFactory>(Mock.Of<Microsoft.Extensions.Options.IOptions<Backend.Configuration.DatabaseOptions>>());
        connectionFactoryMock
            .Setup(factory => factory.CreateConnection())
            .Returns(connection);

        var repository = CreateRepository(connectionFactoryMock.Object);

        await repository.UpdateNodeLogOddsBatchAsync(1, new Dictionary<string, decimal>(), CancellationToken.None);

        Assert.AreEqual(0, connection.ExecutedCommands.Count);
    }

    [TestMethod]
    public async Task UpdateNodeLogOddsBatchAsync_UpdatesOnlyLogOddsForGraphNodes()
    {
        var connection = new FakeDbConnection();

        var connectionFactoryMock = new Mock<DbConnectionFactory>(Mock.Of<Microsoft.Extensions.Options.IOptions<Backend.Configuration.DatabaseOptions>>());
        connectionFactoryMock
            .Setup(factory => factory.CreateConnection())
            .Returns(connection);

        var repository = CreateRepository(connectionFactoryMock.Object);

        await repository.UpdateNodeLogOddsBatchAsync(
            5,
            new Dictionary<string, decimal>
            {
                ["A"] = 1.25m,
                ["B"] = -0.5m
            },
            CancellationToken.None);

        Assert.AreEqual(2, connection.ExecutedCommands.Count);
        Assert.IsTrue(connection.ExecutedCommands[0].CommandText.Contains("UPDATE nodes"));
        Assert.IsTrue(connection.ExecutedCommands[0].CommandText.Contains("log_odds = @LogOdds"));
        Assert.IsTrue(connection.ExecutedCommands[0].CommandText.Contains("updated_at = now()"));
        Assert.IsFalse(connection.ExecutedCommands[0].CommandText.Contains("title ="));
        Assert.IsFalse(connection.ExecutedCommands[0].CommandText.Contains("body_text ="));
        Assert.IsFalse(connection.ExecutedCommands[0].CommandText.Contains("kind ="));
        Assert.AreEqual(5, connection.ExecutedCommands[0].Parameters["GraphId"]);
        Assert.AreEqual("A", connection.ExecutedCommands[0].Parameters["NodeId"]);
        Assert.AreEqual(1.25m, connection.ExecutedCommands[0].Parameters["LogOdds"]);
        Assert.AreEqual(5, connection.ExecutedCommands[1].Parameters["GraphId"]);
        Assert.AreEqual("B", connection.ExecutedCommands[1].Parameters["NodeId"]);
        Assert.AreEqual(-0.5m, connection.ExecutedCommands[1].Parameters["LogOdds"]);
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
