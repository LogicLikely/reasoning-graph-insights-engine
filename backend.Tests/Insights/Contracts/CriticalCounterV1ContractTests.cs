using Backend.Calculation;
using Backend.Insights.Contracts;
using Backend.Models.Domain;

namespace Backend.Tests.Insights.Contracts;

[TestClass]
public class CriticalCounterV1ContractTests
{
    [TestMethod]
    public void Contract_FreezesVersionStrategiesKindsAndDefaultThreshold()
    {
        Assert.AreEqual("critical-counter-v1", CriticalCounterV1Contract.SemanticVersion);
        CollectionAssert.AreEqual(
            new[] { "exact", "greedy", "auto" },
            CriticalCounterV1Contract.Strategies.ToArray());
        CollectionAssert.AreEqual(
            new[] { "objection", "counter" },
            CriticalCounterV1Contract.CandidateKinds.ToArray());
        Assert.AreEqual(-1m, CriticalCounterV1Contract.DefaultThresholdLogOdds);
    }

    [TestMethod]
    public void Eligibility_UsesKindPresenceDistinctnessTargetExclusionAndDirectedReachability()
    {
        var graph = GraphWith(
            [
                Node("target", kind: "claim"),
                Node("branch"),
                Node("O-reachable", kind: "ObJeCtIoN"),
                Node("C-reachable", kind: "COUNTER"),
                Node("O-unreachable", kind: "objection"),
                Node("evidence", kind: "evidence")
            ],
            [
                Edge("e-o-branch", "O-reachable", "branch", kind: "rebut", weight: 2m),
                Edge("e-branch-target", "branch", "target", kind: "custom"),
                Edge("e-c-target", "C-reachable", "target", weight: 4m),
                Edge("e-evidence-target", "evidence", "target")
            ]);

        var eligible = CriticalCounterV1Contract.GetEligibleCandidateNodeIds(graph, "target");

        CollectionAssert.AreEqual(
            new[] { "C-reachable", "O-reachable" },
            eligible.ToArray());
        Assert.IsTrue(CriticalCounterV1Contract.IsEligibleCandidate(graph, "target", "O-reachable"));
        Assert.IsFalse(CriticalCounterV1Contract.IsEligibleCandidate(graph, "target", "O-unreachable"));
        Assert.IsFalse(CriticalCounterV1Contract.IsEligibleCandidate(graph, "target", "evidence"));
    }

    [TestMethod]
    public void Eligibility_DoesNotRequirePositiveMarginalEffect()
    {
        var graph = GraphWith(
            [Node("target"), Node("counter", kind: "objection")],
            [Edge("e-counter-target", "counter", "target", kind: "support", weight: 5m)]);

        Assert.IsTrue(CriticalCounterV1Contract.IsEligibleCandidate(graph, "target", "counter"));
    }

    [TestMethod]
    public void Eligibility_RejectsATargetWithACounterCandidateKind()
    {
        var graph = GraphWith([Node("target", kind: "objection")], []);

        Assert.ThrowsException<ArgumentException>(() =>
            CriticalCounterV1Contract.GetEligibleCandidateNodeIds(graph, "target"));
    }

    [TestMethod]
    public void Eligibility_RequiresTheTargetToBePresent()
    {
        var graph = GraphWith([Node("other")], []);

        Assert.ThrowsException<ArgumentException>(() =>
            CriticalCounterV1Contract.GetEligibleCandidateNodeIds(graph, "missing"));
    }

    [TestMethod]
    public void Projection_RemovesEveryEligibleCandidateAndEveryIncidentEdge()
    {
        var graph = GraphWith(
            [
                Node("target"),
                Node("branch"),
                Node("counter-a", kind: "objection"),
                Node("counter-b", kind: "counter"),
                Node("unreachable-counter", kind: "objection")
            ],
            [
                Edge("e-a-branch", "counter-a", "branch"),
                Edge("e-b-a", "counter-b", "counter-a"),
                Edge("e-branch-target", "branch", "target")
            ]);

        var baseline = CriticalCounterV1Contract.BuildActiveProjection(graph, "target", []);

        CollectionAssert.AreEquivalent(
            new[] { "target", "branch", "unreachable-counter" },
            baseline.Graph.Nodes.Select(node => node.Id).ToArray());
        CollectionAssert.AreEqual(
            new[] { "e-branch-target" },
            baseline.Graph.Edges.Select(edge => edge.Id).ToArray());
        CollectionAssert.AreEqual(
            new[] { "counter-a", "counter-b" },
            baseline.EligibleCandidateNodeIds.ToArray());
    }

