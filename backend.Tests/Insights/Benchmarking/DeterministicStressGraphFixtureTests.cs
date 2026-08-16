using Backend.Insights.Analysis;
using Backend.Insights.Benchmarking;
using Backend.Insights.Contracts;
using Backend.Seeding;

namespace Backend.Tests.Insights.Benchmarking;

[TestClass]
public sealed class DeterministicStressGraphFixtureTests
{
    private const string FrozenCorpusFingerprint =
        "sha256:6bd0ffa41d95e6fabfeeb3736cbc21f2c83fcfa9308ce939311ecc4ea2ac1f85";

    [DataTestMethod]
    [DataRow(
        StressGraphSeedIds.Balanced1K,
        "sha256:8956b9aa4cfe2de1c884b66e2d233c6a3a0cca46085787d2648aa3b8a92a82b0",
        "sha256:6911780ae8debe4888a5b722edb6da8c6aed2488742377ea4e84502055353cb5",
        "sha256:e1b6c46fa7bcf0545daa4c3932a279610f8716a2f8b9b51bd08310a98c50b15f")]
    [DataRow(
        StressGraphSeedIds.Wide1K,
        "sha256:12f4af7d04374540c656cb35307bdefc3dfc72e471d241c54abd439809d76b12",
        "sha256:a9d0e91d95af7a4a4de1a3a19d6ea5697d01a3aa72b48cacb694b4ae09cb422e",
        "sha256:65e9be1b752f06c556d7a5ad99192ffbe51dff62fac2d04ed31ba2188bd96f8f")]
    [DataRow(
        StressGraphSeedIds.Deep1K,
        "sha256:781daf18c656f9e0f1bfaeb748a28846974b7fb8595baf2a8666080b5472bf7b",
        "sha256:1448f60ae02b4838cfb117a6ed23831a6ce89491bd1b0012d4355710929d32f9",
        "sha256:34df18b5d11230c10b1357990451209981a827d3517110141d395ce04389d800")]
    [DataRow(
        StressGraphSeedIds.SharedDiamond1K,
        "sha256:29066c2f5a5a72ed6aed13423e36066ae303158c42f8fb6cb8b0444b375d7b3e",
        "sha256:dd2b0f969de77fecdc1961e3909fe22d471304cfe4d72854083e7763eeb012f5",
        "sha256:66b1dcbd98ebc7cb6878ca87457aefc686ff6b78fc8cd895d48c95fd4d6da290")]
    public void Create_OneThousandNodeFixturesMatchFrozenIdentityCheckpoints(
        string datasetId,
        string topologyFingerprint,
        string inputFingerprint,
        string datasetInputFingerprint)
    {
        var fixture = DeterministicStressGraphFixtureFactory.Create(datasetId);

        Assert.AreEqual(FrozenCorpusFingerprint, fixture.Identity.CorpusFingerprint);
        Assert.AreEqual(topologyFingerprint, fixture.Identity.TopologyFingerprint);
        Assert.AreEqual(inputFingerprint, fixture.Identity.InputFingerprint);
        Assert.AreEqual(datasetInputFingerprint, fixture.Identity.DatasetInputFingerprint);
    }

    [TestMethod]
    public void Create_BalancedOneThousandMatchesFrozenAlgorithmCheckpoint()
    {
        var graph = DeterministicStressGraphFixtureFactory
            .Create(StressGraphSeedIds.Balanced1K)
            .CreateGraph();

        Assert.AreEqual(-0.047680516006996209m, graph.Nodes[0].PosteriorOdds);
        Assert.AreEqual(-0.415499111458502403m, graph.Nodes[5].PosteriorOdds);
        Assert.AreEqual(0.001987000660165562m, graph.Nodes[15].PosteriorOdds);

        var candidates = CriticalCounterCandidateGuard.RequireAtMost(
            graph,
            "n-00015",
            maximumCandidateCount: 8);
        Assert.AreEqual(4, candidates.ActualCandidateCount);
        CollectionAssert.AreEqual(
            new[] { "n-00062", "n-00252", "n-00982", "n-00992" },
            candidates.EligibleCandidateNodeIds.ToArray());

        var result = new CriticalCounterV1Analyzer().Analyze(
            new CriticalCounterV1AnalysisRequest(
                graph,
                "n-00015",
                OperationStrategyNames.Exact,
                CriticalCounterV1Contract.DefaultThresholdLogOdds,
                null));

        Assert.AreEqual(4, result.CandidateCount);
        Assert.AreEqual(16L, result.EvaluationCount);
        Assert.AreEqual(1L, result.TotalResultCardinality);
        Assert.AreEqual(
            "sha256:32c8fd4480822a83e1157576593fbcc1be88bd45829a38f603dfe355f9108c63",
            result.ResultDigest);
    }

