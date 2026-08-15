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
    public void Sql_UsesStreamableForwardFramesForDeepChainSuffixes()
    {
        var sql = ReadSeedSql();
        var deepSql = sql[sql.IndexOf("WITH path_positions AS (", StringComparison.Ordinal)..];
        deepSql = deepSql[..deepSql.IndexOf("WITH RECURSIVE active_paths AS (", StringComparison.Ordinal)];

        StringAssert.Contains(deepSql, "), active_totals AS MATERIALIZED (");
        StringAssert.Contains(deepSql, "), forward_active_totals AS (");
        StringAssert.Contains(
            deepSql,
            "max(forward_active_totals.active_path_through_node) FILTER (");
        StringAssert.Contains(
            deepSql,
            "max(forward_active_totals.active_count_through_node) FILTER (");
        StringAssert.Contains(
            deepSql,
            "ROWS BETWEEN UNBOUNDED PRECEDING AND CURRENT ROW");
        StringAssert.Contains(
            deepSql,
            "active_totals.total_active_path -\n            forward_active_totals.active_path_through_node");
        StringAssert.Contains(deepSql, "ln(1.001::double precision)");
        StringAssert.Contains(deepSql, "ln(0.999::double precision)");
        Assert.IsFalse(
            deepSql.Contains(
                "ROWS BETWEEN 1 FOLLOWING AND UNBOUNDED FOLLOWING",
                StringComparison.Ordinal));
    }

    [TestMethod]
    public void DeepForwardTotals_ExactlyMatchSuffixDefinition()
    {
        foreach (var nodeCount in new[] { 1, 2, 6, 37, 257, 1_024 })
        {
            var pathPositions = new decimal[nodeCount];
            var activeNodes = new bool[nodeCount];
            var pathToRoot = 0m;

            for (var nodeIndex = 0; nodeIndex < nodeCount; nodeIndex++)
            {
                pathToRoot += nodeIndex switch
                {
                    0 => 0m,
                    _ when nodeIndex % 2 == 1 => (decimal)Math.Log(1.001d),
                    _ => (decimal)Math.Log(0.999d)
                };
                pathPositions[nodeIndex] = pathToRoot;
                activeNodes[nodeIndex] = IsActiveNode(nodeIndex);
            }

            var totalActivePath = Enumerable.Range(0, nodeCount)
                .Where(nodeIndex => activeNodes[nodeIndex])
                .Sum(nodeIndex => pathPositions[nodeIndex]);
            var totalActiveCount = activeNodes.Count(isActive => isActive);
            var activePathThroughNode = 0m;
            var activeCountThroughNode = 0;

            for (var nodeIndex = 0; nodeIndex < nodeCount; nodeIndex++)
            {
                if (activeNodes[nodeIndex])
                {
                    activePathThroughNode += pathPositions[nodeIndex];
                    activeCountThroughNode++;
                }

                var expectedSuffix = Enumerable.Range(nodeIndex + 1, nodeCount - nodeIndex - 1)
                    .Where(descendantIndex => activeNodes[descendantIndex])
                    .Sum(descendantIndex =>
                        pathPositions[descendantIndex] - pathPositions[nodeIndex]);
                var forwardTotalSuffix =
                    (totalActivePath - activePathThroughNode) -
                    ((totalActiveCount - activeCountThroughNode) * pathPositions[nodeIndex]);

                Assert.AreEqual(
                    expectedSuffix,
                    forwardTotalSuffix,
                    $"Deep suffix differs at node {nodeIndex} of {nodeCount}.");
            }
        }
    }

    [TestMethod]
    public void Sql_CompressesSharedDiamondPathsIntoOneFrontierPerLevel()
    {
        var sql = ReadSeedSql();

        StringAssert.Contains(sql, "AND @Shape IN ('balanced', 'wide')");
        StringAssert.Contains(sql, "), diamond_frontiers (");
        StringAssert.Contains(sql, "), diamond_path_extremes AS (");
        StringAssert.Contains(sql, "diamond_frontiers.minimum_path + least(");
        StringAssert.Contains(sql, "diamond_frontiers.maximum_path + greatest(");
        StringAssert.Contains(sql, "THEN ln(1.001::numeric)");
        StringAssert.Contains(sql, "ELSE ln(0.999::numeric)");
        Assert.IsFalse(sql.Contains("AND @Shape <> 'deep'", StringComparison.Ordinal));
    }

    [TestMethod]
    public void SharedDiamondFrontier_MatchesExhaustivePathExtremes()
    {
        // This compares the SQL frontier recurrence with an independent DFS of
        // every path. Exercising complete base-four levels covers every sibling
        // pairing, including the fourth-to-first wraparound.
        for (var sourceIndex = 1; sourceIndex < 4_096; sourceIndex++)
        {
            var exhaustive = EnumerateEveryPath(sourceIndex);
            var compressed = CompressDiamondFrontiers(sourceIndex);

            Assert.AreEqual(
                exhaustive.Count,
                compressed.Count,
                $"Ancestor count differs for source {sourceIndex}.");

            foreach (var (ancestorIndex, expected) in exhaustive)
            {
                Assert.IsTrue(
                    compressed.TryGetValue(ancestorIndex, out var actual),
                    $"Compressed paths omit ancestor {ancestorIndex} for source {sourceIndex}.");
                Assert.AreEqual(
                    expected.Minimum,
                    actual.Minimum,
                    1e-15,
                    $"Minimum path differs for {sourceIndex} -> {ancestorIndex}.");
                Assert.AreEqual(
                    expected.Maximum,
                    actual.Maximum,
                    1e-15,
                    $"Maximum path differs for {sourceIndex} -> {ancestorIndex}.");
                Assert.AreEqual(
                    Strongest(expected),
                    Strongest(actual),
                    1e-15,
                    $"Strongest path differs for {sourceIndex} -> {ancestorIndex}.");
            }
        }
    }

    [TestMethod]
    public void SharedDiamondFrontier_MateriallyReducesDeepestHundredThousandNodeExpansion()
    {
        const int deepestNodeIndex = 99_999;

        var enumeratedPathRows = CountEnumeratedPathRows(deepestNodeIndex);
        var compressedPathRows = CompressDiamondFrontiers(deepestNodeIndex).Count;

        Assert.IsTrue(
            enumeratedPathRows > compressedPathRows * 40,
            $"Expected >40x reduction, but {enumeratedPathRows} path rows compressed to {compressedPathRows}.");
    }

    private static string ReadSeedSql()
        => File.ReadAllText(Path.Combine(
            AppContext.BaseDirectory,
            "data",
            "sql",
            "insights_stress_seed.sql"));

    private static Dictionary<int, (double Minimum, double Maximum)> EnumerateEveryPath(
        int sourceIndex)
    {
        var result = new Dictionary<int, (double Minimum, double Maximum)>();

        void Visit(int childIndex, double accumulatedPath)
        {
            var nextPath = accumulatedPath + EdgeLogWeight(childIndex);
            foreach (var parentIndex in ParentIndexes(childIndex))
            {
                MergeExtremes(result, parentIndex, nextPath, nextPath);
                Visit(parentIndex, nextPath);
            }
        }

        Visit(sourceIndex, 0d);
        return result;
    }

    private static Dictionary<int, (double Minimum, double Maximum)> CompressDiamondFrontiers(
        int sourceIndex)
    {
        var result = new Dictionary<int, (double Minimum, double Maximum)>();
        var frontier = ParentIndexes(sourceIndex);
        var minimumPath = EdgeLogWeight(sourceIndex);
        var maximumPath = minimumPath;

        while (frontier.Length > 0)
        {
            foreach (var ancestorIndex in frontier)
            {
                MergeExtremes(result, ancestorIndex, minimumPath, maximumPath);
            }

            if (frontier[0] == 0)
            {
                break;
            }

            Assert.AreEqual(
                2,
                frontier.Length,
                "Every non-root shared-diamond frontier must contain two siblings.");
            CollectionAssert.AreEqual(
                ParentIndexes(frontier[0]),
                ParentIndexes(frontier[1]),
                "Sibling frontier nodes must share their next parent pair.");

            var firstWeight = EdgeLogWeight(frontier[0]);
            var secondWeight = EdgeLogWeight(frontier[1]);
            minimumPath += Math.Min(firstWeight, secondWeight);
            maximumPath += Math.Max(firstWeight, secondWeight);
            frontier = ParentIndexes(frontier[0]);
        }

        return result;
    }

    private static int[] ParentIndexes(int childIndex)
    {
        if (childIndex == 0)
        {
            return [];
        }

        var primaryParentIndex = (childIndex - 1) / 4;
        if (childIndex < 5)
        {
            return [primaryParentIndex];
        }

        var firstSiblingIndex = (4 * ((primaryParentIndex - 1) / 4)) + 1;
        var alternateParentIndex = firstSiblingIndex +
            ((primaryParentIndex - firstSiblingIndex + 1) % 4);
        return [primaryParentIndex, alternateParentIndex];
    }

    private static double EdgeLogWeight(int childIndex)
        => Math.Log(childIndex % 2 == 1 ? 1.001d : 0.999d);

    private static bool IsActiveNode(int nodeIndex)
        => nodeIndex > 0 &&
           (nodeIndex % 5 == 0 || nodeIndex % 10 == 2);

    private static void MergeExtremes(
        Dictionary<int, (double Minimum, double Maximum)> result,
        int ancestorIndex,
        double minimumPath,
        double maximumPath)
    {
        if (result.TryGetValue(ancestorIndex, out var current))
        {
            result[ancestorIndex] = (
                Math.Min(current.Minimum, minimumPath),
                Math.Max(current.Maximum, maximumPath));
            return;
        }

        result[ancestorIndex] = (minimumPath, maximumPath);
    }

    private static double Strongest((double Minimum, double Maximum) extremes)
        => Math.Abs(extremes.Minimum) > Math.Abs(extremes.Maximum)
            ? extremes.Minimum
            : extremes.Maximum;

    private static int CountEnumeratedPathRows(int childIndex)
        => ParentIndexes(childIndex).Sum(parentIndex =>
            1 + CountEnumeratedPathRows(parentIndex));
}
