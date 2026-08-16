using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using Backend.Configuration;
using Backend.Controllers;
using Backend.Data;
using Backend.Insights.Benchmarking;
using Backend.Insights.Contracts;
using Backend.Insights.Export;
using Backend.Insights.Measurement;
using Backend.Insights.Persistence;
using Backend.Seeding;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Moq;
using Npgsql;

namespace backend.Tests.Insights.Benchmarking;

[TestClass]
[DoNotParallelize]
[TestCategory("PostgreSqlIntegration")]
public sealed class RestApiPostgreSqlJourneyTests
{
    private const string ConnectionStringEnvironmentVariable =
        "LOGICLIKELY_TEST_POSTGRES_CONNECTION_STRING";
    private const string DestructiveOptInEnvironmentVariable =
        "LOGICLIKELY_ALLOW_DESTRUCTIVE_POSTGRES_TESTS";

    [TestMethod]
    [Timeout(900_000)]
    public async Task CatalogFetchAndAnalysisParity_UseRealKestrelPostgreSqlAndPersistPortableEvidence()
    {
        var connectionString = RequireDisposablePostgreSql();
        var postgreSqlVersion = await RequirePostgreSqlVersion(connectionString);
        var resetTargetExpectation = await RequireResetTargetExpectation(connectionString);
        var repositoryRoot = Persistence.BenchmarkSchemaSqlTests.FindRepositoryRoot();
        var databaseOptions = Options.Create(new DatabaseOptions
        {
            ConnectionString = connectionString
        });
        var connectionFactory = new DbConnectionFactory(databaseOptions);
        var repository = new BenchmarkRunRepository(connectionFactory);
        var initializer = new BenchmarkSchemaInitializer(
            connectionFactory,
            CreateHostEnvironment(Path.Combine(repositoryRoot, "backend")));
        var exportService = new RunExportService();
        await initializer.InitializeAsync(CancellationToken.None);

        var port = ReservePort();
        var baseAddress = new Uri($"http://127.0.0.1:{port}/");
        using var process = StartApiProcess(connectionString, port);
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        var persistedRunIds = new List<Guid>();

        try
        {
            using var client = CreateHttp2Client();
            await WaitUntilHealthy(client, baseAddress, process, stdout, stderr);
            var restExecutor = new RestApiBenchmarkOperationExecutor(
                client,
                new RestApiJourneyOptions(
                    baseAddress,
                    RestApiJourneyBoundary.RealProcessNetwork,
                    postgreSqlVersion));
            var installingRouter = new BenchmarkOperationExecutorRouter(
                new BenchmarkOperationExecutor(),
                restExecutor,
                new RestBenchmarkDatasetInstaller(
                    client,
                    baseAddress,
                    resetTargetExpectation),
                TimeSpan.FromMinutes(12));
            var measuredRouter = new BenchmarkOperationExecutorRouter(
                new BenchmarkOperationExecutor(),
                restExecutor);

            var catalog = await RunPersisted(
                "quick.graph-catalog.rest",
                installingRouter,
                repository,
                exportService,
                persistedRunIds,
                TimeSpan.FromMinutes(2));
            Assert.AreEqual(ExecutionStatus.Succeeded, catalog.Manifest.Execution.Status);
            Assert.AreEqual(
                catalog.Outputs.Single().Summary.GetProperty("graphCount").GetInt64(),
                catalog.Outputs.Single().TotalResultCardinality,
                "The complete catalog includes both ordinary seed graphs and every stress graph.");
            Assert.AreEqual(
                StressGraphSeedCatalog.All.Count,
                catalog.Outputs.Single().Summary.GetProperty("canonicalStressGraphCount").GetInt32());
            Assert.AreEqual(
                StressGraphSeedCatalog.All.Sum(specification => (long)specification.NodeCount),
                catalog.Manifest.Graph.ActualNodeCount);
            Assert.AreEqual(
                StressGraphSeedCatalog.All.Sum(specification => (long)specification.EdgeCount),
                catalog.Manifest.Graph.ActualEdgeCount);
            Assert.AreEqual("canonical-stress-catalog", catalog.Manifest.Graph.Slug);
            Assert.AreEqual("catalog-aggregate", catalog.Manifest.Graph.Shape);
            Assert.AreEqual("public-domain-stress-corpus-v1", catalog.Manifest.Dataset.CorpusId);
            StringAssert.StartsWith(catalog.Manifest.Dataset.InputFingerprint, "sha256:");
            Assert.IsTrue(catalog.Samples.Any(sample =>
                sample.Classification.IterationKind == IterationClassificationTokens.Setup &&
                sample.Phase == InsightMeasurementPhases.FixtureConstruction &&
                sample.Transport.RequestBytes > 0));
            Assert.IsTrue(catalog.Samples.Any(sample =>
                sample.Classification.IterationKind == IterationClassificationTokens.Measured &&
                sample.Phase == InsightMeasurementPhases.CatalogAggregation));

            var fetch = await RunPersisted(
                "quick.graph-fetch.balanced-1k.rest",
                measuredRouter,
                repository,
                exportService,
                persistedRunIds,
                TimeSpan.FromSeconds(60));

            Assert.AreEqual(ExecutionStatus.Succeeded, fetch.Manifest.Execution.Status);
            Assert.IsTrue(fetch.Samples.Any(sample =>
                sample.Classification.IterationKind == IterationClassificationTokens.Setup &&
                sample.SampleId == fetch.Outputs.Single().SampleId));
            CollectionAssert.IsSubsetOf(
                new[]
                {
                    InsightMeasurementPhases.GraphLookup,
                    InsightMeasurementPhases.NodeQuery,
                    InsightMeasurementPhases.EvidenceJsonMaterialization,
                    InsightMeasurementPhases.EdgeQuery,
                    InsightMeasurementPhases.DtoMapping,
                    InsightMeasurementPhases.Serialization,
                    InsightMeasurementPhases.TimeToFirstByte,
                    InsightMeasurementPhases.FullTransfer,
                    InsightMeasurementPhases.ResponseBytes
                },
                fetch.Samples.Select(sample => sample.Phase).ToArray());
            Assert.IsTrue(fetch.Samples
                .Where(sample => sample.Layer == InsightMeasurementLayers.Transport)
                .All(sample => sample.TimingBoundaryProvenance ==
                    TimingBoundaryProvenance.ExternallyObserved));
            Assert.IsTrue(fetch.Samples.Any(sample =>
                sample.NodeCounts.Canonical == 1_000 &&
                sample.Transport.ResponseBytes > 0));
            var output = fetch.Outputs.Single();
            Assert.AreEqual(1_000, output.Summary.GetProperty("actualNodeCount").GetInt64());
            Assert.AreEqual(999, output.Summary.GetProperty("actualEdgeCount").GetInt64());
            StringAssert.StartsWith(
                output.Summary.GetProperty("observedDatasetFingerprint").GetString(),
                "sha256:");

            var evidenceDatabase = await RunPersisted(
                "quick.evidence.wide-1k.rest.database-loaded",
                measuredRouter,
                repository,
                exportService,
                persistedRunIds,
                TimeSpan.FromSeconds(60));
            var evidenceSupplied = await RunPersisted(
                "quick.evidence.wide-1k.rest.supplied-graph",
                measuredRouter,
                repository,
                exportService,
                persistedRunIds,
                TimeSpan.FromSeconds(60));
            AssertAnalysisParity(evidenceDatabase, evidenceSupplied);

            var robustnessDatabase = await RunPersisted(
                "quick.robustness.balanced-1k.rest.database-loaded",
                measuredRouter,
                repository,
                exportService,
                persistedRunIds,
                TimeSpan.FromSeconds(60));
            var robustnessSupplied = await RunPersisted(
                "quick.robustness.balanced-1k.rest.supplied-graph",
                measuredRouter,
                repository,
                exportService,
                persistedRunIds,
                TimeSpan.FromSeconds(60));
            AssertAnalysisParity(robustnessDatabase, robustnessSupplied);

            foreach (var run in new[]
                     {
                         catalog,
                         fetch,
                         evidenceDatabase,
                         evidenceSupplied,
                         robustnessDatabase,
                         robustnessSupplied
                     })
            {
                await AssertPersistedPortable(run, repository, exportService);
                StringAssert.Contains(run.Manifest.EnvironmentProfile, "real-process-network");
                StringAssert.Contains(run.Manifest.EnvironmentProfile,
                    $"postgresql-major-{new Version(postgreSqlVersion).Major}");
                Assert.AreEqual(postgreSqlVersion, run.Manifest.Dependencies.PostgreSql);
                Assert.AreEqual(
                    "real-process-network",
                    run.Manifest.Dependencies.RelevantDependencies["api-boundary"]);
            }
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync();
            }

            foreach (var runId in persistedRunIds)
            {
                await DeleteRun(connectionString, runId);
            }
        }
    }

    private static async Task<BenchmarkSingleRunResult> RunPersisted(
        string scenarioKey,
        IBenchmarkOperationExecutor executor,
        IBenchmarkRunRepository repository,
        RunExportService exportService,
        ICollection<Guid> runIds,
        TimeSpan timeout)
    {
        var runId = Guid.NewGuid();
        runIds.Add(runId);
        var runner = new SerialBenchmarkRunner(
            executor,
            repository,
            exportService,
            new FixedRunIdentitySource(runId));
        return (await runner.RunAsync(new BenchmarkRunSelection(
            BenchmarkProfiles.QuickKey,
            ScenarioKey: scenarioKey,
            Timeout: timeout,
            Persist: true))).Runs.Single();
    }

    private static void AssertAnalysisParity(
        BenchmarkSingleRunResult database,
        BenchmarkSingleRunResult supplied)
    {
        Assert.AreEqual(ExecutionStatus.Succeeded, database.Manifest.Execution.Status,
            FailureDescription(database.Manifest.Execution));
        Assert.AreEqual(ExecutionStatus.Succeeded, supplied.Manifest.Execution.Status,
            FailureDescription(supplied.Manifest.Execution));
        Assert.AreEqual(
            database.Manifest.Dataset.DatasetInputFingerprint,
            supplied.Manifest.Dataset.DatasetInputFingerprint);
        Assert.AreEqual(
            database.Outputs.Single().ResultDigest,
            supplied.Outputs.Single().ResultDigest,
            "Database-loaded and supplied-graph modes must retain the frozen canonical digest.");
        Assert.AreEqual(
            Observed(database, "responseFingerprint").GetString(),
            Observed(supplied, "responseFingerprint").GetString(),
            "The independently observed legacy REST payload fingerprints must match.");
        Assert.AreEqual(
            database.Outputs.Single().TotalResultCardinality,
            Observed(database, "responseCardinality").GetInt64());
        Assert.AreEqual(
            supplied.Outputs.Single().TotalResultCardinality,
            Observed(supplied, "responseCardinality").GetInt64());
        Assert.AreEqual(0, Observed(database, "requestBytes").GetInt64());
        Assert.IsTrue(Observed(supplied, "requestBytes").GetInt64() > 0);
    }

    private static string FailureDescription(ExecutionOutcome execution) =>
        execution.Failure is null
            ? execution.Status.ToString()
            : JsonSerializer.Serialize(execution.Failure);

    private static JsonElement Observed(BenchmarkSingleRunResult run, string property) =>
        run.Outputs.Single().Summary.GetProperty("observedApi").GetProperty(property);

    private static async Task AssertPersistedPortable(
        BenchmarkSingleRunResult run,
        IBenchmarkRunRepository repository,
        RunExportService exportService)
    {
        Assert.IsTrue(run.WasPersisted);
        Assert.IsTrue(run.WasReloaded);
        Assert.AreEqual(run.Export.Digests.ManifestDigest,
            run.DeserializedExport.Digests.ManifestDigest);
        Assert.AreEqual(run.Export.Digests.SamplesDigest,
            run.DeserializedExport.Digests.SamplesDigest);
        Assert.AreEqual(run.Export.Digests.OutputsDigest,
            run.DeserializedExport.Digests.OutputsDigest);

        var snapshot = await repository.GetSnapshotAsync(run.Manifest.RunId);
        Assert.IsNotNull(snapshot);
        Assert.AreEqual(
            run.Manifest.Dataset.DatasetInputFingerprint,
            snapshot.Manifest.Dataset.DatasetInputFingerprint);
        Assert.AreEqual(
            run.Outputs.Single().ResultDigest,
            snapshot.Outputs.Single().ResultDigest);
        var reexport = exportService.Create(
            snapshot.Manifest,
            snapshot.Samples,
            snapshot.Outputs);
        Assert.AreEqual(run.Export.Digests.SamplesDigest, reexport.Digests.SamplesDigest);
        Assert.AreEqual(run.Export.Digests.OutputsDigest, reexport.Digests.OutputsDigest);
    }

    private static Process StartApiProcess(string connectionString, int port)
    {
        var backendAssembly = typeof(GraphsController).Assembly.Location;
        var startInfo = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = Path.GetDirectoryName(backendAssembly)!,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add(backendAssembly);
        startInfo.Environment["ASPNETCORE_URLS"] = $"http://127.0.0.1:{port}";
        startInfo.Environment["ASPNETCORE_ENVIRONMENT"] = "Production";
        startInfo.Environment["Database__ConnectionString"] = connectionString;
        startInfo.Environment["Kestrel__EndpointDefaults__Protocols"] = "Http2";
        return Process.Start(startInfo)
               ?? throw new InvalidOperationException("The real API process could not be started.");
    }

    private static HttpClient CreateHttp2Client() => new(new SocketsHttpHandler
    {
        AutomaticDecompression = DecompressionMethods.None,
        UseCookies = false
    })
    {
        Timeout = Timeout.InfiniteTimeSpan
    };

    private static async Task WaitUntilHealthy(
        HttpClient client,
        Uri baseAddress,
        Process process,
        Task<string> stdout,
        Task<string> stderr)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(20);
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (process.HasExited)
            {
                Assert.Fail(
                    $"The API process exited before becoming healthy. stdout={await stdout} stderr={await stderr}");
            }

            try
            {
                using var request = new HttpRequestMessage(
                    HttpMethod.Get,
                    new Uri(baseAddress, "api/health"))
                {
                    Version = HttpVersion.Version20,
                    VersionPolicy = HttpVersionPolicy.RequestVersionExact
                };
                using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(1));
                using var response = await client.SendAsync(request, cancellation.Token);
                if (response.IsSuccessStatusCode && response.Version.Major == 2)
                {
                    return;
                }
            }
            catch (Exception exception) when (
                exception is HttpRequestException or OperationCanceledException)
            {
                // The process is still starting; retry within the bounded wait.
            }

            await Task.Delay(100);
        }

        Assert.Fail("The HTTP/2 API process did not become healthy within 20 seconds.");
    }

    private static int ReservePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private static string RequireDisposablePostgreSql()
    {
        var connectionString = Environment.GetEnvironmentVariable(ConnectionStringEnvironmentVariable);
        var destructiveOptIn = Environment.GetEnvironmentVariable(DestructiveOptInEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(connectionString) || destructiveOptIn != "1")
        {
            Assert.Inconclusive(
                $"Set {ConnectionStringEnvironmentVariable} to a disposable database and " +
                $"{DestructiveOptInEnvironmentVariable}=1 to run the real REST/PostgreSQL journey.");
        }

        var builder = new NpgsqlConnectionStringBuilder(connectionString);
        if (string.IsNullOrWhiteSpace(builder.Database) ||
            !builder.Database.Contains("test", StringComparison.OrdinalIgnoreCase))
        {
            Assert.Inconclusive("The opt-in database name must contain 'test'.");
        }

        return connectionString;
    }

    private static async Task<string> RequirePostgreSqlVersion(string connectionString)
    {
        try
        {
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync();
            return connection.PostgreSqlVersion.ToString();
        }
        catch (NpgsqlException exception)
        {
            Assert.Inconclusive($"The opt-in PostgreSQL database is unavailable: {exception.Message}");
            throw;
        }
    }

    private static async Task<DatabaseResetTargetExpectation> RequireResetTargetExpectation(
        string connectionString)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            DatabaseResetTargetIdentity.ProbeSql,
            connection);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.IsTrue(await reader.ReadAsync());
        var actualDatabaseName = reader.GetString(0);
        var configuredDatabaseName = new NpgsqlConnectionStringBuilder(connectionString).Database;
        Assert.AreEqual(configuredDatabaseName, actualDatabaseName);
        return new DatabaseResetTargetExpectation(
            actualDatabaseName,
            DatabaseResetTargetIdentity.ComputeFingerprint(reader.GetString(1)));
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
