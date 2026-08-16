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
    public async Task SchemaInitialization_FreshAndSteadyState_PreservesPayloadConstraintOids()
    {
        var connectionString = RequireDisposablePostgreSql();
        await EnsurePostgreSqlIsAvailable(connectionString);

        var repositoryRoot = BenchmarkSchemaSqlTests.FindRepositoryRoot();
        var options = Options.Create(new DatabaseOptions
        {
            ConnectionString = connectionString
        });
        var initializer = new BenchmarkSchemaInitializer(
            new DbConnectionFactory(options),
            CreateHostEnvironment(Path.Combine(repositoryRoot, "backend")));

        await ResetBenchmarkSchema(connectionString);
        try
        {
            await initializer.InitializeAsync(CancellationToken.None);
            var initialConstraintOids = await ReadPayloadConstraintOids(connectionString);
            Assert.AreEqual(2, initialConstraintOids.Length);

            await initializer.InitializeAsync(CancellationToken.None);
            var repeatedConstraintOids = await ReadPayloadConstraintOids(connectionString);

            CollectionAssert.AreEqual(
                initialConstraintOids,
                repeatedConstraintOids,
                "Steady-state initialization must not replace current payload constraints.");
        }
        finally
        {
            await ResetBenchmarkSchema(connectionString);
        }
    }

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

    [TestMethod]
    public async Task Phase35SchemaReconciliation_PreservesRowsAndUnrelatedOutputData()
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
        var initializer = new BenchmarkSchemaInitializer(
            connectionFactory,
            CreateHostEnvironment(backendRoot));
        var benchmarkRepository = new BenchmarkRunRepository(connectionFactory);
        var intent = ExplicitBenchmarkRunIntent.ForRun(BenchmarkPersistenceTestData.RunId);

        await initializer.InitializeAsync(CancellationToken.None);
        await DeleteFixtureRun(connectionString);

        try
        {
            await benchmarkRepository.CreateRunAsync(
                intent,
                BenchmarkPersistenceTestData.Manifest(),
                CancellationToken.None);
            await benchmarkRepository.AppendSampleAsync(
                intent,
                BenchmarkPersistenceTestData.Sample(),
                CancellationToken.None);
            await benchmarkRepository.AppendOutputAsync(
                intent,
                BenchmarkPersistenceTestData.Output(),
                CancellationToken.None);

            var before = await benchmarkRepository.GetSnapshotAsync(
                BenchmarkPersistenceTestData.RunId,
                CancellationToken.None);
            Assert.IsNotNull(before);
            var manifestDigest = CanonicalJson.ComputeSha256(before.Manifest);
            var sampleDigest = CanonicalJson.ComputeSha256(before.Samples);
            var resultDigest = before.Outputs.Single().ResultDigest;
            var itemsDigest = CanonicalJson.ComputeSha256(before.Outputs.Single().Items);
            var rowCounts = await ReadBenchmarkRowCounts(connectionString);
            var initialConstraintOids = await ReadPayloadConstraintOids(connectionString);

            await SimulatePrePhase35Samples(connectionString);
            await initializer.InitializeAsync(CancellationToken.None);
            var afterSamplesConstraintOids = await ReadPayloadConstraintOids(connectionString);
            Assert.AreNotEqual(
                PayloadConstraintOid(initialConstraintOids, "ck_benchmark_samples_payload_identity"),
                PayloadConstraintOid(afterSamplesConstraintOids, "ck_benchmark_samples_payload_identity"));
            Assert.AreEqual(
                PayloadConstraintOid(initialConstraintOids, "ck_benchmark_outputs_payload_identity"),
                PayloadConstraintOid(afterSamplesConstraintOids, "ck_benchmark_outputs_payload_identity"),
                "Reconciling samples must not replace the outputs payload constraint.");
            Assert.AreEqual(0, await CountRetiredColumns(connectionString));
            Assert.AreEqual(0, await CountRetiredJsonMembers(connectionString));

            await SimulatePrePhase35Outputs(connectionString);
            await initializer.InitializeAsync(CancellationToken.None);
            var afterOutputsConstraintOids = await ReadPayloadConstraintOids(connectionString);
            Assert.AreEqual(
                PayloadConstraintOid(afterSamplesConstraintOids, "ck_benchmark_samples_payload_identity"),
                PayloadConstraintOid(afterOutputsConstraintOids, "ck_benchmark_samples_payload_identity"),
                "Reconciling outputs must not replace the samples payload constraint.");
            Assert.AreNotEqual(
                PayloadConstraintOid(afterSamplesConstraintOids, "ck_benchmark_outputs_payload_identity"),
                PayloadConstraintOid(afterOutputsConstraintOids, "ck_benchmark_outputs_payload_identity"));

            var after = await benchmarkRepository.GetSnapshotAsync(
                BenchmarkPersistenceTestData.RunId,
                CancellationToken.None);
            Assert.IsNotNull(after);
            Assert.AreEqual(manifestDigest, CanonicalJson.ComputeSha256(after.Manifest));
            Assert.AreEqual(sampleDigest, CanonicalJson.ComputeSha256(after.Samples));
            Assert.AreEqual(1, after.Outputs.Count);
            Assert.AreEqual(resultDigest, after.Outputs[0].ResultDigest);
            Assert.AreEqual(itemsDigest, CanonicalJson.ComputeSha256(after.Outputs[0].Items));
            Assert.AreEqual(
                "kept",
                after.Outputs[0].Summary.GetProperty("phase35Preserved").GetString());
            Assert.AreEqual(rowCounts, await ReadBenchmarkRowCounts(connectionString));
            Assert.AreEqual(0, await CountRetiredColumns(connectionString));
            Assert.AreEqual(0, await CountRetiredJsonMembers(connectionString));
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

    private static async Task ResetBenchmarkSchema(string connectionString)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "DROP SCHEMA IF EXISTS benchmark CASCADE;",
            connection);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task SimulatePrePhase35Samples(string connectionString)
    {
        const string sql = """
            ALTER TABLE benchmark.samples
                DROP CONSTRAINT ck_benchmark_samples_payload_identity;

            ALTER TABLE benchmark.samples
                ADD COLUMN visualization_admission text NOT NULL DEFAULT 'not-requested';

            UPDATE benchmark.samples
            SET sample_json = jsonb_set(
                jsonb_set(
                    sample_json,
                    '{visualizationAdmission}',
                    '"not-requested"'::jsonb,
                    true
                ),
                '{warnings}',
                '["legacy sample notice"]'::jsonb,
                true
            );

            ALTER TABLE benchmark.samples
                ADD CONSTRAINT ck_benchmark_samples_payload_identity CHECK (
                    sample_json->>'visualizationAdmission' = visualization_admission
                );
            """;

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task SimulatePrePhase35Outputs(string connectionString)
    {
        const string sql = """
            ALTER TABLE benchmark.outputs
                DROP CONSTRAINT ck_benchmark_outputs_payload_identity;

            ALTER TABLE benchmark.outputs
                ADD COLUMN visualization_admission text NOT NULL DEFAULT 'allowed';

            UPDATE benchmark.outputs
            SET output_json = jsonb_set(
                jsonb_set(
                    jsonb_set(
                        output_json,
                        '{visualizationAdmission}',
                        '"allowed"'::jsonb,
                        true
                    ),
                    '{warnings}',
                    '["legacy output notice"]'::jsonb,
                    true
                ),
                '{summary,phase35Preserved}',
                '"kept"'::jsonb,
                true
            );

            ALTER TABLE benchmark.outputs
                ADD CONSTRAINT ck_benchmark_outputs_payload_identity CHECK (
                    output_json->>'visualizationAdmission' = visualization_admission
                );
            """;

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<(string Name, long Oid)[]> ReadPayloadConstraintOids(
        string connectionString)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            """
            SELECT conname, oid::bigint
            FROM pg_catalog.pg_constraint
            WHERE conrelid IN ('benchmark.samples'::regclass, 'benchmark.outputs'::regclass)
              AND conname IN (
                  'ck_benchmark_samples_payload_identity',
                  'ck_benchmark_outputs_payload_identity'
              )
            ORDER BY conname;
            """,
            connection);
        await using var reader = await command.ExecuteReaderAsync();
        var constraints = new List<(string Name, long Oid)>();
        while (await reader.ReadAsync())
        {
            constraints.Add((reader.GetString(0), reader.GetInt64(1)));
        }

        return [.. constraints];
    }

    private static long PayloadConstraintOid(
        IEnumerable<(string Name, long Oid)> constraints,
        string name)
        => constraints.Single(constraint => constraint.Name == name).Oid;

    private static async Task<(long Runs, long Samples, long Outputs)> ReadBenchmarkRowCounts(
        string connectionString)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            """
            SELECT
                (SELECT count(*) FROM benchmark.runs),
                (SELECT count(*) FROM benchmark.samples),
                (SELECT count(*) FROM benchmark.outputs);
            """,
            connection);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.IsTrue(await reader.ReadAsync());
        return (reader.GetInt64(0), reader.GetInt64(1), reader.GetInt64(2));
    }

    private static async Task<int> CountRetiredColumns(string connectionString)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            """
            SELECT count(*)
            FROM information_schema.columns
            WHERE table_schema = 'benchmark'
              AND table_name IN ('samples', 'outputs')
              AND column_name = 'visualization_admission';
            """,
            connection);
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    private static async Task<int> CountRetiredJsonMembers(string connectionString)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            """
            SELECT
                (SELECT count(*) FROM benchmark.samples
                 WHERE sample_json ? 'visualizationAdmission' OR sample_json ? 'warnings')
                +
                (SELECT count(*) FROM benchmark.outputs
                 WHERE output_json ? 'visualizationAdmission' OR output_json ? 'warnings');
            """,
            connection);
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }
}
