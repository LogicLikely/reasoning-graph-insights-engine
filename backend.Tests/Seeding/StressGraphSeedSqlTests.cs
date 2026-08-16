namespace backend.Tests.Seeding;

[TestClass]
public class StressGraphSeedSqlTests
{
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
    public void Sql_InitializesPosteriorOddsFromPriorOddsWithoutLegacyRecalculation()
    {
        var normalizedSql = ReadSeedSql()
            .Replace("\r\n", "\n", StringComparison.Ordinal);

        StringAssert.Contains(
            normalizedSql,
            "payload.prior_odds,\n    payload.prior_odds,");
        Assert.IsFalse(
            normalizedSql.Contains(
                "UPDATE public.nodes AS node",
                StringComparison.Ordinal));
        Assert.IsFalse(
            normalizedSql.Contains(
                "path_log_likelihood",
                StringComparison.Ordinal));
    }

    private static string ReadSeedSql()
        => File.ReadAllText(Path.Combine(
            AppContext.BaseDirectory,
            "data",
            "sql",
            "insights_stress_seed.sql"));
}
