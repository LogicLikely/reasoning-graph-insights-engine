using Backend.Data;
using Backend.Models.Dto;
using Backend.Repositories;
using Backend.Seeding;
using Microsoft.Extensions.Hosting;
using Moq;
using System.Text.Json;

namespace backend.Tests.Repositories;

[TestClass]
public class GraphRepositoryTests
{
    private static readonly Lazy<string> ValidStressCorpusJson = new(CreateValidStressCorpusJson);

    [TestMethod]
    public async Task GetSummariesAsync_ReturnsGraphMetadataInDatabaseOrder()
    {
        var connection = new FakeDbConnection();
        connection.WhenCommandContains(
            "FROM graphs",
            [
                new Dictionary<string, object?>
                {
                    ["slug"] = "sample-medium",
                    ["title"] = "Sample Medium Reasoning Graph",
                    ["description"] = "Seed graph",
                    ["NodeCount"] = 18,
                    ["EdgeCount"] = 17
                },
                new Dictionary<string, object?>
                {
                    ["slug"] = "flat-earth-large",
                    ["title"] = "Large Flat-Earth Reasoning Graph",
                    ["description"] = null,
                    ["NodeCount"] = 105,
                    ["EdgeCount"] = 112
                }
            ]);

        var connectionFactoryMock = new Mock<DbConnectionFactory>(Mock.Of<Microsoft.Extensions.Options.IOptions<Backend.Configuration.DatabaseOptions>>());
        connectionFactoryMock
            .Setup(factory => factory.CreateConnection())
            .Returns(connection);

        var repository = CreateRepository(connectionFactoryMock.Object);

        var result = await repository.GetSummariesAsync(CancellationToken.None);

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
        Assert.AreEqual(1, connection.ExecutedCommands.Count);
        StringAssert.Contains(connection.ExecutedCommands[0].CommandText, "GROUP BY graph_id");
        StringAssert.Contains(connection.ExecutedCommands[0].CommandText, "ORDER BY graph.id");
    }

