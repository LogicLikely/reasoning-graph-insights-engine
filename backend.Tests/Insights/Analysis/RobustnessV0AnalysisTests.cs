using Backend.Calculation;
using Backend.Insights.Analysis;
using Backend.Insights.Contracts;
using Backend.Models.Domain;

namespace backend.Tests.Insights.Analysis;

[TestClass]
public sealed class RobustnessV0AnalysisTests
{
    private readonly RobustnessV0Analyzer _analyzer = new();

    [TestMethod]
    public void Analyze_PreservesEveryFrozenScalarVectorAndDigest()
    {
        var general = GeneralFrozenGraph();
        var counterOnly = CounterOnlyFrozenGraph();
        var mixed = MixedFrozenGraph();

        var generalResult = _analyzer.Analyze(general);
        var counterResult = _analyzer.Analyze(counterOnly);
        var mixedResult = _analyzer.Analyze(mixed);

        AssertScalarEquivalent(general, generalResult);
        AssertScalarEquivalent(counterOnly, counterResult);
        AssertScalarEquivalent(mixed, mixedResult);

        Assert.AreEqual(
            "sha256:a6d2f8b34f6887a7c5332281e7db9c82912d6ae7145d0a9ff676b9bbc7daec21",
            CanonicalJson.ComputeSha256(new
            {
                semanticIdentity = AlgorithmSemanticIdentities.RobustnessV0,
                ranking = ScalarRanking(generalResult)
            }));
        Assert.AreEqual(
            "sha256:19887c1fd39090db890aec618fa599954bb87b24e0c7c2e45a09d7e587658929",
            CanonicalJson.ComputeSha256(new
            {
                semanticIdentity = AlgorithmSemanticIdentities.RobustnessV0,
                vector = "counter-only",
                ranking = ScalarRanking(counterResult)
            }));
        Assert.AreEqual(
            "sha256:0971e853ace2d58a653c99116f17f218fad51c224e43f616a074927000941361",
            CanonicalJson.ComputeSha256(new
            {
                semanticIdentity = AlgorithmSemanticIdentities.RobustnessV0,
                vector = "mixed-custom-edge",
                ranking = ScalarRanking(mixedResult)
            }));
    }