    [TestMethod]
    public void Projection_RestoresSelectedNodesAndOnlyEdgesWithActiveEndpoints()
    {
        var graph = GraphWith(
            [
                Node("target"),
                Node("branch"),
                Node("counter-a", kind: "objection"),
                Node("counter-b", kind: "counter")
            ],
            [
                Edge("e-a-branch", "counter-a", "branch"),
                Edge("e-b-a", "counter-b", "counter-a"),
                Edge("e-branch-target", "branch", "target")
            ]);

        var projection = CriticalCounterV1Contract.BuildActiveProjection(
            graph,
            "target",
            ["counter-a"]);

        CollectionAssert.AreEquivalent(
            new[] { "target", "branch", "counter-a" },
            projection.Graph.Nodes.Select(node => node.Id).ToArray());
        CollectionAssert.AreEquivalent(
            new[] { "e-a-branch", "e-branch-target" },
            projection.Graph.Edges.Select(edge => edge.Id).ToArray());
        Assert.IsFalse(projection.Graph.Edges.Any(edge => edge.Id == "e-b-a"));
    }

    [TestMethod]
    public void Projection_RebuildsFreshCopiesFromImmutableInput()
    {
        var graph = GraphWith(
            [Node("target"), Node("counter", kind: "objection")],
            [Edge("e-counter-target", "counter", "target")]);
        var baseline = CriticalCounterV1Contract.BuildActiveProjection(graph, "target", []);

        baseline.Graph.Nodes.Single().Title = "mutated projection";
        var applied = CriticalCounterV1Contract.BuildActiveProjection(graph, "target", ["counter"]);

        Assert.AreEqual(string.Empty, graph.Nodes.Single(node => node.Id == "target").Title);
        Assert.AreEqual(string.Empty, applied.Graph.Nodes.Single(node => node.Id == "target").Title);
        Assert.AreNotSame(graph.Nodes[0], applied.Graph.Nodes[0]);
        Assert.AreNotSame(graph.Edges[0], applied.Graph.Edges[0]);
    }

    [TestMethod]
    public void Projection_CurrentLikelihoodRecalculationUsesBaselineOrAppliedEdgesExactlyOnce()
    {
        var target = Node("target");
        target.PriorOdds = 0.25m;
        target.PosteriorOdds = 20m;
        var counter = Node("counter", kind: "objection");
        counter.PriorOdds = -3m;
        counter.PosteriorOdds = -10m;
        var graph = GraphWith(
            [target, counter],
            [Edge("e-counter-target", "counter", "target", kind: "rebut", weight: 0.1m)]);
        var calculator = new GraphLikelihoodCalculator();

        var baseline = CriticalCounterV1Contract.BuildActiveProjection(graph, "target", []);
        var baselineContext = GraphCalculationContext.From(
            baseline.Graph.Nodes,
            baseline.Graph.Edges);
        var baselineResult = calculator.RecalculateNodesAndAncestors(
            baselineContext,
            ["target"]);

        var applied = CriticalCounterV1Contract.BuildActiveProjection(graph, "target", ["counter"]);
        var appliedContext = GraphCalculationContext.From(
            applied.Graph.Nodes,
            applied.Graph.Edges);
        var appliedResult = calculator.RecalculateNodesAndAncestors(
            appliedContext,
            applied.AppliedCandidateNodeIds);

        var expectedAppliedLogOdds = 0.25m + (decimal)Math.Log(0.1d);
        Assert.AreEqual(0.25m, baselineResult["target"]);
        Assert.AreEqual(expectedAppliedLogOdds, appliedResult["target"]);
        Assert.AreNotEqual(0.25m + (2m * (decimal)Math.Log(0.1d)), appliedResult["target"]);
        Assert.AreEqual(20m, graph.Nodes.Single(node => node.Id == "target").PosteriorOdds);
    }

    [TestMethod]
    public void Validation_AllowsSharedDagAndCountsSharedCandidateOnce()
    {
        var graph = GraphWith(
            [
                Node("target"),
                Node("left"),
                Node("right"),
                Node("shared-counter", kind: "objection")
            ],
            [
                Edge("e-counter-left", "shared-counter", "left"),
                Edge("e-counter-right", "shared-counter", "right"),
                Edge("e-left-target", "left", "target"),
                Edge("e-right-target", "right", "target")
            ]);

        var validation = CriticalCounterV1Contract.ValidateGraph(graph);
        var eligible = CriticalCounterV1Contract.GetEligibleCandidateNodeIds(graph, "target");

        Assert.IsTrue(validation.IsValid);
        CollectionAssert.AreEqual(new[] { "shared-counter" }, eligible.ToArray());
    }

    [TestMethod]
    public void Validation_RejectsDirectedCyclesBeforeEligibilityOrProjection()
    {
        var graph = GraphWith(
            [Node("target"), Node("counter", kind: "objection")],
            [
                Edge("e-counter-target", "counter", "target"),
                Edge("e-target-counter", "target", "counter")
            ]);

        var validation = CriticalCounterV1Contract.ValidateGraph(graph);

        Assert.IsFalse(validation.IsValid);
        Assert.IsTrue(validation.Issues.Any(issue => issue.Code == "directed-cycle"));
        Assert.ThrowsException<ArgumentException>(() =>
            CriticalCounterV1Contract.GetEligibleCandidateNodeIds(graph, "target"));
    }

