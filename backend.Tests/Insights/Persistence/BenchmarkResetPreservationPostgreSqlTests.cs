using Backend.Configuration;
using Backend.Data;
using Backend.Insights.Contracts;
using Backend.Insights.Persistence;
using Backend.Repositories;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Moq;
using Npgsql;

namespace backend.Tests.Insights.Persistence;

[TestClass]
[DoNotParallelize]
[TestCategory("PostgreSqlIntegration")]
public class BenchmarkResetPreservationPostgreSqlTests
{
    private const string ConnectionStringEnvironmentVariable =
        "LOGICLIKELY_TEST_POSTGRES_CONNECTION_STRING";

    private const string DestructiveOptInEnvironmentVariable =
        "LOGICLIKELY_ALLOW_DESTRUCTIVE_POSTGRES_TESTS";

    [TestMethod]
    public async Task GraphReset_PreservesCanonicalBenchmarkSnapshot()
    {
        var connectionString = RequireDisposablePostgreSql();
        await EnsurePostgreSqlIsAvailable(connectionString);

        var repositoryRoot = BenchmarkSchemaSqlTests.FindRepositoryRoot();
        var backendRoot = Path.Combine(repositoryRoot, "backend");
        var options = Options.Create(new DatabaseOptions
        {
            ConnectionString = connectionString
        });
        var connectionFactory = new DbConnectionFactory(options);
        var environment = CreateHostEnvironment(backendRoot);
        var initializer = new BenchmarkSchemaInitializer(connectionFactory, environment);
        var benchmarkRepository = new BenchmarkRunRepository(connectionFactory);
        var graphRepository = new GraphRepository(connectionFactory, environment);
        var intent = ExplicitBenchmarkRunIntent.ForRun(BenchmarkPersistenceTestData.RunId);

        await initializer.InitializeAsync(CancellationToken.None);
        await initializer.InitializeAsync(CancellationToken.None);
        await DeleteFixtureRun(connectionString);

        try
        {
            var queuedManifest = BenchmarkPersistenceTestData.Manifest() with
            {
                Execution = new ExecutionOutcome(ExecutionStatus.Queued)
            };
            await benchmarkRepository.CreateRunAsync(
                intent,
                queuedManifest,
                CancellationToken.None);
            await benchmarkRepository.UpdateLifecycleAsync(
                intent,
                new ExecutionOutcome(ExecutionStatus.Running),
                null,
                CancellationToken.None);
            await benchmarkRepository.AppendSampleAsync(
                intent,
                BenchmarkPersistenceTestData.Sample(),
                CancellationToken.None);
            await benchmarkRepository.AppendOutputAsync(
                intent,
                BenchmarkPersistenceTestData.Output(),
                CancellationToken.None);
            await benchmarkRepository.UpdateLifecycleAsync(
                intent,
                new ExecutionOutcome(ExecutionStatus.Succeeded),
                DateTimeOffset.Parse("2026-08-15T14:00:03-04:00"),
                CancellationToken.None);

            await benchmarkRepository.UpdateLifecycleAsync(
                intent,
                new ExecutionOutcome(ExecutionStatus.Succeeded),
                DateTimeOffset.Parse("2026-08-15T14:00:03-04:00"),
                CancellationToken.None);
            await Assert.ThrowsExceptionAsync<InvalidOperationException>(() =>
                benchmarkRepository.UpdateLifecycleAsync(
                    intent,
                    new ExecutionOutcome(
                        ExecutionStatus.Cancelled,
                        BenchmarkPersistenceTestData.Failure(FailureKind.Cancellation)),
                    DateTimeOffset.Parse("2026-08-15T14:00:04-04:00"),
                    CancellationToken.None));

            var beforeReset = await benchmarkRepository.GetSnapshotAsync(
                BenchmarkPersistenceTestData.RunId,
                CancellationToken.None);
            Assert.IsNotNull(beforeReset);
            var beforeManifestDigest = CanonicalJson.ComputeSha256(beforeReset.Manifest);
            var beforeSamplesDigest = CanonicalJson.ComputeSha256(beforeReset.Samples);
            var beforeOutputsDigest = CanonicalJson.ComputeSha256(beforeReset.Outputs);

            await graphRepository.ResetDatabaseAsync([], CancellationToken.None);

            var afterReset = await benchmarkRepository.GetSnapshotAsync(
                BenchmarkPersistenceTestData.RunId,
                CancellationToken.None);
            Assert.IsNotNull(afterReset);
            Assert.AreEqual(beforeManifestDigest, CanonicalJson.ComputeSha256(afterReset.Manifest));
            Assert.AreEqual(beforeSamplesDigest, CanonicalJson.ComputeSha256(afterReset.Samples));
            Assert.AreEqual(beforeOutputsDigest, CanonicalJson.ComputeSha256(afterReset.Outputs));
            Assert.AreEqual(2, await CountBaseGraphs(connectionString));

            await DeleteFixtureRun(connectionString);
            var terminalOutcomes = new[]
            {
                new ExecutionOutcome(ExecutionStatus.Succeeded),
                new ExecutionOutcome(
                    ExecutionStatus.Failed,
                    BenchmarkPersistenceTestData.Failure(FailureKind.Execution)),
                ExecutionOutcome.ValidationFailed(
                [
                    new ValidationFailure("graphSlug", "missing", "Graph was not found.")
                ]),
                new ExecutionOutcome(
                    ExecutionStatus.TimedOut,
                    BenchmarkPersistenceTestData.Failure(FailureKind.Timeout)),
                new ExecutionOutcome(
                    ExecutionStatus.Cancelled,
                    BenchmarkPersistenceTestData.Failure(FailureKind.Cancellation)),
                new ExecutionOutcome(
                    ExecutionStatus.Crashed,
                    BenchmarkPersistenceTestData.Failure(FailureKind.Crash))
            };
            foreach (var terminalOutcome in terminalOutcomes)
            {
                await benchmarkRepository.CreateRunAsync(
                    intent,
                    queuedManifest,
                    CancellationToken.None);
                await benchmarkRepository.UpdateLifecycleAsync(
                    intent,
                    terminalOutcome,
                    DateTimeOffset.Parse("2026-08-15T14:00:03-04:00"),
                    CancellationToken.None);

                var terminalSnapshot = await benchmarkRepository.GetSnapshotAsync(
                    BenchmarkPersistenceTestData.RunId,
                    CancellationToken.None);
                Assert.IsNotNull(terminalSnapshot);
                Assert.AreEqual(terminalOutcome.Status, terminalSnapshot.Manifest.Execution.Status);
                Assert.AreEqual(
                    terminalOutcome.Failure?.Kind,
                    terminalSnapshot.Manifest.Execution.Failure?.Kind);
                await DeleteFixtureRun(connectionString);
            }
        }
        finally
        {
            await DeleteFixtureRun(connectionString);
        }
    }

