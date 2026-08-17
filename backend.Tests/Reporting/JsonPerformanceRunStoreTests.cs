using Backend.Reporting;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace backend.Tests.Reporting;

[TestClass]
public class JsonPerformanceRunStoreTests
{
    private string _temporaryDirectory = null!;
    private string _reportPath = null!;

    [TestInitialize]
    public void Initialize()
    {
        _temporaryDirectory = Path.Combine(
            Path.GetTempPath(),
            $"reasoning-graph-performance-tests-{Guid.NewGuid():N}");
        _reportPath = Path.Combine(_temporaryDirectory, "nested", "performance-runs.json");
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(_temporaryDirectory))
        {
            Directory.Delete(_temporaryDirectory, recursive: true);
        }
    }

    [TestMethod]
    public async Task AppendAsync_CreatesVersionedReportAndAssignsSequentialRunNumbers()
    {
        using var store = new JsonPerformanceRunStore(_reportPath);

        var first = await store.AppendAsync(CreateRun("minimal-counter-set") with
        {
            RunNumber = 100,
            Details = new JsonObject
            {
                ["subsetEvaluations"] = 8
            }
        });
        var second = await store.AppendAsync(CreateRun("robustness-ranking"));

        Assert.AreEqual(1L, first.RunNumber);
        Assert.AreEqual(2L, second.RunNumber);
        Assert.IsTrue(File.Exists(_reportPath));

        var report = await ReadReportAsync();

        Assert.IsNotNull(report);
        Assert.AreEqual(PerformanceReportDocument.CurrentSchemaVersion, report.SchemaVersion);
        Assert.AreEqual(2, report.Runs.Count);
        Assert.AreEqual(1L, report.Runs[0].RunNumber);
        Assert.AreEqual("minimal-counter-set", report.Runs[0].Algorithm.Name);
        Assert.AreEqual(8, report.Runs[0].Details["subsetEvaluations"]!.GetValue<int>());
        Assert.AreEqual(2L, report.Runs[1].RunNumber);
        Assert.AreEqual("robustness-ranking", report.Runs[1].Algorithm.Name);

        var files = Directory.GetFiles(Path.GetDirectoryName(_reportPath)!);
        CollectionAssert.AreEqual(new[] { _reportPath }, files);
    }

    [TestMethod]
    public async Task AppendAsync_PreservesExistingFileWhenJsonIsInvalid()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_reportPath)!);
        await File.WriteAllTextAsync(_reportPath, "{ invalid json");
        using var store = new JsonPerformanceRunStore(_reportPath);

        var exception = await Assert.ThrowsExceptionAsync<InvalidDataException>(() =>
            store.AppendAsync(CreateRun("least-robust-node")));

        StringAssert.Contains(exception.Message, "not valid JSON");
        Assert.AreEqual("{ invalid json", await File.ReadAllTextAsync(_reportPath));
    }

    [TestMethod]
    public async Task AppendAsync_RejectsUnsupportedSchemaVersionWithoutChangingFile()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_reportPath)!);
        const string existingJson = """
            {
              "schemaVersion": 999,
              "runs": []
            }
            """;
        await File.WriteAllTextAsync(_reportPath, existingJson);
        using var store = new JsonPerformanceRunStore(_reportPath);

        var exception = await Assert.ThrowsExceptionAsync<InvalidDataException>(() =>
            store.AppendAsync(CreateRun("evidence-impact-ranking")));

        StringAssert.Contains(exception.Message, "unsupported schema version 999");
        Assert.AreEqual(existingJson, await File.ReadAllTextAsync(_reportPath));
    }

    [TestMethod]
    public async Task AppendAsync_UsesHighestExistingRunNumberWhenAssigningNextNumber()
    {
        using var store = new JsonPerformanceRunStore(_reportPath);
        await store.AppendAsync(CreateRun("first"));

        var report = await ReadReportAsync();
        report.Runs.Add(CreateRun("imported") with { RunNumber = 8 });
        await File.WriteAllTextAsync(
            _reportPath,
            JsonSerializer.Serialize(report, SerializerOptions));

        var stored = await store.AppendAsync(CreateRun("next"));

        Assert.AreEqual(9L, stored.RunNumber);
    }

    private async Task<PerformanceReportDocument> ReadReportAsync()
    {
        await using var stream = File.OpenRead(_reportPath);
        return (await JsonSerializer.DeserializeAsync<PerformanceReportDocument>(
            stream,
            SerializerOptions))!;
    }

    private static PerformanceRunRecord CreateRun(string algorithmName)
    {
        return new PerformanceRunRecord
        {
            StartedAtUtc = new DateTimeOffset(2026, 8, 16, 12, 30, 0, TimeSpan.Zero),
            Algorithm = new PerformanceAlgorithmInfo
            {
                Name = algorithmName,
                Implementation = PerformanceAlgorithmImplementations.Current,
                CalculationModel = "legacy-lr"
            },
            Build = new PerformanceBuildInfo
            {
                GitCommit = "abc123",
                Dirty = false,
                Configuration = "Release",
                DotNetVersion = ".NET 8.0",
                OperatingSystem = "Test OS",
                ProcessArchitecture = "Arm64",
                LogicalProcessorCount = 8,
                ServerGarbageCollection = false
            },
            Graph = new PerformanceGraphInfo
            {
                Slug = "balanced-1k",
                Type = "balanced",
                NodeCount = 1_000,
                EdgeCount = 999,
                NodeKindCounts = new Dictionary<string, int>
                {
                    ["root"] = 1,
                    ["claim"] = 999
                }
            },
            Invocation = new PerformanceInvocationInfo
            {
                DataSource = "database",
                TargetNodeId = "R1"
            },
            Timing = new PerformanceTimingInfo
            {
                LoadElapsedMilliseconds = 1.25,
                ComputeElapsedMilliseconds = 4.5,
                OperationElapsedMilliseconds = 6.0
            },
            Resources = new PerformanceResourceInfo
            {
                CpuTimeMilliseconds = 3.5,
                AllocatedBytes = 1_024,
                Gen0Collections = 0,
                Gen1Collections = 0,
                Gen2Collections = 0
            },
            Outcome = new PerformanceOutcomeInfo
            {
                Status = PerformanceRunStatuses.Completed,
                ResultCount = 1
            }
        };
    }

    private static readonly JsonSerializerOptions SerializerOptions =
        new(JsonSerializerDefaults.Web)
        {
            WriteIndented = true
        };
}
