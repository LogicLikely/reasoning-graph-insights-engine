using System.Text.Json;
using Backend.Extensions;
using Backend.Insights.Benchmarking;
using Backend.Insights.Export;
using Backend.Insights.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Backend.BenchmarkRunner;

internal static class Program
{
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

            using var cancellation = new CancellationTokenSource();
            Console.CancelKeyPress += (_, eventArgs) =>
            {
                eventArgs.Cancel = true;
                cancellation.Cancel();
            };

            var persist = options.ContainsKey("persist");
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
            var runner = new SerialBenchmarkRunner(
                new BenchmarkOperationExecutor(),
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
        Console.WriteLine($"profile\t{profile.Key}\t{profile.Description}");
        foreach (var operation in Backend.Insights.Contracts.InsightOperationRegistry.Operations)
        {
            Console.WriteLine($"operation\t{operation.Key}\t{operation.SemanticIdentity}");
        }

        foreach (var scenario in BenchmarkScenarioRegistry.ForProfile(profileKey))
        {
            var state = scenario.SkipReason is null
                ? scenario.RequiresIsolation ? "supported:isolated" : "supported:in-process"
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
        if (TimeSpan.TryParse(value, out var duration) && duration > TimeSpan.Zero) return duration;
        if (double.TryParse(value, out var seconds) && seconds > 0) return TimeSpan.FromSeconds(seconds);
        throw new ArgumentException("--timeout must be a positive TimeSpan or seconds value.");
    }

    private static string Token<TEnum>(TEnum value) where TEnum : struct, Enum =>
        JsonSerializer.Serialize(value, Backend.Insights.Contracts.CanonicalJson.CreateSerializerOptions()).Trim('"');

    private static void Usage() => Console.Error.WriteLine(
        "Usage: backend.BenchmarkRunner list [--profile quick] | run [--profile quick] [--scenario KEY] [--operation KEY] [--dataset ID] [--parameters JSON] [--strategy NAME] [--timeout SECONDS] [--persist] [--export-dir PATH]");
}
