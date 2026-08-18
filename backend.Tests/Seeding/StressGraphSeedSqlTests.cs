namespace backend.Tests.Seeding;

[TestClass]
public class StressGraphSeedSqlTests
{
    [TestMethod]
    public void Sql_CalibratesEveryNonDeepRootAndObjectionFromNamedParameters()
    {
        var sql = ReadSeedSql();

        StringAssert.Contains(sql, "@InitialTargetLogOdds");
        StringAssert.Contains(sql, "@EffectiveCounterContributionLogOdds");
        StringAssert.Contains(sql, "), counter_paths_to_root AS (");
        StringAssert.Contains(sql, "), calibration_targets AS MATERIALIZED (");
        StringAssert.Contains(sql, "), calibrated_priors AS (");
        StringAssert.Contains(sql, "), calibrated_nodes AS (");
        StringAssert.Contains(sql, "LEFT JOIN contributions");
        StringAssert.Contains(sql, "LEFT JOIN counter_paths_to_root");
        StringAssert.Contains(
            sql,
            "COALESCE(contributions.total_log_likelihood, 0)");
        StringAssert.Contains(
            sql,
            "AND @Shape IN ('balanced', 'wide', 'shared-diamond')");
        StringAssert.Contains(
            sql,
            "prior_odds = calibrated_nodes.calibrated_prior_odds");
        StringAssert.Contains(
            sql,
            "posterior_odds = calibrated_nodes.calibrated_posterior_odds");
        StringAssert.Contains(
            sql,
            "node.graph_id = calibrated_nodes.graph_id");
        StringAssert.Contains(
            sql,
            "node.id = calibrated_nodes.id");
        Assert.IsFalse(
            sql.Contains("importance_to_parent", StringComparison.Ordinal));
        Assert.IsFalse(
            sql.Contains("@Shape = 'deep'", StringComparison.Ordinal));
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
    public void Sql_PreservesAuthoredEvidenceAndObjectionBayesFactors()
    {
        var normalizedSql = ReadSeedSql()
            .Replace("\r\n", "\n", StringComparison.Ordinal);

        StringAssert.Contains(
            normalizedSql,
            "WHEN payload.kind IN ('evidence', 'objection') THEN 0");
        StringAssert.Contains(
            normalizedSql,
            "END,\n    payload.prior_odds,");
        StringAssert.Contains(
            normalizedSql,
            "node.id = 'n-00000'\n          OR node.kind = 'objection'");
        StringAssert.Contains(
            normalizedSql,
            "calibrated_priors.posterior_odds -\n                    calibrated_priors.prior_odds");
        StringAssert.Contains(
            normalizedSql,
            "ln(edge.probability_given_parent::double precision) -");
        StringAssert.Contains(
            normalizedSql,
            "ln(edge.probability_given_not_parent::double precision)");
    }

    private static string ReadSeedSql()
        => File.ReadAllText(Path.Combine(
            AppContext.BaseDirectory,
            "data",
            "sql",
            "insights_stress_seed.sql"));
}
