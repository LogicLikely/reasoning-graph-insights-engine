using Backend.Data;
using Backend.Insights.Persistence;
using backend.Tests.Repositories;
using Microsoft.Extensions.Hosting;
using Moq;

namespace backend.Tests.Insights.Persistence;

[TestClass]
public class BenchmarkSchemaInitializerTests
{
    [TestMethod]
    public async Task InitializeAsync_ExecutesSchemaTransactionallyAndCanBeRepeated()
    {
        var schemaRoot = CreateSchemaRoot("CREATE SCHEMA IF NOT EXISTS benchmark;");
        try
        {
            var connection = new FakeDbConnection();
            var factory = CreateConnectionFactory(connection);
            var initializer = CreateInitializer(factory, schemaRoot);

            await initializer.InitializeAsync(CancellationToken.None);
            await initializer.InitializeAsync(CancellationToken.None);

            Assert.AreEqual(2, connection.ExecutedCommands.Count);
            Assert.IsTrue(connection.ExecutedCommands.All(command =>
                command.CommandText == "CREATE SCHEMA IF NOT EXISTS benchmark;"));
            Assert.IsTrue(connection.ExecutedCommands.All(command => command.CommandTimeout == 30));
            Assert.AreEqual(2, connection.BeginTransactionCount);
            Assert.AreEqual(2, connection.CommitCount);
            Assert.AreEqual(0, connection.RollbackCount);
        }
        finally
        {
            Directory.Delete(schemaRoot, recursive: true);
        }
    }

    [TestMethod]
    public async Task InitializeAsync_CommandFailureRollsBackWithoutCommit()
    {
        var schemaRoot = CreateSchemaRoot("CREATE SCHEMA IF NOT EXISTS benchmark;");
        try
        {
            var connection = new FakeDbConnection();
            connection.ThrowWhenCommandContains("CREATE SCHEMA");
            var initializer = CreateInitializer(CreateConnectionFactory(connection), schemaRoot);

            await Assert.ThrowsExceptionAsync<InvalidOperationException>(() =>
                initializer.InitializeAsync(CancellationToken.None));

            Assert.AreEqual(1, connection.BeginTransactionCount);
            Assert.AreEqual(0, connection.CommitCount);
            Assert.AreEqual(1, connection.RollbackCount);
        }
        finally
        {
            Directory.Delete(schemaRoot, recursive: true);
        }
    }

    [TestMethod]
    public async Task InitializeAsync_MissingOrEmptySqlDoesNotOpenDatabase()
    {
        foreach (var sql in new string?[] { null, "   " })
        {
            var schemaRoot = CreateSchemaRoot(sql);
            try
            {
                var factoryMock = new Mock<DbConnectionFactory>(
                    Mock.Of<Microsoft.Extensions.Options.IOptions<Backend.Configuration.DatabaseOptions>>());
                var initializer = CreateInitializer(factoryMock.Object, schemaRoot);

                if (sql is null)
                {
                    await Assert.ThrowsExceptionAsync<FileNotFoundException>(() =>
                        initializer.InitializeAsync(CancellationToken.None));
                }
                else
                {
                    await Assert.ThrowsExceptionAsync<InvalidDataException>(() =>
                        initializer.InitializeAsync(CancellationToken.None));
                }

                factoryMock.Verify(factory => factory.CreateConnection(), Times.Never);
            }
            finally
            {
                Directory.Delete(schemaRoot, recursive: true);
            }
        }
    }

    private static string CreateSchemaRoot(string? sql)
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            $"benchmark-schema-tests-{Guid.NewGuid():N}");
        if (sql is not null)
        {
            var sqlDirectory = Path.Combine(root, "data", "sql");
            Directory.CreateDirectory(sqlDirectory);
            File.WriteAllText(Path.Combine(sqlDirectory, "benchmark_schema.sql"), sql);
        }
        else
        {
            Directory.CreateDirectory(root);
        }

        return root;
    }

    private static DbConnectionFactory CreateConnectionFactory(FakeDbConnection connection)
    {
        var factoryMock = new Mock<DbConnectionFactory>(
            Mock.Of<Microsoft.Extensions.Options.IOptions<Backend.Configuration.DatabaseOptions>>());
        factoryMock
            .Setup(factory => factory.CreateConnection())
            .Returns(connection);
        return factoryMock.Object;
    }

    private static BenchmarkSchemaInitializer CreateInitializer(
        DbConnectionFactory connectionFactory,
        string contentRoot)
    {
        var environmentMock = new Mock<IHostEnvironment>();
        environmentMock
            .Setup(environment => environment.ContentRootPath)
            .Returns(contentRoot);
        return new BenchmarkSchemaInitializer(connectionFactory, environmentMock.Object);
    }
}
