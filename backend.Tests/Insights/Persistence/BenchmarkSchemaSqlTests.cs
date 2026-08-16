using System.Text.RegularExpressions;

namespace backend.Tests.Insights.Persistence;

[TestClass]
public class BenchmarkSchemaSqlTests
{
    [TestMethod]
    public void Schema_IsIdempotentResetSafeAndContainsOnlyInternalRelationships()
    {
        var sql = ReadRepositoryFile("backend", "Data", "Sql", "benchmark_schema.sql");

        StringAssert.Contains(sql, "CREATE SCHEMA IF NOT EXISTS benchmark;");
        Assert.AreEqual(
            3,
            Regex.Matches(
                sql,
                @"CREATE\s+TABLE\s+IF\s+NOT\s+EXISTS\s+benchmark\.(runs|samples|outputs)",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant).Count);
        StringAssert.Contains(sql, "REFERENCES benchmark.runs(run_id) ON DELETE CASCADE");
        Assert.IsFalse(
            Regex.IsMatch(sql, @"\bREFERENCES\s+public\.", RegexOptions.IgnoreCase),
            "Benchmark storage must never depend on resettable graph tables.");
        Assert.IsFalse(
            Regex.IsMatch(
                sql,
                @"^\s*(DROP|TRUNCATE|DELETE)\b",
                RegexOptions.IgnoreCase | RegexOptions.Multiline),
            "Idempotent initialization must not delete existing history.");
    }

    [TestMethod]
    public void Schema_PreservesCanonicalPayloadsNormalizedSelectorsAndAppendOrder()
    {
        var sql = ReadRepositoryFile("backend", "Data", "Sql", "benchmark_schema.sql");

        StringAssert.Contains(sql, "manifest_json jsonb NOT NULL");
        StringAssert.Contains(sql, "sample_json jsonb NOT NULL");
        StringAssert.Contains(sql, "output_json jsonb NOT NULL");
        StringAssert.Contains(sql, "entry_id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY");
        StringAssert.Contains(sql, "dataset_input_fingerprint text NOT NULL");
        StringAssert.Contains(sql, "algorithm_semantic_identity text NOT NULL");
        StringAssert.Contains(sql, "parameter_digest text NOT NULL");
        StringAssert.Contains(sql, "environment_profile text NOT NULL");
        StringAssert.Contains(sql, "build_mode text NOT NULL");
        StringAssert.Contains(sql, "measurement_units jsonb NOT NULL");
        StringAssert.Contains(sql, "ck_benchmark_runs_failure_matches_status");
        StringAssert.Contains(sql, "ck_benchmark_runs_completion_matches_status");
        StringAssert.Contains(sql, "ck_benchmark_runs_completion_not_before_start");
        StringAssert.Contains(sql, "ck_benchmark_samples_failure_matches_status");
        StringAssert.Contains(sql, "ck_benchmark_outputs_failure_matches_status");
        StringAssert.Contains(sql, "jsonb_array_length(output_json->'items') <= 100");
    }

    [TestMethod]
    public void GraphResetSql_DoesNotMentionOrMutateBenchmarkStorage()
    {
        var resetSql = ReadRepositoryFile("backend", "Data", "Sql", "insights_seed.sql");

        Assert.IsFalse(
            resetSql.Contains("benchmark", StringComparison.OrdinalIgnoreCase),
            "Graph reset SQL must remain unaware of benchmark history.");
        StringAssert.Contains(
            resetSql,
            "DROP TABLE IF EXISTS public.edges, public.nodes, public.graphs CASCADE;");
    }

    internal static string FindRepositoryRoot()
    {
        foreach (var startingPath in new[]
                 {
                     Directory.GetCurrentDirectory(),
                     AppContext.BaseDirectory
                 })
        {
            var directory = new DirectoryInfo(startingPath);
            while (directory is not null)
            {
                if (File.Exists(Path.Combine(directory.FullName, "reasoning-graph-insights-engine.sln")))
                {
                    return directory.FullName;
                }

                directory = directory.Parent;
            }
        }

        throw new DirectoryNotFoundException("Could not locate the repository root.");
    }

    private static string ReadRepositoryFile(params string[] segments)
        => File.ReadAllText(Path.Combine([FindRepositoryRoot(), .. segments]));
}