    [TestMethod]
    public void Validation_RejectsADirectedCycleDisconnectedFromTheTarget()
    {
        var graph = GraphWith(
            [
                Node("target"),
                Node("counter", kind: "objection"),
                Node("cycle-a"),
                Node("cycle-b")
            ],
            [
                Edge("e-counter-target", "counter", "target"),
                Edge("e-cycle-a-b", "cycle-a", "cycle-b"),
                Edge("e-cycle-b-a", "cycle-b", "cycle-a")
            ]);

        var validation = CriticalCounterV1Contract.ValidateGraph(graph);

        Assert.IsFalse(validation.IsValid);
        Assert.IsTrue(validation.Issues.Any(issue => issue.Code == "directed-cycle"));
        Assert.ThrowsException<ArgumentException>(() =>
            CriticalCounterV1Contract.GetEligibleCandidateNodeIds(graph, "target"));
    }

    [TestMethod]
    public void Validation_RejectsDuplicateCandidateIds()
    {
        var graph = GraphWith(
            [Node("target"), Node("counter", kind: "objection"), Node("counter", kind: "objection")],
            []);

        var validation = CriticalCounterV1Contract.ValidateGraph(graph);

        Assert.IsFalse(validation.IsValid);
        Assert.IsTrue(validation.Issues.Any(issue => issue.Code == "duplicate-node-id"));
    }

    [TestMethod]
    public void Threshold_IsInclusiveAtEquality()
    {
        Assert.IsTrue(CriticalCounterV1Contract.IsThresholdAttained(-1m, -1m));
        Assert.IsTrue(CriticalCounterV1Contract.IsThresholdAttained(-1.0001m, -1m));
        Assert.IsFalse(CriticalCounterV1Contract.IsThresholdAttained(-0.9999m, -1m));
    }

    [TestMethod]
    public void ResultOrdering_IsLexicographicAcrossAttainmentCardinalityMarginAndIds()
    {
        var unattained = Outcome(["a"], -0.5m);
        var attainedTwo = Outcome(["a", "b"], -4m);
        var attainedOneNarrow = Outcome(["z"], -1.1m);
        var attainedOneWideZ = Outcome(["z"], -2m);
        var attainedOneWideA = Outcome(["a"], -2m);

        var ordered = new[]
            {
                unattained,
                attainedTwo,
                attainedOneNarrow,
                attainedOneWideZ,
                attainedOneWideA
            }
            .OrderBy(outcome => outcome, CriticalCounterSelectionOutcomeComparer.Instance)
            .ToArray();

        CollectionAssert.AreEqual(
            new[]
            {
                attainedOneWideA,
                attainedOneWideZ,
                attainedOneNarrow,
                attainedTwo,
                unattained
            },
            ordered);
    }

    [TestMethod]
    public void ResultOrdering_NormalizesIdsOrdinallyAndRejectsDuplicates()
    {
        var result = Outcome(["z", "a"], -2m);

        CollectionAssert.AreEqual(new[] { "a", "z" }, result.SelectedNodeIds.ToArray());
        Assert.ThrowsException<ArgumentException>(() => Outcome(["a", "a"], -2m));
    }

    [TestMethod]
    public void ResultOrdering_ForNonAttainingSetsRetainsCardinalityMarginThenOrdinalIds()
    {
        var baseline = Outcome([], 0m);
        var weakerTwo = Outcome(["a", "b"], -0.8m);
        var weakerOneZ = Outcome(["z"], -0.8m);
        var weakerOneA = Outcome(["a"], -0.8m);

        var ordered = new[] { baseline, weakerTwo, weakerOneZ, weakerOneA }
            .OrderBy(outcome => outcome, CriticalCounterSelectionOutcomeComparer.Instance)
            .ToArray();

        CollectionAssert.AreEqual(
            new[] { baseline, weakerOneA, weakerOneZ, weakerTwo },
            ordered);
    }

    [TestMethod]
    public void Projection_RejectsSelectionsOutsideTheEligibleSet()
    {
        var graph = GraphWith([Node("target"), Node("evidence", kind: "evidence")], []);

        Assert.ThrowsException<ArgumentException>(() =>
            CriticalCounterV1Contract.BuildActiveProjection(graph, "target", ["evidence"]));
    }

    private static CriticalCounterSelectionOutcome Outcome(
        IEnumerable<string> selectedNodeIds,
        decimal resultingLogOdds) =>
        CriticalCounterSelectionOutcome.Create(selectedNodeIds, resultingLogOdds);

    private static Graph GraphWith(IEnumerable<GraphNode> nodes, IEnumerable<GraphEdge> edges) =>
        new()
        {
            Slug = "critical-counter-contract",
            Nodes = nodes.ToList(),
            Edges = edges.ToList()
        };

    private static GraphNode Node(string id, string kind = "claim") =>
        new()
        {
            Id = id,
            Kind = kind
        };

    private static GraphEdge Edge(
        string id,
        string from,
        string to,
        string kind = "support",
        decimal weight = 1m) =>
        new()
        {
            Id = id,
            From = from,
            To = to,
            Kind = kind,
            ImportanceToParent = weight
        };
}
