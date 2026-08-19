namespace backend.Tests.Seeding;

[TestClass]
public class StressGraphSeedSqlTests
{
    [TestMethod]
    public void Sql_GeneratesBayesianNonDeepNodesFromNamedParameters()
    {
        var sql = Normalize(ReadSeedSql());

        StringAssert.Contains(sql, "@CounterCandidateCount");
        StringAssert.Contains(sql, "@InitialTargetLogOdds");
        StringAssert.Contains(sql, "@CounterLeafLogBayesFactor");
        StringAssert.Contains(
            sql,
            "series.node_index >= @NodeCount - @CounterCandidateCount\n" +
            "                THEN 'objection'");
        StringAssert.Contains(
            sql,
            "series.node_index >= @NodeCount - (2 * @CounterCandidateCount)");
        StringAssert.Contains(
            sql,
            "series.node_index < @NodeCount - @CounterCandidateCount");
        StringAssert.Contains(sql, "series.node_index % 5 = 1");
        StringAssert.Contains(
            sql,
            "WHEN @Shape = 'deep' AND series.node_index % 5 = 0 THEN 'evidence'");
        StringAssert.Contains(
            sql,
            "WHEN @Shape = 'deep' AND series.node_index % 10 = 2 THEN 'objection'");
        StringAssert.Contains(sql, "WHEN @Shape <> 'deep' THEN 50");
        StringAssert.Contains(
            sql,
            "WHEN @Shape <> 'deep' AND payload.kind = 'root'\n" +
            "            THEN @InitialTargetLogOdds");
        StringAssert.Contains(
            sql,
            "WHEN @Shape <> 'deep' AND payload.kind = 'root'\n" +
            "            THEN @InitialTargetLogOdds\n" +
            "        ELSE 0\n" +
            "    END,\n" +
            "    CASE");
        StringAssert.Contains(
            sql,
            "WHEN @Shape <> 'deep' AND payload.kind = 'objection'\n" +
            "            THEN @CounterLeafLogBayesFactor");
        Assert.IsTrue(
            CountOccurrences(sql, "THEN @InitialTargetLogOdds") >= 2,
            "The root's prior and posterior must both start at the calibrated target odds.");
        Assert.IsFalse(
            sql.Contains("@EffectiveCounterContributionLogOdds", StringComparison.Ordinal));
        Assert.IsFalse(sql.Contains("WITH RECURSIVE", StringComparison.Ordinal));
        Assert.IsFalse(sql.Contains("counter_paths_to_root", StringComparison.Ordinal));
        Assert.IsFalse(sql.Contains("calibrated_priors", StringComparison.Ordinal));
        Assert.IsFalse(sql.Contains("UPDATE public.nodes", StringComparison.Ordinal));
        Assert.IsFalse(
            sql.Contains("importance_to_parent", StringComparison.Ordinal));
    }

    [TestMethod]
    public void Sql_MapsCorpusFieldsAndCyclesEveryTenThousandNodes()
    {
        var sql = File.ReadAllText(Path.Combine(
            AppContext.BaseDirectory,
            "data",
            "sql",
            "insights_stress_seed.sql"));

        StringAssert.Contains(
            sql,
            "corpus.corpus_index = series.node_index % @CorpusEntryCount");
        StringAssert.Contains(sql, "payload.title,");
        StringAssert.Contains(sql, "payload.excerpt");
        StringAssert.Contains(sql, "payload.category,");
        StringAssert.Contains(sql, "payload.tags,");
        StringAssert.Contains(sql, "'%s %s — %s'");
        Assert.IsFalse(sql.Contains("Search marker", StringComparison.Ordinal));
    }

    [TestMethod]
    public void Sql_UsesCalibratedPropagationForNonDeepEdgesAndLegacyDeepValues()
    {
        var sql = Normalize(ReadSeedSql());

        StringAssert.Contains(sql, "@ProbabilityGivenParent");
        StringAssert.Contains(sql, "@ProbabilityGivenNotParent");
        StringAssert.Contains(
            sql,
            "WHEN @Shape = 'deep' AND generated_edges.node_index % 2 = 0 THEN 'rebut'\n" +
            "        ELSE 'support'");
        StringAssert.Contains(
            sql,
            "WHEN @Shape = 'deep' AND generated_edges.node_index % 2 = 1 THEN 0.5005\n" +
            "        WHEN @Shape = 'deep' THEN 0.4995\n" +
            "        ELSE @ProbabilityGivenParent");
        StringAssert.Contains(
            sql,
            "WHEN @Shape = 'deep' THEN 0.5\n" +
            "        ELSE @ProbabilityGivenNotParent");
        StringAssert.Contains(
            sql,
            "'support',\n    @ProbabilityGivenParent,\n    @ProbabilityGivenNotParent");
        StringAssert.Contains(
            sql,
            "WHEN generated.kind = 'evidence' THEN ln(\n" +
            "                generated.evidence_score::double precision /\n" +
            "                (100 - generated.evidence_score)::double precision\n" +
            "            )");
    }

    private static string Normalize(string value)
        => value.Replace("\r\n", "\n", StringComparison.Ordinal);

    private static int CountOccurrences(string value, string search)
        => (value.Length - value.Replace(search, string.Empty, StringComparison.Ordinal).Length) /
            search.Length;

    private static string ReadSeedSql()
        => File.ReadAllText(Path.Combine(
            AppContext.BaseDirectory,
            "data",
            "sql",
            "insights_stress_seed.sql"));
}
