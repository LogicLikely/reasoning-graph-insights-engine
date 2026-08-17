namespace backend.Tests.Seeding;

[TestClass]
public class InsightsSeedSqlTests
{
    [TestMethod]
    public void Sql_DefinesEdgeProbabilitiesAndNeutralEvidencePriors()
    {
        var sql = File.ReadAllText(Path.Combine(
            AppContext.BaseDirectory,
            "data",
            "sql",
            "insights_seed.sql"));

        Assert.IsFalse(sql.Contains("importance_to_parent", StringComparison.Ordinal));
        StringAssert.Contains(
            sql,
            "probability_given_parent numeric(10,9) DEFAULT 0.5 NOT NULL");
        StringAssert.Contains(
            sql,
            "probability_given_not_parent numeric(10,9) DEFAULT 0.5 NOT NULL");
        StringAssert.Contains(sql, "CONSTRAINT ck_edges_probability_given_parent");
        StringAssert.Contains(sql, "CHECK (probability_given_parent > 0 AND probability_given_parent <= 1)");
        StringAssert.Contains(sql, "CONSTRAINT ck_edges_probability_given_not_parent");
        StringAssert.Contains(sql, "CHECK (probability_given_not_parent > 0 AND probability_given_not_parent <= 1)");
        StringAssert.Contains(sql, "SET prior_odds = 0");
        StringAssert.Contains(sql, "WHERE kind IN ('evidence', 'objection')");
    }
}