    [TestMethod]
    public void Analyze_ReturnsRichFieldsPathsDistributionAndOnePassLeastReference()
    {
        var result = _analyzer.Analyze(GeneralFrozenGraph());

        Assert.AreEqual(OperationKeys.NodeRobustness, result.OperationKey);
        Assert.AreEqual(AlgorithmSemanticIdentities.RobustnessV0, result.AlgorithmSemanticIdentity);
        Assert.AreEqual(4L, result.TotalResultCardinality);
        Assert.AreEqual(4, result.Ranking.Count);
        Assert.AreEqual(4, result.Top100.Count);
        Assert.AreEqual(4, result.RetainedItems.Count);
        Assert.AreEqual(4, result.OrderedPaths.Count);
        Assert.AreSame(result.Ranking[0], result.LeastRobust);
        for (var index = 0; index < result.Top100.Count; index++)
        {
            Assert.AreSame(result.Ranking[index], result.Top100[index]);
        }

        CollectionAssert.AreEqual(
            new[] { "target", "branch", "counter-leaf", "support-leaf" },
            result.Ranking.Select(item => item.NodeId).ToArray());
        CollectionAssert.AreEqual(
            new[] { 1, 2, 3, 4 },
            result.Ranking.Select(item => item.Rank).ToArray());

        var target = result.Ranking[0];
        var targetPathLogLr =
            (decimal)Math.Log(3d) + (decimal)Math.Log(2d);
        var expectedVector = RobustnessV0Contract.Evaluate(0.4m, targetPathLogLr);

        Assert.AreEqual("target title", target.Title);
        Assert.AreEqual("claim", target.Kind);
        Assert.AreEqual(AlgorithmSemanticIdentities.RobustnessV0, target.SemanticVersion);
        Assert.AreEqual(expectedVector.RobustnessScore, target.RobustnessScore);
        Assert.AreEqual(expectedVector.OriginalProbability, target.OriginalProbability);
        Assert.AreEqual(expectedVector.HypotheticalProbability, target.HypotheticalProbability);
        Assert.AreEqual(expectedVector.AbsoluteProbabilityDelta, target.AbsoluteProbabilityDelta);
        Assert.AreEqual(targetPathLogLr, target.AccumulatedPathLogLikelihoodRatio);
        Assert.AreEqual(expectedVector.AccumulatedPathLikelihoodRatio, target.AccumulatedPathLikelihoodRatio);
        CollectionAssert.AreEqual(
            new[] { "support-leaf", "branch", "target" },
            target.NodeIds.ToArray());
        CollectionAssert.AreEqual(
            new[] { "edge-support-branch", "edge-branch-target" },
            target.EdgeIds.ToArray());
        CollectionAssert.AreEqual(
            target.NodeIds.ToArray(),
            result.OrderedPaths[0].NodeIds.ToArray());
        CollectionAssert.AreEqual(
            target.EdgeIds.ToArray(),
            result.OrderedPaths[0].EdgeIds.ToArray());
        Assert.AreEqual(target.AccumulatedPathLogLikelihoodRatio, result.OrderedPaths[0].AccumulatedScore);

        var leaf = result.Ranking.Single(item => item.NodeId == "support-leaf");
        Assert.AreEqual(1m, leaf.RobustnessScore);
        Assert.AreEqual(0m, leaf.AccumulatedPathLogLikelihoodRatio);
        Assert.AreEqual(1m, leaf.AccumulatedPathLikelihoodRatio);
        CollectionAssert.AreEqual(new[] { "support-leaf" }, leaf.NodeIds.ToArray());
        Assert.AreEqual(0, leaf.EdgeIds.Count);

        Assert.AreEqual(4L, result.Distribution.Count);
        Assert.AreEqual(
            CanonicalResultNumber.Normalize(result.Ranking[0].RobustnessScore),
            result.Distribution.MinimumScore);
        Assert.AreEqual(1m, result.Distribution.MaximumScore);
        Assert.IsNotNull(result.Distribution.MedianScore);
        Assert.IsNotNull(result.Distribution.MeanScore);

        Assert.AreEqual(
            CanonicalJson.ComputeSha256(result.RetainedItems),
            result.ResultDigest,
            "A fully retained result must be independently digestible from its normalized items.");
        Assert.AreEqual(
            CanonicalResultNumber.Normalize(target.RobustnessScore),
            result.RetainedItems[0].GetProperty("robustnessScore").GetDecimal());
        Assert.AreEqual(
            CanonicalResultNumber.Normalize(target.OriginalProbability),
            result.RetainedItems[0].GetProperty("originalProbability").GetDecimal());
        Assert.AreEqual(
            "sha256:3d964b07066b5ad518dddd76fe0576fccf0c90973818b382296da100a7828ad9",
            result.ResultDigest);
    }

    [TestMethod]
    public void Analyze_SharedDagPathTiesAndInputPermutationsAreDeterministic()
    {
        var first = _analyzer.Analyze(SharedDagTieGraph(reverseInput: false));
        var permuted = _analyzer.Analyze(SharedDagTieGraph(reverseInput: true));

        CollectionAssert.AreEqual(
            first.Ranking.Select(item => item.NodeId).ToArray(),
            permuted.Ranking.Select(item => item.NodeId).ToArray());
        Assert.AreEqual(first.ResultDigest, permuted.ResultDigest);
        Assert.AreEqual(
            CanonicalJson.Canonicalize(first.RetainedItems),
            CanonicalJson.Canonicalize(permuted.RetainedItems));

        var target = first.Ranking.Single(item => item.NodeId == "target");
        CollectionAssert.AreEqual(
            new[] { "shared-leaf", "a-branch", "target" },
            target.NodeIds.ToArray(),
            "The ordinal node sequence must select a-branch over b-branch.");
        CollectionAssert.AreEqual(
            new[] { "edge-a", "edge-a-target" },
            target.EdgeIds.ToArray(),
            "The ordinal edge sequence must select edge-a between equal node paths.");

        var tiedBranches = first.Ranking
            .Where(item => item.NodeId is "a-branch" or "b-branch")
            .ToArray();
        Assert.AreEqual(tiedBranches[0].RobustnessScore, tiedBranches[1].RobustnessScore);
        CollectionAssert.AreEqual(
            new[] { "a-branch", "b-branch" },
            tiedBranches.Select(item => item.NodeId).ToArray());
    }

