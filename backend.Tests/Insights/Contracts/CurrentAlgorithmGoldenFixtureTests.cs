using Backend.Calculation;
using Backend.Insights.Contracts;
using Backend.Models.Domain;
using Backend.Repositories;
using Backend.Services;
using Moq;

namespace backend.Tests.Insights.Contracts;

/// <summary>
/// Phase 0 snapshots of the algorithms that existed before the Insights Lab.
/// A deliberate semantic correction must use a new semantic identity and a new
/// fixture rather than updating these digests in place.
/// </summary>
[TestClass]
public sealed class CurrentAlgorithmGoldenFixtureTests
{
    [TestMethod]
    public void StrongestPathScalarV0_HasFrozenCanonicalDigest()
    {
        var context = GraphCalculationContext.From(
            [
                Node("root"),
                Node("branch"),
                Node("support-leaf", kind: "evidence"),
                Node("counter-leaf", kind: "objection")
            ],
            [
                Edge("edge-support-branch", "support-leaf", "branch", "support", 2m),
                Edge("edge-branch-root", "branch", "root", "support", 3m),
                Edge("edge-counter-root", "counter-leaf", "root", "rebut", 0.25m)
            ]);
        var calculator = new GraphLikelihoodCalculator();

        var scores = calculator.GetStrongestPaths(context, "root", PathDirection.Down);
        var snapshot = new
        {
            semanticIdentity = AlgorithmSemanticIdentities.StrongestPathScalarV0,
            direction = "down",
            startNodeId = "root",
            scores = scores
                .OrderBy(entry => entry.Key, StringComparer.Ordinal)
                .Select(entry => new { nodeId = entry.Key, logLr = Stable(entry.Value) })
                .ToArray()
        };

        Assert.AreEqual(
            "sha256:033268fbcfff0617bb43325e42a7f52d6f5255be354f880185f933849511a1f5",
            CanonicalJson.ComputeSha256(snapshot));
    }

    [TestMethod]
    public void SinglePairPathV0_MinimumAndMaximumHaveFrozenCanonicalDigest()
    {
        var context = GraphCalculationContext.From(
            [Node("start"), Node("counter-branch"), Node("support-branch"), Node("target")],
            [
                Edge("edge-start-counter", "start", "counter-branch", "rebut", 0.5m),
                Edge("edge-counter-target", "counter-branch", "target", "rebut", 0.5m),
                Edge("edge-start-support", "start", "support-branch", "support", 2m),
                Edge("edge-support-target", "support-branch", "target", "support", 3m)
            ]);
        var calculator = new GraphLikelihoodCalculator();

        var minimumLogLr = calculator.GetMinLogPath(context, "start", "target");
        var maximumLogLr = calculator.GetMaxLogPath(context, "start", "target");
        var snapshot = new
        {
            semanticIdentity = AlgorithmSemanticIdentities.SinglePairPathV0,
            startNodeId = "start",
            targetNodeId = "target",
            minimumLogLr = Stable(minimumLogLr!.Value),
            maximumLogLr = Stable(maximumLogLr!.Value)
        };

        Assert.AreEqual(
            "sha256:518e4145aa48c9b085b3e5572349b87d046c37f7440b1e73a743369ce3ea5c54",
            CanonicalJson.ComputeSha256(snapshot));
    }

    [TestMethod]
    public void StrongestPathScalarV0_EqualMagnitudeTieChoosesGreaterSignedLogScore()
    {
        var context = GraphCalculationContext.From(
            [Node("root"), Node("support-branch"), Node("counter-branch"), Node("shared-leaf", kind: "evidence")],
            [
                Edge("edge-support-root", "support-branch", "root", "support", 1m),
                Edge("edge-leaf-support", "shared-leaf", "support-branch", "support", 2m),
                Edge("edge-counter-root", "counter-branch", "root", "rebut", 1m),
                Edge("edge-leaf-counter", "shared-leaf", "counter-branch", "rebut", 0.5m)
            ]);

        var result = new GraphLikelihoodCalculator().GetStrongestPaths(
            context,
            "root",
            PathDirection.Down);

        Assert.AreEqual(
            Stable((decimal)Math.Log(2d)),
            Stable(result["shared-leaf"]));
    }

    [TestMethod]
    public void EvidenceImpactV0_HasFrozenCanonicalDigest()
    {
        var graph = GraphWith(
            [
                Node("target", priorLogOdds: 0.2m),
                Node("support-a", kind: "evidence"),
                Node("support-b", kind: "evidence"),
                Node("counter-a", kind: "objection")
            ],
            [
                Edge("edge-support-a", "support-a", "target", "support", 2m),
                Edge("edge-support-b", "support-b", "target", "support", 3m),
                Edge("edge-counter-a", "counter-a", "target", "rebut", 0.2m)
            ]);
        var calculator = new GraphLikelihoodCalculator();

        var result = calculator.GetEvidenceImpactRanking(graph, "target", CancellationToken.None);
        var snapshot = new
        {
            semanticIdentity = AlgorithmSemanticIdentities.EvidenceImpactV0,
            targetNodeId = "target",
            supportingEvidence = result.SupportingEvidence.Select(StableImpact).ToArray(),
            counterEvidence = result.CounterEvidence.Select(StableImpact).ToArray()
        };

        Assert.AreEqual(
            "sha256:d20f416235ee70b5c5616269bf920714ca31501723371034a7ccc8d66d834041",
            CanonicalJson.ComputeSha256(snapshot));
    }

