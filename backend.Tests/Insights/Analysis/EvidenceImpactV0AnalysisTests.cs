using Backend.Insights.Analysis;
using Backend.Insights.Contracts;
using Backend.Models.Domain;

namespace Backend.Tests.Insights.Analysis;

[TestClass]
public sealed class EvidenceImpactV0AnalysisTests
{
    private readonly EvidenceImpactV0Analysis _analysis = new();

    [TestMethod]
    public void Analyze_ReturnsRichFrozenVectorsPartitionsAndResponsiblePaths()
    {
        var graph = GraphWith(
            [
                Node("target", "claim", priorOdds: 0.2m, title: "Target"),
                Node("support-a", "evidence", title: "Support A"),
                Node("support-b", "evidence", title: "Support B"),
                Node("counter-a", "objection", title: "Counter A"),
                Node("neutral", "evidence", title: "Neutral"),
                Node("legacy-counter", "counter", title: "Legacy alias")
            ],
            [
                Edge("edge-support-a", "support-a", "target", 2m),
                Edge("edge-support-b", "support-b", "target", 3m),
                Edge("edge-counter-a", "counter-a", "target", 0.2m),
                Edge("edge-neutral", "neutral", "target", 1m),
                Edge("edge-legacy", "legacy-counter", "target", 0.1m)
            ]);

        var result = _analysis.Analyze(graph, "target");

        var baselineLogOdds = 0.2m +
                              (decimal)Math.Log(2d) +
                              (decimal)Math.Log(3d) +
                              (decimal)Math.Log(0.2d);
        var baselineProbability = Probability(baselineLogOdds);
        Assert.AreEqual(OperationKeys.EvidenceImpactRanking, result.OperationKey);
        Assert.AreEqual(AlgorithmSemanticIdentities.EvidenceImpactV0,
            result.AlgorithmSemanticIdentity);
        Assert.AreEqual(CanonicalResultNumber.Normalize(baselineLogOdds),
            result.Summary.BaselineLogOdds);
        Assert.AreEqual(CanonicalResultNumber.Normalize(baselineProbability),
            result.Summary.BaselineProbability);
        Assert.AreEqual(2, result.SupportingEvidence.Count);
        Assert.AreEqual(1, result.CounterEvidence.Count);
        Assert.AreEqual(3, result.TotalResultCardinality);
        CollectionAssert.AreEqual(
            new[] { "support-b", "support-a" },
            result.SupportingEvidence.Select(item => item.NodeId).ToArray());
        CollectionAssert.AreEqual(
            new[] { "counter-a" },
            result.CounterEvidence.Select(item => item.NodeId).ToArray());
        Assert.IsFalse(result.Items.Any(item => item.NodeId == "neutral"));
        Assert.IsFalse(result.Items.Any(item => item.NodeId == "legacy-counter"));

        var support = result.SupportingEvidence[0];
        var counterfactual = Probability(baselineLogOdds - (decimal)Math.Log(3d));
        Assert.AreEqual(1, support.Rank);
        Assert.AreEqual(EvidenceImpactV0Partitions.Supporting, support.Partition);
        Assert.AreEqual("Support B", support.Title);
        Assert.AreEqual("evidence", support.Kind);
        Assert.AreEqual(CanonicalResultNumber.Normalize(baselineProbability),
            support.BaselineProbability);
        Assert.AreEqual(CanonicalResultNumber.Normalize(counterfactual),
            support.CounterfactualProbability);
        Assert.AreEqual(
            CanonicalResultNumber.Normalize(baselineProbability - counterfactual),
            support.RawProbabilityDelta);
        CollectionAssert.AreEqual(
            new[] { "target", "support-b" },
            support.NodeIds.ToArray());
        CollectionAssert.AreEqual(
            new[] { "edge-support-b" },
            support.EdgeIds.ToArray());
        Assert.IsTrue(result.CounterEvidence[0].RawProbabilityDelta < 0m);
    }

    [TestMethod]
    public void Analyze_UsesOneStrongestSharedDagPathAndOrdinalImpactTies()
    {
        var graph = GraphWith(
            [
                Node("target"),
                Node("a-branch"),
                Node("z-branch"),
                Node("shared", "evidence", title: "Shared"),
                Node("a-direct", "evidence"),
                Node("z-direct", "evidence")
            ],
            [
                Edge("edge-a-target", "a-branch", "target", 2m),
                Edge("edge-shared-a", "shared", "a-branch", 3m),
                Edge("edge-z-target", "z-branch", "target", 2m),
                Edge("edge-shared-z", "shared", "z-branch", 3m),
                Edge("edge-a-direct", "a-direct", "target", 2m),
                Edge("edge-z-direct", "z-direct", "target", 2m)
            ]);

        var result = _analysis.Analyze(graph, "target");
        var shared = result.SupportingEvidence.Single(item => item.NodeId == "shared");

        CollectionAssert.AreEqual(
            new[] { "target", "a-branch", "shared" },
            shared.NodeIds.ToArray());
        CollectionAssert.AreEqual(
            new[] { "edge-a-target", "edge-shared-a" },
            shared.EdgeIds.ToArray());
        Assert.AreEqual(
            CanonicalResultNumber.Normalize(Math.Log(6d)),
            shared.AccumulatedPathLogLikelihoodRatio);

        var directIds = result.SupportingEvidence
            .Where(item => item.NodeId.EndsWith("-direct", StringComparison.Ordinal))
            .Select(item => item.NodeId)
            .ToArray();
        CollectionAssert.AreEqual(new[] { "a-direct", "z-direct" }, directIds);
    }

