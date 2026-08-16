namespace backend.Tests.Seeding;

[TestClass]
public class InsightsSeedSqlTests
{
    [TestMethod]
    public void Sql_DefinesEdgeProbabilitiesWithNeutralDefaultsAndRangeChecks()
    {
        var sql = File.ReadAllText(Path.Combine(
            AppContext.BaseDirectory,
            "data",
            "sql",
            "insights_seed.sql"));

        StringAssert.Contains(sql, "importance_to_parent numeric(10,3) NOT NULL");
        StringAssert.Contains(
            sql,
            "probability_given_parent numeric(10,9) DEFAULT 0.5 NOT NULL");
        StringAssert.Contains(
            sql,
            "probability_given_not_parent numeric(10,9) DEFAULT 0.5 NOT NULL");
        StringAssert.Contains(sql, "CONSTRAINT ck_edges_probability_given_parent");
        StringAssert.Contains(sql, "CHECK (probability_given_parent BETWEEN 0 AND 1)");
        StringAssert.Contains(sql, "CONSTRAINT ck_edges_probability_given_not_parent");
        StringAssert.Contains(sql, "CHECK (probability_given_not_parent BETWEEN 0 AND 1)");
    }
}