    [TestMethod]
    public void EvidenceImpactV0_EqualImpactTieUsesOrdinalNodeId()
    {
        var graph = GraphWith(
            [
                Node("target"),
                Node("z-evidence", kind: "evidence"),
                Node("a-evidence", kind: "evidence")
            ],
            [
                Edge("edge-z", "z-evidence", "target", "support", 2m),
                Edge("edge-a", "a-evidence", "target", "support", 2m)
            ]);

        var result = new GraphLikelihoodCalculator().GetEvidenceImpactRanking(
            graph,
            "target",
            CancellationToken.None);

        CollectionAssert.AreEqual(
            new[] { "a-evidence", "z-evidence" },
            result.SupportingEvidence.Select(item => item.NodeId).ToArray());
    }

    [TestMethod]
    public void RobustnessV0_HasFrozenCanonicalDigest()
    {
        var graph = GraphWith(
            [
                Node("target", posteriorLogOdds: 0.4m),
                Node("branch", posteriorLogOdds: -0.2m),
                Node("support-leaf", posteriorLogOdds: 4m, kind: "evidence"),
                Node("counter-leaf", posteriorLogOdds: -4m, kind: "objection")
            ],
            [
                Edge("edge-support-branch", "support-leaf", "branch", "support", 2m),
                Edge("edge-branch-target", "branch", "target", "support", 3m),
                Edge("edge-counter-target", "counter-leaf", "target", "rebut", 0.1m)
            ]);
        var calculator = new GraphLikelihoodCalculator();

        var result = calculator.GetAllNodeRobustness(graph, CancellationToken.None);
        var snapshot = new
        {
            semanticIdentity = AlgorithmSemanticIdentities.RobustnessV0,
            ranking = result
                .OrderBy(entry => entry.Value)
                .ThenBy(entry => entry.Key, StringComparer.Ordinal)
                .Select(entry => new { nodeId = entry.Key, score = Stable(entry.Value) })
                .ToArray()
        };

        Assert.AreEqual(
            "sha256:a6d2f8b34f6887a7c5332281e7db9c82912d6ae7145d0a9ff676b9bbc7daec21",
            CanonicalJson.ComputeSha256(snapshot));
    }

    [TestMethod]
    public void RobustnessV0_CounterOnlyPathHasFrozenCanonicalDigest()
    {
        var graph = GraphWith(
            [
                Node("target", priorLogOdds: 12m, posteriorLogOdds: -0.2m),
                Node("counter-leaf", posteriorLogOdds: -4m, kind: "objection")
            ],
            [Edge("edge-counter-target", "counter-leaf", "target", "rebut", 0.25m)]);

        var result = new GraphLikelihoodCalculator().GetAllNodeRobustness(
            graph,
            CancellationToken.None);
        var snapshot = new
        {
            semanticIdentity = AlgorithmSemanticIdentities.RobustnessV0,
            vector = "counter-only",
            ranking = StableRobustnessRanking(result)
        };

        Assert.AreEqual(
            "sha256:19887c1fd39090db890aec618fa599954bb87b24e0c7c2e45a09d7e587658929",
            CanonicalJson.ComputeSha256(snapshot));
    }

    [TestMethod]
    public void RobustnessV0_MixedCustomEdgePathHasFrozenCanonicalDigest()
    {
        var graph = GraphWith(
            [
                Node("target", priorLogOdds: -10m, posteriorLogOdds: 0.1m),
                Node("custom-parent", posteriorLogOdds: 0.3m),
                Node("branch", posteriorLogOdds: -0.4m),
                Node("mixed-leaf", posteriorLogOdds: 2m, kind: "evidence"),
                Node("direct-counter", posteriorLogOdds: -3m, kind: "objection")
            ],
            [
                Edge("edge-mixed-support", "mixed-leaf", "branch", "support", 4m),
                Edge("edge-mixed-rebut", "branch", "custom-parent", "rebut", 0.5m),
                Edge("edge-mixed-custom", "custom-parent", "target", "custom", 1.5m),
                Edge("edge-direct-counter", "direct-counter", "target", "rebut", 0.01m)
            ]);

        var result = new GraphLikelihoodCalculator().GetAllNodeRobustness(
            graph,
            CancellationToken.None);
        var snapshot = new
        {
            semanticIdentity = AlgorithmSemanticIdentities.RobustnessV0,
            vector = "mixed-custom-edge",
            ranking = StableRobustnessRanking(result)
        };

        Assert.AreEqual(
            "sha256:0971e853ace2d58a653c99116f17f218fad51c224e43f616a074927000941361",
            CanonicalJson.ComputeSha256(snapshot));
    }