    [DataTestMethod]
    [DataRow(StressGraphSeedIds.Balanced1K)]
    [DataRow(StressGraphSeedIds.Wide1K)]
    [DataRow(StressGraphSeedIds.Deep1K)]
    [DataRow(StressGraphSeedIds.SharedDiamond1K)]
    [DataRow(StressGraphSeedIds.Balanced10K)]
    [DataRow(StressGraphSeedIds.Wide10K)]
    [DataRow(StressGraphSeedIds.Deep10K)]
    [DataRow(StressGraphSeedIds.SharedDiamond10K)]
    [DataRow(StressGraphSeedIds.Balanced100K)]
    [DataRow(StressGraphSeedIds.Wide100K)]
    [DataRow(StressGraphSeedIds.Deep100K)]
    [DataRow(StressGraphSeedIds.SharedDiamond100K)]
    public void Create_MatchesCanonicalCatalogCountsAndKinds(string datasetId)
    {
        var specification = StressGraphSeedCatalog.Resolve([datasetId]).Single();
        var fixture = DeterministicStressGraphFixtureFactory.Create(specification);
        var graph = fixture.CreateGraph();

        Assert.AreSame(specification, fixture.Specification);
        Assert.AreEqual(specification.NodeCount, fixture.NodeCount);
        Assert.AreEqual(specification.EdgeCount, fixture.EdgeCount);
        Assert.AreEqual(specification.NodeCount, graph.Nodes.Count);
        Assert.AreEqual(specification.EdgeCount, graph.Edges.Count);
        Assert.AreEqual(specification.RootCount,
            graph.Nodes.Count(node => node.Kind == "root"));
        Assert.AreEqual(specification.ClaimCount,
            graph.Nodes.Count(node => node.Kind == "claim"));
        Assert.AreEqual(specification.EvidenceCount,
            graph.Nodes.Count(node => node.Kind == "evidence"));
        Assert.AreEqual(specification.ObjectionCount,
            graph.Nodes.Count(node => node.Kind == "objection"));
        Assert.AreEqual("n-00000", fixture.RootNodeId);
        Assert.AreEqual(
            DeterministicStressGraphFixtureFactory.NodeId(specification.NodeCount - 1),
            fixture.DeepestNodeId);
        AssertIdentityUsesCanonicalDigests(fixture.Identity);
    }

    [TestMethod]
    public void Create_ReproducesSqlTopologyRulesForEveryShape()
    {
        var balanced = DeterministicStressGraphFixtureFactory
            .Create(StressGraphSeedIds.Balanced1K)
            .CreateGraph();
        AssertEdge(balanced, "e-p-00005", "n-00005", "n-00001");

        var wide = DeterministicStressGraphFixtureFactory
            .Create(StressGraphSeedIds.Wide1K)
            .CreateGraph();
        AssertEdge(wide, "e-p-00999", "n-00999", "n-00000");

        var deep = DeterministicStressGraphFixtureFactory
            .Create(StressGraphSeedIds.Deep1K)
            .CreateGraph();
        AssertEdge(deep, "e-p-00999", "n-00999", "n-00998");

        var sharedDiamond = DeterministicStressGraphFixtureFactory
            .Create(StressGraphSeedIds.SharedDiamond1K)
            .CreateGraph();
        AssertEdge(sharedDiamond, "e-p-00005", "n-00005", "n-00001");
        AssertEdge(sharedDiamond, "e-a-00005", "n-00005", "n-00002");
        Assert.AreEqual(2,
            sharedDiamond.Edges.Count(edge => edge.From == "n-00005"));

        var oddEdge = balanced.Edges.Single(edge => edge.Id == "e-p-00005");
        Assert.AreEqual("support", oddEdge.Kind);
        Assert.AreEqual(1.001m, oddEdge.ImportanceToParent);
        var evenEdge = balanced.Edges.Single(edge => edge.Id == "e-p-00006");
        Assert.AreEqual("rebut", evenEdge.Kind);
        Assert.AreEqual(0.999m, evenEdge.ImportanceToParent);
    }

