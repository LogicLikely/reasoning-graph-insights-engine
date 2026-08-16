using System.Globalization;
using System.Text.Json;
using Backend.Extensions;
using Backend.Insights.Benchmarking;
using Backend.Insights.Export;
using Backend.Insights.Persistence;
using Backend.Seeding;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;

namespace Backend.BenchmarkRunner;

internal static class Program
{
    private const string BenchmarkPostgreSqlConnectionStringEnvironmentVariable =
        "LOGICLIKELY_BENCHMARK_POSTGRES_CONNECTION_STRING";
    private const string DestructiveDatasetResetEnvironmentVariable =
        "LOGICLIKELY_ALLOW_DESTRUCTIVE_POSTGRES_BENCHMARK";

    public static async Task<int> Main(string[] args)
    {
        try
        {
            var command = args.FirstOrDefault() ?? "list";
            var options = ParseOptions(args.Skip(1).ToArray());
            var profileKey = Value(options, "profile") ?? BenchmarkProfiles.QuickKey;
            if (command == "list")
            {
                List(profileKey);
                return 0;
            }

            if (command != "run")
            {
                Usage();
                return 2;
            }

            // Refuse configuration-only profiles before constructing clients,
            // initializing persistence, contacting PostgreSQL, or performing
            // any other externally visible setup.
            BenchmarkProfiles.RequireExecutable(profileKey);

            using var cancellation = new CancellationTokenSource();
            Console.CancelKeyPress += (_, eventArgs) =>
            {
                eventArgs.Cancel = true;
                cancellation.Cancel();
            };

            var persist = options.ContainsKey("persist");
            var apiBaseUrl = ParseApiBaseUrl(Value(options, "api-base-url"));
            var explicitBrowserApiBaseUrl = ParseHttpUrl(
                Value(options, "browser-api-base-url"),
                "--browser-api-base-url");
            var browserApiBaseUrl = explicitBrowserApiBaseUrl ??
                BrowserCompatibleDefault(apiBaseUrl);
            var browserHarnessUrl = ParseHttpUrl(
                Value(options, "browser-harness-url"),
                "--browser-harness-url");
            if (options.ContainsKey("install-datasets") && apiBaseUrl is null)
            {
                throw new ArgumentException("--install-datasets requires --api-base-url.");
            }

            var postgreSqlVersion = "not-used";
            DatabaseResetTargetExpectation? resetTargetExpectation = null;
            if (apiBaseUrl is not null)
            {
                var apiDatabaseConnectionString = Environment.GetEnvironmentVariable(
                    BenchmarkPostgreSqlConnectionStringEnvironmentVariable);
                if (string.IsNullOrWhiteSpace(apiDatabaseConnectionString))
                {
                    throw new ArgumentException(
                        $"REST runs require {BenchmarkPostgreSqlConnectionStringEnvironmentVariable} " +
                        "for actual PostgreSQL environment identity.");
                }

                if (options.ContainsKey("install-datasets"))
                {
                    var expectedDatabaseName = ValidateDisposableDatasetReset(
                        apiBaseUrl,
                        apiDatabaseConnectionString);
                    resetTargetExpectation = await ReadResetTargetExpectationAsync(
                        apiDatabaseConnectionString,
                        expectedDatabaseName,
                        cancellation.Token);
                }

                postgreSqlVersion = await ReadPostgreSqlVersionAsync(
                    apiDatabaseConnectionString,
                    cancellation.Token);
            }

            IBenchmarkRunRepository? repository = null;
            ServiceProvider? services = null;
            if (persist)
            {
                var host = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
                {
                    ContentRootPath = AppContext.BaseDirectory,
                    Args = []
                });
                host.Services.AddApplicationServices(host.Configuration);
                services = host.Services.BuildServiceProvider();
                var initializer = services.GetRequiredService<IBenchmarkSchemaInitializer>();
                await initializer.InitializeAsync(cancellation.Token);
                repository = services.GetRequiredService<IBenchmarkRunRepository>();
            }

            await using var _ = services;
            var parameters = Value(options, "parameters") is { } rawParameters
                ? JsonDocument.Parse(rawParameters).RootElement.Clone()
                : (JsonElement?)null;
            var selection = new BenchmarkRunSelection(
                profileKey,
                Value(options, "scenario"),
                Value(options, "operation"),
                Value(options, "dataset"),
                parameters,
                Value(options, "strategy"),
                ParseTimeout(Value(options, "timeout")),
                persist);
            using var apiClient = apiBaseUrl is null
                ? null
                : CreateApiClient();
            IBenchmarkOperationExecutor? restExecutor = apiClient is null
                ? null
                : new RestApiBenchmarkOperationExecutor(
                    apiClient,
                    new RestApiJourneyOptions(
                        apiBaseUrl!,
                        RestApiJourneyBoundary.RealProcessNetwork,
                        postgreSqlVersion));
            var datasetInstaller = apiClient is not null && options.ContainsKey("install-datasets")
                ? new RestBenchmarkDatasetInstaller(
                    apiClient,
                    apiBaseUrl!,
                    resetTargetExpectation ?? throw new InvalidOperationException(
                        "Dataset reset identity was not established."))
                : null;
            IBenchmarkOperationExecutor? browserExecutor = browserHarnessUrl is null
                ? null
                : new BrowserBenchmarkOperationExecutor(
                    new PlaywrightBrowserJourneyDriver(
                        new PublishedPlaywrightBrowserJourneyCommandProvider(
                            Value(options, "browser-driver"),
                            Value(options, "frontend-dir"))),
                    new BrowserJourneyOptions(
                        browserHarnessUrl,
                        browserApiBaseUrl,
                        postgreSqlVersion));
            var executor = new BenchmarkOperationExecutorRouter(
                new BenchmarkOperationExecutor(),
                restExecutor,
                datasetInstaller,
                ParseSetupTimeout(Value(options, "setup-timeout")),
                browserExecutor);
            if (apiBaseUrl is not null)
            {
                Console.WriteLine($"api-boundary\treal-process-network-http2\t{apiBaseUrl.AbsoluteUri}");
            }
            if (browserHarnessUrl is not null)
            {
                Console.WriteLine($"browser-boundary\tplaywright-real-process\t{browserHarnessUrl.AbsoluteUri}");
                Console.WriteLine(
                    browserApiBaseUrl is null
                        ? "browser-api-boundary\tnot-configured\tgraph-browser-scenarios-will-fail-setup"
                        : $"browser-api-boundary\tbrowser-fetch-configured-{browserApiBaseUrl.Scheme}\t{browserApiBaseUrl.AbsoluteUri}");
            }

            var runner = new SerialBenchmarkRunner(
                executor,
                repository,
                new RunExportService());
            var result = await runner.RunAsync(selection, cancellation.Token);
            var exportDirectory = Value(options, "export-dir");
            if (exportDirectory is not null) Directory.CreateDirectory(exportDirectory);

            foreach (var run in result.Runs)
            {
                Console.WriteLine(
                    $"{run.Scenario.Key}\t{Token(run.Manifest.Execution.Status)}\t" +
                    $"requested={run.Manifest.Strategy.Requested ?? "none"}\t" +
                    $"used={run.Manifest.Strategy.Used ?? "none"}\t{run.Manifest.RunId:D}");
                if (exportDirectory is not null)
                {
                    var path = Path.Combine(exportDirectory, $"{run.Manifest.RunId:D}.json");
                    await File.WriteAllTextAsync(path, run.ExportJson, cancellation.Token);
                    Console.WriteLine($"export\t{path}");
                }
            }

            return result.Runs.Any(run => run.Manifest.Execution.Status is
                Backend.Insights.Contracts.ExecutionStatus.Failed or
                Backend.Insights.Contracts.ExecutionStatus.TimedOut or
                Backend.Insights.Contracts.ExecutionStatus.Cancelled or
                Backend.Insights.Contracts.ExecutionStatus.Crashed) ? 1 : 0;
        }
        catch (OperationCanceledException)
        {
            Console.Error.WriteLine("Benchmark run cancelled.");
            return 130;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception.Message);
            return 2;
        }
    }

    private static void List(string profileKey)
    {
        var profile = BenchmarkProfiles.Get(profileKey);
        foreach (var available in BenchmarkProfiles.All)
        {
            Console.WriteLine(
                $"available-profile\t{available.Key}\t" +
                (available.ExecutionEnabled ? "executable" : "configuration-validation-only"));
        }

        Console.WriteLine($"profile\t{profile.Key}\t{profile.Description}");
        Console.WriteLine(
            $"iterations\twarmup={profile.WarmupIterations}\tmeasured={profile.MeasuredIterations}");
        Console.WriteLine(
            $"sampling\tmode={profile.SampleMode}\twarmup={profile.WarmupPolicy}\t" +
            $"samples={profile.SamplePolicy}\tjit={profile.JitPolicy}\tcache={profile.CachePolicy}");
        Console.WriteLine($"reset-disclosure\t{profile.ResetDisclosure}");
        foreach (var operation in Backend.Insights.Contracts.InsightOperationRegistry.Operations)
        {
            Console.WriteLine($"operation\t{operation.Key}\t{operation.SemanticIdentity}");
        }

        foreach (var scenario in BenchmarkScenarioRegistry.ForProfile(profileKey))
        {
            var state = scenario.SkipReason is null
                ? scenario.ExecutionTarget switch
                {
                    BenchmarkScenarioExecutionTarget.RestDatabaseLoaded => "supported:rest-database-loaded",
                    BenchmarkScenarioExecutionTarget.RestSuppliedGraph => "supported:rest-supplied-graph",
                    BenchmarkScenarioExecutionTarget.Browser =>
                        $"supported:browser:{scenario.BrowserJourney!.Action}",
                    _ => scenario.RequiresIsolation ? "supported:isolated" : "supported:in-process"
                }
                : $"skipped:{scenario.SkipReason.Code}";
            Console.WriteLine(
                $"scenario\t{scenario.Key}\t{scenario.OperationKey}\t{scenario.DatasetId}\t{state}\t" +
                (scenario.SkipReason?.Message ?? scenario.Description));
        }
    }

    private static Dictionary<string, string?> ParseOptions(string[] args)
    {
        var values = new Dictionary<string, string?>(StringComparer.Ordinal);
        for (var index = 0; index < args.Length; index++)
        {
            if (!args[index].StartsWith("--", StringComparison.Ordinal))
            {
                throw new ArgumentException($"Unexpected argument '{args[index]}'.");
            }

            var key = args[index][2..];
            string? value = null;
            if (index + 1 < args.Length && !args[index + 1].StartsWith("--", StringComparison.Ordinal))
            {
                value = args[++index];
            }

            values.Add(key, value);
        }

        return values;
    }

    private static string? Value(IReadOnlyDictionary<string, string?> options, string key) =>
        options.TryGetValue(key, out var value) ? value : null;

    private static TimeSpan? ParseTimeout(string? value)
    {
        if (value is null) return null;
        // A bare CLI number is documented as seconds. TimeSpan.TryParse("90")
        // treats it as 90 days, which overflows the browser protocol's bounded
        // millisecond field; parse the unadorned numeric form first.
        if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds) &&
            seconds > 0)
        {
            return TimeSpan.FromSeconds(seconds);
        }

        if (TimeSpan.TryParse(value, CultureInfo.InvariantCulture, out var duration) &&
            duration > TimeSpan.Zero)
        {
            return duration;
        }

        throw new ArgumentException("--timeout must be a positive TimeSpan or seconds value.");
    }

    private static TimeSpan ParseSetupTimeout(string? value) =>
        ParseTimeout(value) ?? TimeSpan.FromMinutes(10);

    private static Uri? ParseApiBaseUrl(string? value)
        => ParseHttpUrl(value, "--api-base-url");

    private static Uri? BrowserCompatibleDefault(Uri? apiBaseUrl) =>
        apiBaseUrl?.Scheme == Uri.UriSchemeHttps ? apiBaseUrl : null;

    private static Uri? ParseHttpUrl(string? value, string optionName)
    {
        if (value is null) return null;
        if (Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
            (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
        {
            return uri;
        }

        throw new ArgumentException($"{optionName} must be an absolute HTTP or HTTPS URI.");
    }

    private static HttpClient CreateApiClient()
    {
        var handler = new SocketsHttpHandler
        {
            AutomaticDecompression = System.Net.DecompressionMethods.None,
            UseCookies = false
        };
        return new HttpClient(handler)
        {
            Timeout = Timeout.InfiniteTimeSpan
        };
    }

    private static string ValidateDisposableDatasetReset(
        Uri apiBaseUrl,
        string connectionString)
    {
        if (Environment.GetEnvironmentVariable(DestructiveDatasetResetEnvironmentVariable) != "1")
        {
            throw new ArgumentException(
                $"--install-datasets requires {DestructiveDatasetResetEnvironmentVariable}=1.");
        }

        if (!apiBaseUrl.IsLoopback)
        {
            throw new ArgumentException(
                "Dataset installation is restricted to a loopback API endpoint.");
        }

        var builder = new NpgsqlConnectionStringBuilder(connectionString);
        if (string.IsNullOrWhiteSpace(builder.Database) ||
            !(builder.Database.Contains("test", StringComparison.OrdinalIgnoreCase) ||
              builder.Database.Contains("benchmark", StringComparison.OrdinalIgnoreCase) ||
              builder.Database.Contains("disposable", StringComparison.OrdinalIgnoreCase)))
        {
            throw new ArgumentException(
                "The opted-in disposable database name must contain 'test', 'benchmark', or 'disposable'.");
        }

        return builder.Database;
    }

    private static async Task<DatabaseResetTargetExpectation> ReadResetTargetExpectationAsync(
        string connectionString,
        string expectedDatabaseName,
        CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = DatabaseResetTargetIdentity.ProbeSql;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            throw new InvalidOperationException(
                "The PostgreSQL reset target identity probe returned no row.");
        }

        var actualDatabaseName = reader.GetString(0);
        var identityTuple = reader.GetString(1);
        if (!string.Equals(
                actualDatabaseName,
                expectedDatabaseName,
                StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "The disposable connection string database name did not match its connected PostgreSQL target.");
        }

        return new DatabaseResetTargetExpectation(
            expectedDatabaseName,
            DatabaseResetTargetIdentity.ComputeFingerprint(identityTuple));
    }

    private static async Task<string> ReadPostgreSqlVersionAsync(
        string connectionString,
        CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        return connection.PostgreSqlVersion.ToString();
    }

    private static string Token<TEnum>(TEnum value) where TEnum : struct, Enum =>
        JsonSerializer.Serialize(value, Backend.Insights.Contracts.CanonicalJson.CreateSerializerOptions()).Trim('"');

    private static void Usage() => Console.Error.WriteLine(
        "Usage: backend.BenchmarkRunner list [--profile quick|standard|cold|authoritative] | run [--profile quick|standard|cold] [--scenario KEY] [--operation KEY] [--dataset ID] [--parameters JSON] [--strategy NAME] [--timeout SECONDS] [--api-base-url URL] [--browser-api-base-url URL] [--browser-harness-url URL] [--browser-driver PATH] [--frontend-dir PATH] [--install-datasets] [--setup-timeout SECONDS] [--persist] [--export-dir PATH]. The authoritative profile is configuration/list/validation-only and cannot execute before Phase 6. --api-base-url is the exact-HTTP/2 REST/preparation boundary; graph-browser runs also require a browser-compatible --browser-api-base-url (HTTPS may reuse --api-base-url). REST and graph-browser runs require LOGICLIKELY_BENCHMARK_POSTGRES_CONNECTION_STRING; destructive setup also requires LOGICLIKELY_ALLOW_DESTRUCTIVE_POSTGRES_BENCHMARK=1 and a loopback disposable database.");
}
