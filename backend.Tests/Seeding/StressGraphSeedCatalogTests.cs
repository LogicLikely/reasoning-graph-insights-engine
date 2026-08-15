using Backend.Seeding;
using System.Globalization;

namespace backend.Tests.Seeding;

[TestClass]
public class StressGraphSeedCatalogTests
{
    [TestMethod]
    public void All_DefinesTheApprovedCanonicalMatrixAndReservedGraphIds()
    {
        var specs = StressGraphSeedCatalog.All;

        CollectionAssert.AreEqual(
            new[]
            {
                StressGraphSeedIds.Balanced1K,
                StressGraphSeedIds.Wide1K,
                StressGraphSeedIds.Deep1K,
                StressGraphSeedIds.SharedDiamond1K,
                StressGraphSeedIds.Balanced10K,
                StressGraphSeedIds.Wide10K,
                StressGraphSeedIds.Deep10K,
                StressGraphSeedIds.SharedDiamond10K,
                StressGraphSeedIds.Balanced100K,
                StressGraphSeedIds.Wide100K,
                StressGraphSeedIds.Deep100K,
                StressGraphSeedIds.SharedDiamond100K
            },
            specs.Select(spec => spec.Id).ToArray());
        CollectionAssert.AreEqual(
            Enumerable.Range(3, 12).ToArray(),
            specs.Select(spec => spec.GraphId).ToArray());
        CollectionAssert.AreEqual(
            new[]
            {
                "balanced", "wide", "deep", "shared-diamond",
                "balanced", "wide", "deep", "shared-diamond",
                "balanced", "wide", "deep", "shared-diamond"
            },
            specs.Select(spec => spec.Shape).ToArray());
        CollectionAssert.AreEqual(
            new[]
            {
                1_000, 1_000, 1_000, 1_000,
                10_000, 10_000, 10_000, 10_000,
                100_000, 100_000, 100_000, 100_000
            },
            specs.Select(spec => spec.NodeCount).ToArray());
        CollectionAssert.AreEqual(
            new[]
            {
                999, 999, 999, 1_994,
                9_999, 9_999, 9_999, 19_994,
                99_999, 99_999, 99_999, 199_994
            },
            specs.Select(spec => spec.EdgeCount).ToArray());
        CollectionAssert.AreEqual(
            new[] { 5, 1, 999, 5, 7, 1, 9_999, 7, 9, 1, 99_999, 9 },
            specs.Select(spec => spec.MaximumDepth).ToArray());
    }

    [TestMethod]
    public void All_DefinesExactKindCounts()
    {
        foreach (var spec in StressGraphSeedCatalog.All.Take(4))
        {
            Assert.AreEqual(1, spec.RootCount);
            Assert.AreEqual(700, spec.ClaimCount);
            Assert.AreEqual(199, spec.EvidenceCount);
            Assert.AreEqual(100, spec.ObjectionCount);
        }

        foreach (var spec in StressGraphSeedCatalog.All.Skip(4).Take(4))
        {
            Assert.AreEqual(1, spec.RootCount);
            Assert.AreEqual(7_000, spec.ClaimCount);
            Assert.AreEqual(1_999, spec.EvidenceCount);
            Assert.AreEqual(1_000, spec.ObjectionCount);
        }

        foreach (var spec in StressGraphSeedCatalog.All.Skip(8))
        {
            Assert.AreEqual(1, spec.RootCount);
            Assert.AreEqual(70_000, spec.ClaimCount);
            Assert.AreEqual(19_999, spec.EvidenceCount);
            Assert.AreEqual(10_000, spec.ObjectionCount);
        }
    }

    [TestMethod]
    public void Resolve_IsDeduplicatedAndIndependentOfRequestOrder()
    {
        var resolved = StressGraphSeedCatalog.Resolve(
        [
            StressGraphSeedIds.Deep10K,
            StressGraphSeedIds.Wide1K,
            StressGraphSeedIds.Deep10K,
            StressGraphSeedIds.Balanced1K
        ]);

        CollectionAssert.AreEqual(
            new[]
            {
                StressGraphSeedIds.Balanced1K,
                StressGraphSeedIds.Wide1K,
                StressGraphSeedIds.Deep10K
            },
            resolved.Select(spec => spec.Id).ToArray());
    }

    [TestMethod]
    public void Resolve_ReportsEveryUnknownId()
    {
        var exception = Assert.ThrowsException<InvalidStressGraphSeedSelectionException>(() =>
            StressGraphSeedCatalog.Resolve(
            [
                StressGraphSeedIds.Balanced1K,
                "zzz",
                "aaa"
            ]));

        CollectionAssert.AreEqual(new[] { "aaa", "zzz" }, exception.UnknownIds.ToArray());
    }

    [TestMethod]
    public void Description_IsCultureInvariant()
    {
        var originalCulture = CultureInfo.CurrentCulture;

        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
            var germanDescription = StressGraphSeedCatalog.All[4].Description;
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
            var americanDescription = StressGraphSeedCatalog.All[4].Description;

            Assert.AreEqual(americanDescription, germanDescription);
            StringAssert.Contains(americanDescription, "10000 nodes");
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
        }
    }
}