    [TestMethod]
    public void LikelihoodRecalculationV0_HasFrozenCanonicalDigest()
    {
        var context = GraphCalculationContext.From(
            [
                Node("a-target", priorLogOdds: 0.1m),
                Node("z-support", kind: "evidence"),
                Node("m-counter", kind: "objection")
            ],
            [
                Edge("edge-support", "z-support", "a-target", "support", 2m),
                Edge("edge-counter", "m-counter", "a-target", "rebut", 0.25m)
            ]);
        var calculator = new GraphLikelihoodCalculator();

        var result = calculator.RecalculateNodesAndAncestors(context, ["z-support", "m-counter"]);
        CollectionAssert.AreEqual(
            new[] { "m-counter", "z-support", "a-target" },
            result.Keys.ToArray());
        var snapshot = new
        {
            semanticIdentity = AlgorithmSemanticIdentities.LikelihoodRecalculateV0,
            recalculatedLogOdds = result
                .Select(entry => new { nodeId = entry.Key, logOdds = Stable(entry.Value) })
                .ToArray()
        };

        Assert.AreEqual(
            "sha256:46de9192aa069a98e7dd5c88cd76b8679454aae7a2126012a653614592311481",
            CanonicalJson.ComputeSha256(snapshot));
    }

    [TestMethod]
    public async Task CriticalCounterHeuristicV0_ExcludedCounterStillAffectsBaseline_GoldenDigest()
    {
        var graph = GraphWith(
            [
                Node("target"),
                Node("counter", priorLogOdds: -2m, posteriorLogOdds: -2m, kind: "objection")
            ],
            [Edge("edge-counter", "counter", "target", "rebut", 0.1m)]);
        var repository = new Mock<IGraphRepository>();
        repository
            .Setup(candidate => candidate.GetBySlugAsync("golden", It.IsAny<CancellationToken>()))
            .ReturnsAsync(graph);
        var service = new GraphService(repository.Object, new GraphLikelihoodCalculator());

        var result = await service.GetMinimalCounterSetAsync(
            "golden",
            "target",
            CancellationToken.None);
        var snapshot = new
        {
            semanticIdentity = AlgorithmSemanticIdentities.LegacyCriticalCounterHeuristicV0,
            targetNodeId = "target",
            counterNodeIds = result
        };

        Assert.IsNotNull(result);
        Assert.AreEqual(0, result.Count,
            "The legacy ID-only exclusion leaves the counter traversable, so its baseline already passes -1.");
        Assert.AreEqual(
            "sha256:84789ec23abde4f0b7eaaa2a4982598ff5c30063aee0b4ef57e3becd3fde9b60",
            CanonicalJson.ComputeSha256(snapshot));
    }

    private static object StableImpact(Backend.Models.Dto.EvidenceImpactDto impact)
    {
        return new
        {
            nodeId = impact.NodeId,
            logLr = Stable(impact.LogLr),
            probabilityDifference = Stable(impact.ProbabilityDifference)
        };
    }

    private static object[] StableRobustnessRanking(
        IReadOnlyDictionary<string, decimal> robustnessByNodeId)
    {
        return robustnessByNodeId
            .OrderBy(entry => entry.Value)
            .ThenBy(entry => entry.Key, StringComparer.Ordinal)
            .Select(entry => (object)new { nodeId = entry.Key, score = Stable(entry.Value) })
            .ToArray();
    }

    private static decimal Stable(decimal value)
        => CanonicalResultNumber.Normalize(value);

    private static decimal Stable(double value)
        => CanonicalResultNumber.Normalize(value);

    private static Graph GraphWith(List<GraphNode> nodes, List<GraphEdge> edges)
    {
        return new Graph
        {
            Id = 1,
            Slug = "golden",
            Title = "Phase 0 golden fixture",
            Nodes = nodes,
            Edges = edges
        };
    }

    private static GraphNode Node(
        string id,
        decimal priorLogOdds = 0m,
        decimal? posteriorLogOdds = null,
        string kind = "claim")
    {
        return new GraphNode
        {
            Id = id,
            Kind = kind,
            Title = id,
            BodyText = id,
            PriorOdds = priorLogOdds,
            PosteriorOdds = posteriorLogOdds ?? priorLogOdds
        };
    }

    private static GraphEdge Edge(
        string id,
        string from,
        string to,
        string kind,
        decimal importanceToParent)
    {
        return new GraphEdge
        {
            Id = id,
            From = from,
            To = to,
            Kind = kind,
            ImportanceToParent = importanceToParent
        };
    }
}
