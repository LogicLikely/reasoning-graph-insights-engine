using System.Text.RegularExpressions;

namespace backend.Tests.Insights.Persistence;

[TestClass]
public class BenchmarkSchemaSqlTests
{
    [TestMethod]
    public void Schema_IsIdempotentResetSafeAndContainsOnlyInternalRelationships()
    {
        var sql = ReadRepositoryFile("backend", "data", "sql", "benchmark_schema.sql");

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
                @"^\s*(DROP\s+(TABLE|SCHEMA)|TRUNCATE|DELETE\s+FROM)\b",
                RegexOptions.IgnoreCase | RegexOptions.Multiline),
            "Idempotent initialization must not delete existing history.");
    }

    [TestMethod]
    public void Schema_PreservesCanonicalPayloadsNormalizedSelectorsAndAppendOrder()
    {
        var sql = ReadRepositoryFile("backend", "data", "sql", "benchmark_schema.sql");

        StringAssert.Contains(sql, "manifest_json jsonb NOT NULL");
        StringAssert.Contains(sql, "sample_json jsonb NOT NULL");
        StringAssert.Contains(sql, "output_json jsonb NOT NULL");
        StringAssert.Contains(sql, "entry_id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY");
        StringAssert.Contains(sql, "dataset_input_fingerprint text NOT NULL");
        StringAssert.Contains(sql, "algorithm_semantic_identity text NOT NULL");
        StringAssert.Contains(sql, "parameter_digest text NOT NULL");
        StringAssert.Contains(sql, "profile_key text NOT NULL");
        StringAssert.Contains(sql, "environment_profile text NOT NULL");
        StringAssert.Contains(sql, "build_mode text NOT NULL");
        StringAssert.Contains(sql, "actual_strategy text");
        StringAssert.Contains(sql, "sample_mode text NOT NULL");
        StringAssert.Contains(sql, "measurement_units jsonb NOT NULL");
        StringAssert.Contains(sql, "timing_boundary_provenance text NOT NULL");
        StringAssert.Contains(sql, "ck_benchmark_samples_timing_boundary_provenance");
        StringAssert.Contains(
            sql,
            "sample_json->>'timingBoundaryProvenance' = timing_boundary_provenance");
        StringAssert.Contains(sql, "sample_json ? 'timingBoundaryProvenance'");
        StringAssert.Contains(sql, "sample_json ? 'operationCounters'");
        StringAssert.Contains(sql, "ck_benchmark_runs_failure_matches_status");
        StringAssert.Contains(sql, "ck_benchmark_runs_completion_matches_status");
        StringAssert.Contains(sql, "ck_benchmark_runs_completion_not_before_start");
        StringAssert.Contains(sql, "ck_benchmark_samples_failure_matches_status");
        StringAssert.Contains(sql, "ck_benchmark_outputs_failure_matches_status");
        StringAssert.Contains(sql, "ck_benchmark_runs_sample_mode");
        StringAssert.Contains(sql, "manifest_json->>'profileKey' = profile_key");
        StringAssert.Contains(
            sql,
            "manifest_json#>>'{strategy,used}' IS NOT DISTINCT FROM actual_strategy");
        StringAssert.Contains(sql, "manifest_json#>>'{samplingPolicy,sampleMode}' = sample_mode");
        StringAssert.Contains(sql, "jsonb_array_length(output_json->'items') <= 100");
    }

    [TestMethod]
    public void Schema_ReconcilesPreGoal2RunComparisonSelectorsWithoutGuessingWarmState()
    {
        var sql = ReadRepositoryFile("backend", "data", "sql", "benchmark_schema.sql");
        var reconciliationStart = sql.IndexOf("DO $phase4_goal2_runs$", StringComparison.Ordinal);
        Assert.IsTrue(reconciliationStart > 0);
        var reconciliationSql = sql[reconciliationStart..];

        StringAssert.Contains(reconciliationSql, "AND column_name = 'profile_key'");
        StringAssert.Contains(reconciliationSql, "ADD COLUMN profile_key text");
        StringAssert.Contains(reconciliationSql, "ADD COLUMN actual_strategy text");
        StringAssert.Contains(reconciliationSql, "ADD COLUMN sample_mode text");
        StringAssert.Contains(reconciliationSql, "profile_key = 'legacy-unspecified'");
        StringAssert.Contains(reconciliationSql, "sample_mode = 'legacy-unspecified'");
        StringAssert.Contains(reconciliationSql, "'{profileKey}'");
        StringAssert.Contains(reconciliationSql, "'{samplingPolicy,sampleMode}'");
        Assert.IsFalse(
            Regex.IsMatch(
                reconciliationSql,
                @"^\s*(TRUNCATE|DELETE\s+FROM)\b",
                RegexOptions.IgnoreCase | RegexOptions.Multiline),
            "Goal 2 reconciliation must preserve benchmark history.");
    }

    [TestMethod]
    public void Schema_ReconcilesComparisonIndexToEveryDefaultCompatibilitySelector()
    {
        var sql = ReadRepositoryFile("backend", "data", "sql", "benchmark_schema.sql");
        StringAssert.Contains(sql, "DO $phase4_goal2_comparison_index$");
        StringAssert.Contains(sql, "current_columns IS DISTINCT FROM expected_columns");
        StringAssert.Contains(sql, "DROP INDEX IF EXISTS benchmark.ix_benchmark_runs_comparison;");

        var create = Regex.Match(
            sql,
            @"CREATE\s+INDEX\s+ix_benchmark_runs_comparison\s+ON\s+benchmark\.runs\s*\((?<columns>[^)]*)\)",
            RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant);
        Assert.IsTrue(create.Success);
        CollectionAssert.AreEqual(
            new[]
            {
                "scenario_key",
                "profile_key",
                "operation_key",
                "dataset_input_fingerprint",
                "algorithm_semantic_identity",
                "parameter_digest",
                "actual_strategy",
                "environment_profile",
                "build_mode",
                "sample_mode",
                "measurement_units"
            },
            create.Groups["columns"].Value
                .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries));
    }

    [TestMethod]
    public void Schema_ReconcilesLegacySampleMeasurementEvidenceWithoutDeletingRows()
    {
        var sql = ReadRepositoryFile("backend", "data", "sql", "benchmark_schema.sql");
        var reconciliationStart = sql.IndexOf("-- Phase 4", StringComparison.Ordinal);
        Assert.IsTrue(reconciliationStart > 0);
        var reconciliationSql = sql[reconciliationStart..];

        StringAssert.Contains(reconciliationSql, "DO $phase4_samples$");
        StringAssert.Contains(
            reconciliationSql,
            "AND column_name = 'timing_boundary_provenance'");
        StringAssert.Contains(
            reconciliationSql,
            "ADD COLUMN timing_boundary_provenance text;");
        StringAssert.Contains(
            reconciliationSql,
            "timing_boundary_provenance = 'estimated'");
        StringAssert.Contains(
            reconciliationSql,
            "'{timingBoundaryProvenance}'");
        StringAssert.Contains(reconciliationSql, "\"estimated\"'::jsonb");
        StringAssert.Contains(reconciliationSql, "'{operationCounters}'");
        StringAssert.Contains(reconciliationSql, "'null'::jsonb");
        StringAssert.Contains(
            reconciliationSql,
            "ALTER COLUMN timing_boundary_provenance SET NOT NULL;");
        Assert.IsFalse(
            Regex.IsMatch(
                reconciliationSql,
                @"^\s*(TRUNCATE|DELETE\s+FROM)\b",
                RegexOptions.IgnoreCase | RegexOptions.Multiline),
            "Phase 4 reconciliation must preserve benchmark history.");
    }

    [TestMethod]
    public void Schema_ReconcilesDroppedGraphMapAdmissionDataWithoutDeletingHistory()
    {
        var sql = ReadRepositoryFile("backend", "data", "sql", "benchmark_schema.sql");
        var reconciliationStart = sql.IndexOf("-- Phase 3.5", StringComparison.Ordinal);
        Assert.IsTrue(reconciliationStart > 0);
        var freshSchemaSql = sql[..reconciliationStart];
        var reconciliationSql = sql[reconciliationStart..];

        Assert.IsFalse(Regex.IsMatch(
            freshSchemaSql,
            @"\bvisualization_admission\s+text\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant));
        StringAssert.Contains(
            freshSchemaSql,
            "CONSTRAINT ck_benchmark_samples_payload_identity CHECK (");
        StringAssert.Contains(
            freshSchemaSql,
            "CONSTRAINT ck_benchmark_outputs_payload_identity CHECK (");
        StringAssert.Contains(reconciliationSql, "DO $phase35_samples$");
        StringAssert.Contains(reconciliationSql, "DO $phase35_outputs$");
        Assert.AreEqual(
            2,
            Regex.Matches(
                reconciliationSql,
                @"IF\s+EXISTS\s*\(\s*SELECT\s+1\s+FROM\s+information_schema\.columns",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant).Count);
        StringAssert.Contains(reconciliationSql, "AND table_name = 'samples'");
        StringAssert.Contains(reconciliationSql, "AND table_name = 'outputs'");
        Assert.AreEqual(
            2,
            Regex.Matches(
                reconciliationSql,
                @"AND\s+column_name\s+=\s+'visualization_admission'",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant).Count);
        StringAssert.Contains(
            reconciliationSql,
            "SET sample_json = sample_json - 'visualizationAdmission' - 'warnings'");
        StringAssert.Contains(
            reconciliationSql,
            "SET output_json = output_json - 'visualizationAdmission' - 'warnings'");
        StringAssert.Contains(
            reconciliationSql,
            "DROP COLUMN IF EXISTS visualization_admission;");
        StringAssert.Contains(
            reconciliationSql,
            "DROP CONSTRAINT IF EXISTS ck_benchmark_samples_payload_identity;");
        StringAssert.Contains(
            reconciliationSql,
            "DROP CONSTRAINT IF EXISTS ck_benchmark_outputs_payload_identity;");
        Assert.AreEqual(
            2,
            Regex.Matches(
                reconciliationSql,
                @"DROP\s+COLUMN\s+IF\s+EXISTS\s+visualization_admission",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant).Count);
    }

    [TestMethod]
    public void GraphResetSql_DoesNotMentionOrMutateBenchmarkStorage()
    {
        var resetSql = ReadRepositoryFile("backend", "data", "sql", "insights_seed.sql");

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