    [TestMethod]
    public void Analyze_Top100RetainsRankingPrefixWhileDigestCoversEveryLogicalItem()
    {
        var full = _analyzer.Analyze(IsolatedNodesGraph(105, reverseInput: false));
        var permuted = _analyzer.Analyze(IsolatedNodesGraph(105, reverseInput: true));
        var firstHundred = _analyzer.Analyze(IsolatedNodesGraph(100, reverseInput: false));

        Assert.AreEqual(105L, full.TotalResultCardinality);
        Assert.AreEqual(105, full.Ranking.Count);
        Assert.AreEqual(100, full.Top100.Count);
        Assert.AreEqual(100, full.RetainedItems.Count);
        Assert.AreEqual(105, full.OrderedPaths.Count);
        var leastRobust = full.LeastRobust;
        Assert.IsNotNull(leastRobust);
        Assert.AreSame(full.Ranking[0], leastRobust);
        Assert.AreEqual("node-000", leastRobust.NodeId);
        Assert.AreEqual("node-099", full.Top100[^1].NodeId);

        CollectionAssert.AreEqual(
            firstHundred.Top100.Select(item => item.NodeId).ToArray(),
            full.Top100.Select(item => item.NodeId).ToArray());
        Assert.AreEqual(
            firstHundred.ResultDigest,
            CanonicalJson.ComputeSha256(full.RetainedItems),
            "The retained prefix is identical to the complete 100-item result.");
        Assert.AreNotEqual(
            full.ResultDigest,
            CanonicalJson.ComputeSha256(full.RetainedItems),
            "The 105-item digest must not be computed from only the retained prefix.");
        Assert.AreEqual(full.ResultDigest, permuted.ResultDigest);
        StringAssert.StartsWith(full.ResultDigest, "sha256:");
        Assert.AreEqual("sha256:".Length + 64, full.ResultDigest.Length);

        Assert.AreEqual(105L, full.Distribution.Count);
        Assert.AreEqual(1m, full.Distribution.MinimumScore);
        Assert.AreEqual(1m, full.Distribution.MedianScore);
        Assert.AreEqual(1m, full.Distribution.MaximumScore);
        Assert.AreEqual(1m, full.Distribution.MeanScore);
        Assert.IsTrue(full.OrderedPaths.All(path =>
            path.NodeIds.Count == 1 && path.EdgeIds.Count == 0 && path.AccumulatedScore == 0m));
    }

    [TestMethod]
    public void Analyze_EmptyGraphHasDeterministicEmptyResult()
    {
        var result = _analyzer.Analyze(GraphWith([], []));

        Assert.AreEqual(0L, result.TotalResultCardinality);
        Assert.AreEqual(0, result.Ranking.Count);
        Assert.AreEqual(0, result.Top100.Count);
        Assert.AreEqual(0, result.RetainedItems.Count);
        Assert.AreEqual(0, result.OrderedPaths.Count);
        Assert.IsNull(result.LeastRobust);
        Assert.AreEqual(0L, result.Distribution.Count);
        Assert.IsNull(result.Distribution.MinimumScore);
        Assert.IsNull(result.Distribution.MedianScore);
        Assert.IsNull(result.Distribution.MaximumScore);
        Assert.IsNull(result.Distribution.MeanScore);
        Assert.AreEqual(CanonicalJson.ComputeSha256(Array.Empty<object>()), result.ResultDigest);
    }

