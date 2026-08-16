using Backend.Configuration;
using Backend.Data;
using Backend.Insights.Benchmarking;
using Backend.Insights.Contracts;
using Backend.Insights.Export;
using Backend.Insights.Measurement;
using Backend.Insights.Persistence;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Moq;
using Npgsql;

namespace backend.Tests.Insights.Benchmarking;

[TestClass]
[DoNotParallelize]
[TestCategory("PostgreSqlIntegration")]
public sealed class BenchmarkRunnerPostgreSqlVerticalSliceTests
{
    private const string ConnectionStringEnvironmentVariable =
        "LOGICLIKELY_TEST_POSTGRES_CONNECTION_STRING";

    private const string DestructiveOptInEnvironmentVariable =
        "LOGICLIKELY_ALLOW_DESTRUCTIVE_POSTGRES_TESTS";

    [TestMethod]
    [Timeout(60_000)]
    public async Task StrongestPath_PersistsReloadsAndProducesSchemaValidPortableExport()
    {
        var connectionString = RequireDisposablePostgreSql();
        await EnsurePostgreSqlIsAvailable(connectionString);
        var runId = Guid.NewGuid();
        var repositoryRoot = Persistence.BenchmarkSchemaSqlTests.FindRepositoryRoot();
        var options = Options.Create(new DatabaseOptions { ConnectionString = connectionString });
        var connectionFactory = new DbConnectionFactory(options);
        var repository = new BenchmarkRunRepository(connectionFactory);
        var initializer = new BenchmarkSchemaInitializer(
            connectionFactory,
            CreateHostEnvironment(Path.Combine(repositoryRoot, "backend")));
        var exportService = new RunExportService();
        var selection = new BenchmarkRunSelection(
            BenchmarkProfiles.QuickKey,
            ScenarioKey: "quick.strongest.balanced-1k");

        await initializer.InitializeAsync(CancellationToken.None);
        await DeleteRun(connectionString, runId);
        try
        {
            var baseline = (await new SerialBenchmarkRunner(
                new BenchmarkOperationExecutor(),
                exportService: exportService).RunAsync(selection)).Runs.Single();
            var persisted = (await new SerialBenchmarkRunner(
                new BenchmarkOperationExecutor(),
                repository,
                exportService,
                new FixedRunIdentitySource(runId)).RunAsync(
                    selection with { Persist = true })).Runs.Single();

            Assert.IsTrue(persisted.WasPersisted);
            Assert.IsTrue(persisted.WasReloaded);
            Assert.AreEqual(ExecutionStatus.Succeeded, persisted.Manifest.Execution.Status);
            Assert.AreEqual(
                baseline.Manifest.Dataset.DatasetInputFingerprint,
                persisted.Manifest.Dataset.DatasetInputFingerprint);
            Assert.AreEqual(
                baseline.Manifest.CanonicalParameters.Digest,
                persisted.Manifest.CanonicalParameters.Digest);
            Assert.AreEqual(
                baseline.Outputs.Single().ResultDigest,
                persisted.Outputs.Single().ResultDigest);
            Assert.IsTrue(persisted.Samples.Any(sample =>
                sample.Phase == InsightMeasurementPhases.Persistence));
            Assert.IsTrue(persisted.Samples.Any(sample =>
                sample.Phase == InsightMeasurementPhases.ExportValidation));

            var snapshot = await repository.GetSnapshotAsync(runId, CancellationToken.None);
            Assert.IsNotNull(snapshot);
            Assert.AreEqual(
                persisted.Manifest.Dataset.DatasetInputFingerprint,
                snapshot.Manifest.Dataset.DatasetInputFingerprint);
            Assert.AreEqual(
                persisted.Manifest.CanonicalParameters.Digest,
                snapshot.Manifest.CanonicalParameters.Digest);
            Assert.AreEqual(
                persisted.Outputs.Single().ResultDigest,
                snapshot.Outputs.Single().ResultDigest);
            Assert.IsTrue(snapshot.Samples.Any(sample =>
                sample.Phase == InsightMeasurementPhases.ExportValidation));

            var snapshotExport = exportService.Create(
                snapshot.Manifest,
                snapshot.Samples,
                snapshot.Outputs);
            Assert.AreEqual(
                persisted.Export.Digests.SamplesDigest,
                snapshotExport.Digests.SamplesDigest);

            var deserialized = exportService.DeserializeAndValidate(persisted.ExportJson);
            Assert.AreEqual(
                persisted.Export.Digests.ManifestDigest,
                deserialized.Digests.ManifestDigest);
            Assert.AreEqual(
                persisted.Export.Digests.SamplesDigest,
                deserialized.Digests.SamplesDigest);
            Assert.AreEqual(
                persisted.Export.Digests.OutputsDigest,
                deserialized.Digests.OutputsDigest);
        }
        finally
        {
            await DeleteRun(connectionString, runId);
        }
    }

    private static string RequireDisposablePostgreSql()
    {
        var connectionString = Environment.GetEnvironmentVariable(ConnectionStringEnvironmentVariable);
        var destructiveOptIn = Environment.GetEnvironmentVariable(DestructiveOptInEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(connectionString) || destructiveOptIn != "1")
        {
            Assert.Inconclusive(
                $"Set {ConnectionStringEnvironmentVariable} to a disposable database and " +
                $"{DestructiveOptInEnvironmentVariable}=1 to run the benchmark vertical slice.");
        }

        var builder = new NpgsqlConnectionStringBuilder(connectionString);
        if (string.IsNullOrWhiteSpace(builder.Database) ||
            !builder.Database.Contains("test", StringComparison.OrdinalIgnoreCase))
        {
            Assert.Inconclusive("The opt-in database name must contain 'test'.");
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
        var environment = new Mock<IHostEnvironment>();
        environment.Setup(value => value.ContentRootPath).Returns(contentRoot);
        return environment.Object;
    }

    private static async Task DeleteRun(string connectionString, Guid runId)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "DELETE FROM benchmark.runs WHERE run_id = @run_id;",
            connection);
        command.Parameters.AddWithValue("run_id", runId);
        await command.ExecuteNonQueryAsync();
    }

    private sealed class FixedRunIdentitySource : IBenchmarkIdentitySource
    {
        private readonly Guid _runId;

        public FixedRunIdentitySource(Guid runId) => _runId = runId;

        public Guid NewRunId() => _runId;

        public Guid NewSampleId() => Guid.NewGuid();

        public DateTimeOffset UtcNow() => DateTimeOffset.UtcNow;
    }
}