    [TestMethod]
    public void Create_IsDeterministicAndReturnsIndependentMutableDomainGraphs()
    {
        var firstFixture = DeterministicStressGraphFixtureFactory.Create(
            StressGraphSeedIds.Balanced1K);
        var secondFixture = DeterministicStressGraphFixtureFactory.Create(
            StressGraphSeedIds.Balanced1K);

        Assert.AreEqual(firstFixture.Identity, secondFixture.Identity);
        var firstGraph = firstFixture.CreateGraph();
        var secondGraph = firstFixture.CreateGraph();
        Assert.AreNotSame(firstGraph, secondGraph);
        Assert.AreNotSame(firstGraph.Nodes, secondGraph.Nodes);
        Assert.AreNotSame(firstGraph.Nodes[5], secondGraph.Nodes[5]);
        Assert.AreNotSame(firstGraph.Nodes[5].Tags, secondGraph.Nodes[5].Tags);
        Assert.AreNotSame(firstGraph.Edges[4], secondGraph.Edges[4]);

        firstGraph.Nodes[5].Kind = "changed";
        firstGraph.Nodes[5].Tags.Add("changed");
        firstGraph.Edges[4].ImportanceToParent = 9m;

        var freshGraph = firstFixture.CreateGraph();
        Assert.AreEqual("evidence", freshGraph.Nodes[5].Kind);
        Assert.IsFalse(freshGraph.Nodes[5].Tags.Contains("changed"));
        Assert.AreEqual(1.001m, freshGraph.Edges[4].ImportanceToParent);
    }

    [TestMethod]
    public void CandidateGuard_EnforcesExplicitLimitWithoutChangingTheGraph()
    {
        const int maximumCandidateCount = 8;
        var fixture = DeterministicStressGraphFixtureFactory.Create(
            StressGraphSeedIds.Balanced1K);
        var graph = fixture.CreateGraph();
        var inputFingerprint = CanonicalJson.ComputeSha256(graph);

        var accepted = CriticalCounterCandidateGuard.RequireAtMost(
            graph,
            "n-00015",
            maximumCandidateCount);

        Assert.IsTrue(accepted.ActualCandidateCount > 0);
        Assert.IsTrue(accepted.ActualCandidateCount <= maximumCandidateCount);
        Assert.AreEqual(maximumCandidateCount, accepted.MaximumCandidateCount);
        Assert.IsTrue(accepted.EligibleCandidateNodeIds.All(candidateId =>
            graph.Nodes.Single(node => node.Id == candidateId).Kind == "objection"));
        Assert.AreEqual(inputFingerprint, CanonicalJson.ComputeSha256(graph));

        var exception = Assert.ThrowsException<CriticalCounterCandidateLimitExceededException>(() =>
            CriticalCounterCandidateGuard.RequireAtMost(
                graph,
                fixture.RootNodeId,
                maximumCandidateCount));
        Assert.AreEqual(fixture.RootNodeId, exception.TargetNodeId);
        Assert.AreEqual(maximumCandidateCount, exception.MaximumCandidateCount);
        Assert.AreEqual(fixture.Specification.ObjectionCount, exception.ActualCandidateCount);
        Assert.AreEqual(inputFingerprint, CanonicalJson.ComputeSha256(graph));
    }

    [TestMethod]
    public void Create_RejectsAChangedCatalogSpecification()
    {
        var canonical = StressGraphSeedCatalog.Resolve(
            [StressGraphSeedIds.Balanced1K]).Single();
        var changed = canonical with { EdgeCount = canonical.EdgeCount + 1 };

        Assert.ThrowsException<ArgumentException>(() =>
            DeterministicStressGraphFixtureFactory.Create(changed));
    }

    private static void AssertEdge(
        Backend.Models.Domain.Graph graph,
        string edgeId,
        string from,
        string to)
    {
        var edge = graph.Edges.Single(candidate => candidate.Id == edgeId);
        Assert.AreEqual(from, edge.From);
        Assert.AreEqual(to, edge.To);
    }

    private static void AssertIdentityUsesCanonicalDigests(
        DeterministicStressGraphIdentity identity)
    {
        Assert.AreEqual(DeterministicStressGraphFixtureFactory.GeneratorVersion,
            identity.GeneratorVersion);
        Assert.AreEqual(DeterministicStressGraphFixtureFactory.CorpusId,
            identity.CorpusId);
        AssertDigest(identity.CorpusFingerprint);
        AssertDigest(identity.TopologyFingerprint);
        AssertDigest(identity.InputFingerprint);
        AssertDigest(identity.DatasetInputFingerprint);
    }

    private static void AssertDigest(string value)
    {
        Assert.IsTrue(value.StartsWith("sha256:", StringComparison.Ordinal));
        Assert.AreEqual(71, value.Length);
        Assert.IsTrue(value[7..].All(character =>
            character is >= '0' and <= '9' or >= 'a' and <= 'f'));
    }
}