    [TestMethod]
    public void Analyze_ObservesCallerCancellation()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.ThrowsException<OperationCanceledException>(() =>
            _analyzer.Analyze(GeneralFrozenGraph(), cancellation.Token));
    }

    private static void AssertScalarEquivalent(
        Graph graph,
        RobustnessV0AnalysisResult richResult)
    {
        var scalar = new GraphLikelihoodCalculator().GetAllNodeRobustness(
            graph,
            CancellationToken.None);

        Assert.AreEqual(scalar.Count, richResult.Ranking.Count);
        foreach (var item in richResult.Ranking)
        {
            Assert.AreEqual(scalar[item.NodeId], item.RobustnessScore);
        }
    }

    private static object[] ScalarRanking(RobustnessV0AnalysisResult result)
    {
        return result.Ranking
            .Select(item => (object)new
            {
                nodeId = item.NodeId,
                score = CanonicalResultNumber.Normalize(item.RobustnessScore)
            })
            .ToArray();
    }

    private static Graph GeneralFrozenGraph()
    {
        return GraphWith(
            [
                Node("target", posteriorLogOdds: 0.4m),
                Node("branch", posteriorLogOdds: -0.2m),
                Node("support-leaf", kind: "evidence", posteriorLogOdds: 4m),
                Node("counter-leaf", kind: "objection", posteriorLogOdds: -4m)
            ],
            [
                Edge("edge-support-branch", "support-leaf", "branch", "support", 2m),
                Edge("edge-branch-target", "branch", "target", "support", 3m),
                Edge("edge-counter-target", "counter-leaf", "target", "rebut", 0.1m)
            ]);
    }

    private static Graph CounterOnlyFrozenGraph()
    {
        return GraphWith(
            [
                Node("target", priorLogOdds: 12m, posteriorLogOdds: -0.2m),
                Node("counter-leaf", kind: "objection", posteriorLogOdds: -4m)
            ],
            [Edge("edge-counter-target", "counter-leaf", "target", "rebut", 0.25m)]);
    }

    private static Graph MixedFrozenGraph()
    {
        return GraphWith(
            [
                Node("target", priorLogOdds: -10m, posteriorLogOdds: 0.1m),
                Node("custom-parent", posteriorLogOdds: 0.3m),
                Node("branch", posteriorLogOdds: -0.4m),
                Node("mixed-leaf", kind: "evidence", posteriorLogOdds: 2m),
                Node("direct-counter", kind: "objection", posteriorLogOdds: -3m)
            ],
            [
                Edge("edge-mixed-support", "mixed-leaf", "branch", "support", 4m),
                Edge("edge-mixed-rebut", "branch", "custom-parent", "rebut", 0.5m),
                Edge("edge-mixed-custom", "custom-parent", "target", "custom", 1.5m),
                Edge("edge-direct-counter", "direct-counter", "target", "rebut", 0.01m)
            ]);
    }

    private static Graph SharedDagTieGraph(bool reverseInput)
    {
        var nodes = new List<GraphNode>
        {
            Node("target", posteriorLogOdds: 0.4m),
            Node("b-branch", posteriorLogOdds: 0.2m),
            Node("shared-leaf", kind: "evidence", posteriorLogOdds: 2m),
            Node("a-branch", posteriorLogOdds: 0.2m)
        };
        var edges = new List<GraphEdge>
        {
            Edge("edge-z", "shared-leaf", "a-branch", "support", 2m),
            Edge("edge-a", "shared-leaf", "a-branch", "custom", 2m),
            Edge("edge-b", "shared-leaf", "b-branch", "rebut", 2m),
            Edge("edge-b-target", "b-branch", "target", "support", 2m),
            Edge("edge-a-target", "a-branch", "target", "support", 2m)
        };

        if (reverseInput)
        {
            nodes.Reverse();
            edges.Reverse();
        }

        return GraphWith(nodes, edges);
    }

    private static Graph IsolatedNodesGraph(int count, bool reverseInput)
    {
        var nodes = Enumerable.Range(0, count)
            .Select(index => Node($"node-{index:D3}", kind: index % 2 == 0 ? "claim" : "evidence"))
            .ToList();
        if (reverseInput)
        {
            nodes.Reverse();
        }

        return GraphWith(nodes, []);
    }

    private static Graph GraphWith(List<GraphNode> nodes, List<GraphEdge> edges)
    {
        return new Graph
        {
            Slug = "robustness-v0-analysis",
            Title = "Robustness v0 analysis fixture",
            Nodes = nodes,
            Edges = edges
        };
    }

    private static GraphNode Node(
        string id,
        string kind = "claim",
        decimal priorLogOdds = 0m,
        decimal? posteriorLogOdds = null)
    {
        return new GraphNode
        {
            Id = id,
            Kind = kind,
            Title = $"{id} title",
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