    [TestMethod]
    public async Task GetSummariesAsync_ReturnsEmptyList_WhenNoGraphsExist()
    {
        var connection = new FakeDbConnection();
        connection.WhenCommandContains("FROM graphs", []);

        var connectionFactoryMock = new Mock<DbConnectionFactory>(Mock.Of<Microsoft.Extensions.Options.IOptions<Backend.Configuration.DatabaseOptions>>());
        connectionFactoryMock
            .Setup(factory => factory.CreateConnection())
            .Returns(connection);

        var repository = CreateRepository(connectionFactoryMock.Object);

        var result = await repository.GetSummariesAsync(CancellationToken.None);

        Assert.IsNotNull(result);
        Assert.AreEqual(0, result.Count);
        Assert.AreEqual(1, connection.ExecutedCommands.Count);
    }

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
                    ["ImportanceToParent"] = 8,
                    ["ProbabilityGivenParent"] = 0.85m,
                    ["ProbabilityGivenNotParent"] = 0.15m
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
        Assert.AreEqual(0.85m, result.Edges[0].ProbabilityGivenParent);
        Assert.AreEqual(0.15m, result.Edges[0].ProbabilityGivenNotParent);
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
            PriorOdds = 0.75m
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
        Assert.AreEqual(0.75m, connection.ExecutedCommands[0].Parameters["PriorOdds"]);
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
            PriorOdds = 0m
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
            PriorOdds = 0m,
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
    public async Task AddNodeAsync_PropagatesProbabilityWeightsToParentEdge()
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
            BodyText = "New evidence body"
        };

        var result = await repository.AddNodeAsync(
            "sample-medium",
            node,
            parentID: "C1",
            edgeKind: "support",
            importanceToParent: 4m,
            probabilityGivenParent: 0.8m,
            probabilityGivenNotParent: 0.2m,
            cancellationToken: CancellationToken.None);

        Assert.IsTrue(result);
        Assert.AreEqual(2, connection.ExecutedCommands.Count);

        var edgeCommand = connection.ExecutedCommands[1];
        StringAssert.Contains(edgeCommand.CommandText, "INSERT INTO edges");
        StringAssert.Contains(edgeCommand.CommandText, "probability_given_parent");
        StringAssert.Contains(edgeCommand.CommandText, "probability_given_not_parent");
        Assert.AreEqual(0.8m, edgeCommand.Parameters["ProbabilityGivenParent"]);
        Assert.AreEqual(0.2m, edgeCommand.Parameters["ProbabilityGivenNotParent"]);
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
            ImportanceToParent = 3,
            ProbabilityGivenParent = 0.25m,
            ProbabilityGivenNotParent = 0.75m
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
        Assert.AreEqual(0.25m, connection.ExecutedCommands[0].Parameters["ProbabilityGivenParent"]);
        Assert.AreEqual(0.75m, connection.ExecutedCommands[0].Parameters["ProbabilityGivenNotParent"]);
    }

    [TestMethod]
    public async Task UpdateEdgeAsync_UpdatesAllEdgeWeights()
    {
        var connection = new FakeDbConnection();

        var connectionFactoryMock = new Mock<DbConnectionFactory>(Mock.Of<Microsoft.Extensions.Options.IOptions<Backend.Configuration.DatabaseOptions>>());
        connectionFactoryMock
            .Setup(factory => factory.CreateConnection())
            .Returns(connection);

        var repository = CreateRepository(connectionFactoryMock.Object);
        var edge = new GraphEdgeUpdateDto
        {
            ImportanceToParent = 7,
            ProbabilityGivenParent = 0.9m,
            ProbabilityGivenNotParent = 0.1m
        };

        var result = await repository.UpdateEdgeAsync("sample-medium", "E-C1-E1", edge, CancellationToken.None);

        Assert.IsTrue(result);
        Assert.AreEqual(1, connection.ExecutedCommands.Count);
        Assert.IsTrue(connection.ExecutedCommands[0].CommandText.Contains("UPDATE edges"));
        Assert.AreEqual("sample-medium", connection.ExecutedCommands[0].Parameters["Slug"]);
        Assert.AreEqual("E-C1-E1", connection.ExecutedCommands[0].Parameters["EdgeId"]);
        Assert.AreEqual(7m, connection.ExecutedCommands[0].Parameters["ImportanceToParent"]);
        Assert.AreEqual(0.9m, connection.ExecutedCommands[0].Parameters["ProbabilityGivenParent"]);
        Assert.AreEqual(0.1m, connection.ExecutedCommands[0].Parameters["ProbabilityGivenNotParent"]);
        StringAssert.Contains(connection.ExecutedCommands[0].CommandText, "probability_given_parent = COALESCE");
        StringAssert.Contains(connection.ExecutedCommands[0].CommandText, "probability_given_not_parent = COALESCE");
    }

    [TestMethod]
    public async Task UpdateNodePosteriorOddsBatchAsync_DoesNothingWhenDictionaryIsEmpty()
    {
        var connection = new FakeDbConnection();

        var connectionFactoryMock = new Mock<DbConnectionFactory>(Mock.Of<Microsoft.Extensions.Options.IOptions<Backend.Configuration.DatabaseOptions>>());
        connectionFactoryMock
            .Setup(factory => factory.CreateConnection())
            .Returns(connection);

        var repository = CreateRepository(connectionFactoryMock.Object);

        await repository.UpdateNodePosteriorOddsBatchAsync(1, new Dictionary<string, decimal>(), CancellationToken.None);

        Assert.AreEqual(0, connection.ExecutedCommands.Count);
    }

    [TestMethod]
    public async Task UpdateNodePosteriorOddsBatchAsync_UpdatesOnlyLogOddsForGraphNodes()
    {
        var connection = new FakeDbConnection();

        var connectionFactoryMock = new Mock<DbConnectionFactory>(Mock.Of<Microsoft.Extensions.Options.IOptions<Backend.Configuration.DatabaseOptions>>());
        connectionFactoryMock
            .Setup(factory => factory.CreateConnection())
            .Returns(connection);

        var repository = CreateRepository(connectionFactoryMock.Object);

        await repository.UpdateNodePosteriorOddsBatchAsync(
            5,
            new Dictionary<string, decimal>
            {
                ["A"] = 1.25m,
                ["B"] = -0.5m
            },
            CancellationToken.None);

        Assert.AreEqual(2, connection.ExecutedCommands.Count);
        Assert.IsTrue(connection.ExecutedCommands[0].CommandText.Contains("UPDATE nodes"));
        Assert.IsTrue(connection.ExecutedCommands[0].CommandText.Contains("posterior_odds = @PosteriorOdds"));
        Assert.IsTrue(connection.ExecutedCommands[0].CommandText.Contains("updated_at = now()"));
        Assert.IsFalse(connection.ExecutedCommands[0].CommandText.Contains("title ="));
        Assert.IsFalse(connection.ExecutedCommands[0].CommandText.Contains("body_text ="));
        Assert.IsFalse(connection.ExecutedCommands[0].CommandText.Contains("kind ="));
        Assert.AreEqual(5, connection.ExecutedCommands[0].Parameters["GraphId"]);
        Assert.AreEqual("A", connection.ExecutedCommands[0].Parameters["NodeId"]);
        Assert.AreEqual(1.25m, connection.ExecutedCommands[0].Parameters["PosteriorOdds"]);
        Assert.AreEqual(5, connection.ExecutedCommands[1].Parameters["GraphId"]);
        Assert.AreEqual("B", connection.ExecutedCommands[1].Parameters["NodeId"]);
        Assert.AreEqual(-0.5m, connection.ExecutedCommands[1].Parameters["PosteriorOdds"]);
    }

    [TestMethod]
    public async Task ResetDatabaseAsync_InstallsBaseAndSelectedStressGraphsInOneTransaction()
    {
        var seedRoot = CreateSeedRoot(includeStressSeed: true);

        try
        {
            var connection = new FakeDbConnection();
            var connectionFactoryMock = new Mock<DbConnectionFactory>(Mock.Of<Microsoft.Extensions.Options.IOptions<Backend.Configuration.DatabaseOptions>>());
            connectionFactoryMock
                .Setup(factory => factory.CreateConnection())
                .Returns(connection);

            var repository = CreateRepository(connectionFactoryMock.Object, seedRoot);
            var selected = new[]
            {
                StressGraphSeedCatalog.All[0],
                StressGraphSeedCatalog.All[11]
            };

            await repository.ResetDatabaseAsync(selected, CancellationToken.None);

            Assert.AreEqual(3, connection.ExecutedCommands.Count);
            Assert.AreEqual("BASE SEED", connection.ExecutedCommands[0].CommandText);
            Assert.AreEqual(3, connection.ExecutedCommands[1].Parameters["GraphId"]);
            Assert.AreEqual(StressGraphSeedIds.Balanced1K, connection.ExecutedCommands[1].Parameters["Slug"]);
            Assert.AreEqual("balanced", connection.ExecutedCommands[1].Parameters["Shape"]);
            Assert.AreEqual(1_000, connection.ExecutedCommands[1].Parameters["NodeCount"]);
            Assert.AreEqual(10_000, connection.ExecutedCommands[1].Parameters["CorpusEntryCount"]);
            Assert.AreEqual(ValidStressCorpusJson.Value, connection.ExecutedCommands[1].Parameters["CorpusJson"]);
            Assert.AreEqual(14, connection.ExecutedCommands[2].Parameters["GraphId"]);
            Assert.AreEqual(StressGraphSeedIds.SharedDiamond100K, connection.ExecutedCommands[2].Parameters["Slug"]);
            Assert.AreEqual(100_000, connection.ExecutedCommands[2].Parameters["NodeCount"]);
            Assert.AreEqual(10_000, connection.ExecutedCommands[2].Parameters["CorpusEntryCount"]);
            Assert.AreEqual(ValidStressCorpusJson.Value, connection.ExecutedCommands[2].Parameters["CorpusJson"]);
            Assert.IsTrue(connection.ExecutedCommands.All(command => command.CommandTimeout == 300));
            Assert.AreEqual(1, connection.BeginTransactionCount);
            Assert.AreEqual(1, connection.CommitCount);
            Assert.AreEqual(0, connection.RollbackCount);
        }
        finally
        {
            Directory.Delete(seedRoot, recursive: true);
        }
    }

    [TestMethod]
    public async Task ResetDatabaseAsync_BaseOnly_DoesNotRequireStressSeedFile()
    {
        var seedRoot = CreateSeedRoot(
            includeStressSeed: false,
            includeStressCorpus: false);

        try
        {
            var connection = new FakeDbConnection();
            var connectionFactoryMock = new Mock<DbConnectionFactory>(Mock.Of<Microsoft.Extensions.Options.IOptions<Backend.Configuration.DatabaseOptions>>());
            connectionFactoryMock
                .Setup(factory => factory.CreateConnection())
                .Returns(connection);

            var repository = CreateRepository(connectionFactoryMock.Object, seedRoot);

            await repository.ResetDatabaseAsync([], CancellationToken.None);

            Assert.AreEqual(1, connection.ExecutedCommands.Count);
            Assert.AreEqual(300, connection.ExecutedCommands[0].CommandTimeout);
            Assert.AreEqual(1, connection.CommitCount);
        }
        finally
        {
            Directory.Delete(seedRoot, recursive: true);
        }
    }

    [TestMethod]
    public async Task ResetDatabaseAsync_StressSeedFailure_RollsBackWithoutCommit()
    {
        var seedRoot = CreateSeedRoot(includeStressSeed: true);

        try
        {
            var connection = new FakeDbConnection();
            connection.ThrowWhenCommandContains("STRESS");
            var connectionFactoryMock = new Mock<DbConnectionFactory>(Mock.Of<Microsoft.Extensions.Options.IOptions<Backend.Configuration.DatabaseOptions>>());
            connectionFactoryMock
                .Setup(factory => factory.CreateConnection())
                .Returns(connection);
            var repository = CreateRepository(connectionFactoryMock.Object, seedRoot);

            await Assert.ThrowsExceptionAsync<InvalidOperationException>(() =>
                repository.ResetDatabaseAsync(
                    [StressGraphSeedCatalog.All[0]],
                    CancellationToken.None));

            Assert.AreEqual(2, connection.ExecutedCommands.Count);
            Assert.AreEqual(1, connection.BeginTransactionCount);
            Assert.AreEqual(0, connection.CommitCount);
            Assert.AreEqual(1, connection.RollbackCount);
        }
        finally
        {
            Directory.Delete(seedRoot, recursive: true);
        }
    }

    [TestMethod]
    public async Task ResetDatabaseAsync_MissingStressSeed_DoesNotOpenDatabase()
    {
        var seedRoot = CreateSeedRoot(
            includeStressSeed: false,
            includeStressCorpus: true);

        try
        {
            var connectionFactoryMock = new Mock<DbConnectionFactory>(Mock.Of<Microsoft.Extensions.Options.IOptions<Backend.Configuration.DatabaseOptions>>());
            var repository = CreateRepository(connectionFactoryMock.Object, seedRoot);

            await Assert.ThrowsExceptionAsync<FileNotFoundException>(() =>
                repository.ResetDatabaseAsync([StressGraphSeedCatalog.All[0]], CancellationToken.None));

            connectionFactoryMock.Verify(factory => factory.CreateConnection(), Times.Never);
        }
        finally
        {
            Directory.Delete(seedRoot, recursive: true);
        }
    }

    [TestMethod]
    public async Task ResetDatabaseAsync_MissingStressCorpus_DoesNotOpenDatabase()
    {
        var seedRoot = CreateSeedRoot(
            includeStressSeed: true,
            includeStressCorpus: false);

        try
        {
            var connectionFactoryMock = new Mock<DbConnectionFactory>(Mock.Of<Microsoft.Extensions.Options.IOptions<Backend.Configuration.DatabaseOptions>>());
            var repository = CreateRepository(connectionFactoryMock.Object, seedRoot);

            var exception = await Assert.ThrowsExceptionAsync<FileNotFoundException>(() =>
                repository.ResetDatabaseAsync([StressGraphSeedCatalog.All[0]], CancellationToken.None));

            StringAssert.EndsWith(
                exception.FileName!,
                Path.Combine("Data", "Seed", "insights_stress_corpus.json"));
            connectionFactoryMock.Verify(factory => factory.CreateConnection(), Times.Never);
        }
        finally
        {
            Directory.Delete(seedRoot, recursive: true);
        }
    }

    [TestMethod]
    public async Task ResetDatabaseAsync_MalformedStressCorpus_DoesNotOpenDatabase()
    {
        var seedRoot = CreateSeedRoot(
            includeStressSeed: true,
            includeStressCorpus: true,
            corpusJson: "{ definitely-not-valid-json }");

        try
        {
            var connectionFactoryMock = new Mock<DbConnectionFactory>(Mock.Of<Microsoft.Extensions.Options.IOptions<Backend.Configuration.DatabaseOptions>>());
            var repository = CreateRepository(connectionFactoryMock.Object, seedRoot);

            await Assert.ThrowsExceptionAsync<InvalidDataException>(() =>
                repository.ResetDatabaseAsync([StressGraphSeedCatalog.All[0]], CancellationToken.None));

            connectionFactoryMock.Verify(factory => factory.CreateConnection(), Times.Never);
        }
        finally
        {
            Directory.Delete(seedRoot, recursive: true);
        }
    }

    private static string CreateSeedRoot(
        bool includeStressSeed,
        bool includeStressCorpus = true,
        string? corpusJson = null)
    {
        var seedRoot = Path.Combine(
            Path.GetTempPath(),
            $"reasoning-graph-seed-tests-{Guid.NewGuid():N}");
        var sqlDirectory = Path.Combine(seedRoot, "Data", "Sql");
        Directory.CreateDirectory(sqlDirectory);
        File.WriteAllText(Path.Combine(sqlDirectory, "insights_seed.sql"), "BASE SEED");

        if (includeStressSeed)
        {
            File.WriteAllText(
                Path.Combine(sqlDirectory, "insights_stress_seed.sql"),
                "STRESS @GraphId @Slug @Title @Description @Shape @NodeCount @CorpusJson @CorpusEntryCount");
        }

        if (includeStressCorpus)
        {
            var seedDirectory = Path.Combine(seedRoot, "Data", "Seed");
            Directory.CreateDirectory(seedDirectory);
            File.WriteAllText(
                Path.Combine(seedDirectory, "insights_stress_corpus.json"),
                corpusJson ?? ValidStressCorpusJson.Value);
        }

        return seedRoot;
    }

    private static string CreateValidStressCorpusJson()
    {
        return JsonSerializer.Serialize(new
        {
            schemaVersion = 1,
            corpusId = "test-corpus-v1",
            entryCount = StressGraphCorpusLoader.RequiredEntryCount,
            entries = Enumerable
                .Range(0, StressGraphCorpusLoader.RequiredEntryCount)
                .Select(index => new
                {
                    index,
                    title = $"Distinct Corpus Title {index}",
                    excerpt = $"Distinct public-domain excerpt number {index}.",
                    category = "public-domain",
                    tags = new[] { "corpus", "stress" }
                })
        });
    }

    private static GraphRepository CreateRepository(
        DbConnectionFactory connectionFactory,
        string? contentRootPath = null)
    {
        var hostEnvironmentMock = new Mock<IHostEnvironment>();
        hostEnvironmentMock
            .Setup(environment => environment.ContentRootPath)
            .Returns(contentRootPath ?? Directory.GetCurrentDirectory());

        return new GraphRepository(connectionFactory, hostEnvironmentMock.Object);
    }
}