    private static string RequireDisposablePostgreSql()
    {
        var connectionString = Environment.GetEnvironmentVariable(
            ConnectionStringEnvironmentVariable);
        var destructiveOptIn = Environment.GetEnvironmentVariable(
            DestructiveOptInEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(connectionString) || destructiveOptIn != "1")
        {
            Assert.Inconclusive(
                $"Set {ConnectionStringEnvironmentVariable} to a disposable database and " +
                $"{DestructiveOptInEnvironmentVariable}=1 to run the reset-preservation test.");
        }

        var builder = new NpgsqlConnectionStringBuilder(connectionString);
        if (string.IsNullOrWhiteSpace(builder.Database) ||
            !builder.Database.Contains("test", StringComparison.OrdinalIgnoreCase))
        {
            Assert.Inconclusive(
                "The opt-in connection must name a clearly disposable database containing 'test'.");
        }

        return connectionString;
    }

    private static async Task EnsurePostgreSqlIsAvailable(string connectionString)
    {
        try
        {
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync();
        }
        catch (NpgsqlException exception)
        {
            Assert.Inconclusive($"The opt-in PostgreSQL database is unavailable: {exception.Message}");
        }
    }

    private static IHostEnvironment CreateHostEnvironment(string contentRoot)
    {
        var environmentMock = new Mock<IHostEnvironment>();
        environmentMock
            .Setup(environment => environment.ContentRootPath)
            .Returns(contentRoot);
        return environmentMock.Object;
    }

    private static async Task DeleteFixtureRun(string connectionString)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "DELETE FROM benchmark.runs WHERE run_id = @run_id;",
            connection);
        command.Parameters.AddWithValue("run_id", BenchmarkPersistenceTestData.RunId);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<int> CountBaseGraphs(string connectionString)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "SELECT count(*) FROM public.graphs;",
            connection);
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }
}