    [TestMethod]
    public void Analyze_IsIndependentOfInputPermutationAndDigestCoversNormalizedItems()
    {
        var nodes = new[]
        {
            Node("target", priorOdds: 0.1m),
            Node("z-support", "evidence"),
            Node("a-support", "evidence"),
            Node("counter", "objection")
        };
        var edges = new[]
        {
            Edge("edge-z", "z-support", "target", 2m),
            Edge("edge-a", "a-support", "target", 2m),
            Edge("edge-counter", "counter", "target", 0.25m)
        };

        var normal = _analysis.Analyze(GraphWith(nodes, edges), "target");
        var reversed = _analysis.Analyze(
            GraphWith(nodes.Reverse(), edges.Reverse()),
            "target");

        Assert.AreEqual(normal.ResultDigest, reversed.ResultDigest);
        Assert.AreEqual(
            CanonicalJson.Canonicalize(normal.Items),
            CanonicalJson.Canonicalize(reversed.Items));
        Assert.AreEqual(CanonicalJson.ComputeSha256(normal.Items), normal.ResultDigest);
        Assert.AreEqual(
            "sha256:de1fda3676e293004753c6b29ef6b29f9cc59d2004129af117b80d55b6dd6fb4",
            normal.ResultDigest);
        CollectionAssert.AreEqual(
            new[] { "a-support", "z-support" },
            normal.SupportingEvidence.Select(item => item.NodeId).ToArray());
        Assert.IsTrue(normal.Items.All(item =>
            item.AccumulatedPathLogLikelihoodRatio ==
            CanonicalResultNumber.Normalize(item.AccumulatedPathLogLikelihoodRatio)));
    }

    [TestMethod]
    public void Analyze_RetainsDeterministicTop100ButDigestsAllPartitionItems()
    {
        var nodes = new List<GraphNode> { Node("target") };
        var edges = new List<GraphEdge>();
        for (var index = 0; index < 120; index++)
        {
            var nodeId = $"evidence-{index:D3}";
            nodes.Add(Node(nodeId, "evidence"));
            edges.Add(Edge($"edge-{index:D3}", nodeId, "target", 2m));
        }

        var result = _analysis.Analyze(GraphWith(nodes, edges), "target");

        Assert.AreEqual(120, result.SupportingEvidence.Count);
        Assert.AreEqual(0, result.CounterEvidence.Count);
        Assert.AreEqual(120, result.Items.Count);
        Assert.AreEqual(120, result.TotalResultCardinality);
        Assert.AreEqual(100, result.TopItems.Count);
        Assert.AreEqual(CanonicalJson.ComputeSha256(result.Items), result.ResultDigest);
        Assert.AreNotEqual(CanonicalJson.ComputeSha256(result.TopItems), result.ResultDigest);
        Assert.AreEqual(120, result.OrderedPaths.Count);
        Assert.AreEqual("evidence-000", result.SupportingEvidence[0].NodeId);
    }

    [TestMethod]
    public void Analyze_RejectsDisconnectedCycleAndHonorsCancellation()
    {
        var cycle = GraphWith(
            [Node("target"), Node("cycle-a"), Node("cycle-b")],
            [
                Edge("edge-a-b", "cycle-a", "cycle-b", 2m),
                Edge("edge-b-a", "cycle-b", "cycle-a", 2m)
            ]);
        Assert.ThrowsException<ArgumentException>(() =>
            _analysis.Analyze(cycle, "target"));

        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.ThrowsException<OperationCanceledException>(() =>
            _analysis.Analyze(
                GraphWith([Node("target")], []),
                "target",
                cancellation.Token));
    }

    [TestMethod]
    public void Analyze_EmptyPartitionsHaveExplicitCompactDistributionAndNoConsoleOutput()
    {
        var originalOut = Console.Out;
        using var captured = new StringWriter();
        EvidenceImpactV0Result result;
        try
        {
            Console.SetOut(captured);
            result = _analysis.Analyze(
                GraphWith([Node("target", priorOdds: 0.5m)], []),
                "target");
        }
        finally
        {
            Console.SetOut(originalOut);
        }

        Assert.AreEqual(string.Empty, captured.ToString());
        Assert.AreEqual(0, result.TotalResultCardinality);
        Assert.AreEqual(0, result.TopItems.Count);
        Assert.AreEqual(0, result.Distribution.Supporting.Count);
        Assert.IsNull(result.Distribution.Supporting.MinimumRawProbabilityDelta);
        Assert.AreEqual(CanonicalJson.ComputeSha256(result.Items), result.ResultDigest);
    }

    private static double Probability(decimal logOdds)
    {
        var value = (double)logOdds;
        if (value >= 0d)
        {
            var inverseOdds = Math.Exp(-value);
            return 1d / (1d + inverseOdds);
        }

        var odds = Math.Exp(value);
        return odds / (1d + odds);
    }

    private static Graph GraphWith(
        IEnumerable<GraphNode> nodes,
        IEnumerable<GraphEdge> edges) => new()
        {
            Slug = "evidence-impact-v0-test",
            Nodes = nodes.ToList(),
            Edges = edges.ToList()
        };

    private static GraphNode Node(
        string id,
        string kind = "claim",
        decimal priorOdds = 0m,
        string? title = null) => new()
        {
            Id = id,
            Kind = kind,
            Title = title ?? id,
            PriorOdds = priorOdds,
            PosteriorOdds = 99m
        };

    private static GraphEdge Edge(
        string id,
        string from,
        string to,
        decimal likelihoodRatio) => new()
        {
            Id = id,
            From = from,
            To = to,
            Kind = "ignored-kind",
            ImportanceToParent = likelihoodRatio
        };
}
