using Backend.Calculation;
using Backend.Calculation.MinimalCounterSets;
using Backend.Models.Domain;

namespace backend.Tests.Calculation;

[TestClass]
public sealed class MinimalCounterSetSolverTests
{
    [TestMethod]
    public void GreedySolver_UsesPriorityThenNodeIdAndStopsAtThreshold()
    {
        var problem = new FakeProblem(
            initialTargetLogOdds: 0m,
            thresholdLogOdds: -1m,
            candidates:
            [
                new MinimalCounterCandidate("B", 5m),
                new MinimalCounterCandidate("C", 4m),
                new MinimalCounterCandidate("A", 5m)
            ],
            contributions: new Dictionary<string, decimal>
            {
                ["A"] = -0.4m,
                ["B"] = -0.7m,
                ["C"] = -10m
            });

        var result = CreateGreedySolver(problem).Solve(
            new Graph(),
            "target",
            Array.Empty<string>());

        CollectionAssert.AreEqual(
            new[] { "A", "B" },
            result.CounterNodeIds.ToArray());
        Assert.IsTrue(result.ThresholdReached);
        Assert.AreEqual(-1.1m, result.FinalTargetLogOdds);
        Assert.AreEqual(3, result.TotalCandidateCount);
        Assert.AreEqual(2, result.CandidatesExamined);
        Assert.AreEqual(MinimalCounterSetProofStatus.NotApplicable, result.ProofStatus);
    }

    [TestMethod]
    public void BoundedSolver_FindsSmallerSetThanGreedyPriorityOrder()
    {
        var problem = new FakeProblem(
            initialTargetLogOdds: 0m,
            thresholdLogOdds: -1m,
            candidates:
            [
                new MinimalCounterCandidate("A", 10m),
                new MinimalCounterCandidate("B", 9m)
            ],
            contributions: new Dictionary<string, decimal>
            {
                ["A"] = -0.4m,
                ["B"] = -1.2m
            });

        var greedyResult = CreateGreedySolver(problem).Solve(
            new Graph(),
            "target",
            Array.Empty<string>());
        var boundedResult = CreateBoundedSolver(problem).Solve(
            new Graph(),
            "target",
            Array.Empty<string>());

        CollectionAssert.AreEqual(
            new[] { "A", "B" },
            greedyResult.CounterNodeIds.ToArray());
        CollectionAssert.AreEqual(
            new[] { "B" },
            boundedResult.CounterNodeIds.ToArray());
        Assert.AreEqual(3L, boundedResult.SubsetEvaluations);
        Assert.AreEqual(1, boundedResult.LargestCardinalityFullyExhausted);
        Assert.AreEqual(MinimalCounterSetProofStatus.Proven, boundedResult.ProofStatus);
    }

    [TestMethod]
    public void BoundedSolver_EnumeratesInIncreasingCardinality()
    {
        var problem = new FakeProblem(
            initialTargetLogOdds: 0m,
            thresholdLogOdds: -1m,
            candidates:
            [
                new MinimalCounterCandidate("A", 2m),
                new MinimalCounterCandidate("B", 1m)
            ],
            contributions: new Dictionary<string, decimal>
            {
                ["A"] = -0.5m,
                ["B"] = -0.6m
            });

        var result = CreateBoundedSolver(problem).Solve(
            new Graph(),
            "target",
            Array.Empty<string>());

        CollectionAssert.AreEqual(
            new[] { "A", "B" },
            result.CounterNodeIds.ToArray());
        Assert.AreEqual(4L, result.SubsetEvaluations);
        Assert.AreEqual(2, result.LargestCardinalityFullyExhausted);
        Assert.IsTrue(result.ThresholdReached);
    }

    [TestMethod]
    public void BoundedSolver_TruncatesDeterministicallyAndReportsNotProven()
    {
        var candidates = Enumerable.Range(0, 21)
            .Reverse()
            .Select(index => new MinimalCounterCandidate($"C{index:00}", 1m))
            .ToArray();
        var contributions = candidates.ToDictionary(
            candidate => candidate.NodeId,
            candidate => candidate.NodeId == "C00" ? -2m : 0m,
            StringComparer.Ordinal);
        var problem = new FakeProblem(0m, -1m, candidates, contributions);

        var result = CreateBoundedSolver(problem).Solve(
            new Graph(),
            "target",
            Array.Empty<string>());

        Assert.AreEqual(20, BoundedBruteForceMinimalCounterSetSolver.CandidateLimit);
        Assert.AreEqual(21, result.TotalCandidateCount);
        Assert.AreEqual(20, result.SearchedCandidateCount);
        Assert.AreEqual(1, result.ExcludedCandidateCount);
        Assert.AreEqual(1, result.CandidatesExamined);
        CollectionAssert.AreEqual(
            new[] { "C00" },
            result.CounterNodeIds.ToArray());
        Assert.AreEqual(MinimalCounterSetProofStatus.NotProven, result.ProofStatus);
        Assert.AreEqual(MinimalCounterSetStopReason.CandidateLimit, result.StopReason);
    }

    [TestMethod]
    public void BoundedSolver_ProvesNoSolutionWhenItExhaustsAllSubsets()
    {
        var problem = new FakeProblem(
            initialTargetLogOdds: 0m,
            thresholdLogOdds: -1m,
            candidates:
            [
                new MinimalCounterCandidate("A", 3m),
                new MinimalCounterCandidate("B", 2m),
                new MinimalCounterCandidate("C", 1m)
            ],
            contributions: new Dictionary<string, decimal>
            {
                ["A"] = 0.1m,
                ["B"] = 0.2m,
                ["C"] = 0.3m
            });

        var result = CreateBoundedSolver(problem).Solve(
            new Graph(),
            "target",
            Array.Empty<string>());

        Assert.IsFalse(result.ThresholdReached);
        Assert.AreEqual(8L, result.SubsetEvaluations);
        Assert.AreEqual(3, result.LargestCardinalityFullyExhausted);
        Assert.AreEqual(0m, result.FinalTargetLogOdds);
        Assert.AreEqual(0, result.CounterNodeIds.Count);
        Assert.AreEqual(MinimalCounterSetProofStatus.Proven, result.ProofStatus);
    }

    [TestMethod]
    [Timeout(10_000)]
    public void BoundedSolver_ExhaustsExactlyAllSubsetsAtCandidateLimit()
    {
        var candidates = Enumerable.Range(
                0,
                BoundedBruteForceMinimalCounterSetSolver.CandidateLimit)
            .Select(index => new MinimalCounterCandidate($"C{index:00}", 1m))
            .ToArray();
        var problem = new FakeProblem(
            initialTargetLogOdds: 0m,
            thresholdLogOdds: -1m,
            candidates,
            candidates.ToDictionary(candidate => candidate.NodeId, _ => 0.1m));

        var result = CreateBoundedSolver(problem).Solve(
            new Graph(),
            "target",
            Array.Empty<string>());

        Assert.IsFalse(result.ThresholdReached);
        Assert.AreEqual(1L << candidates.Length, result.SubsetEvaluations);
        Assert.AreEqual(candidates.Length, result.CandidatesExamined);
        Assert.AreEqual(
            candidates.Length,
            result.LargestCardinalityFullyExhausted);
        Assert.AreEqual(MinimalCounterSetProofStatus.Proven, result.ProofStatus);
        Assert.AreEqual(MinimalCounterSetStopReason.Completed, result.StopReason);
    }

    [TestMethod]
    public void BoundedSolver_ReportsTruncatedEmptySetAsNotProven()
    {
        var candidates = Enumerable.Range(0, 21)
            .Select(index => new MinimalCounterCandidate($"C{index:00}", 1m))
            .ToArray();
        var problem = new FakeProblem(
            -1m,
            -1m,
            candidates,
            candidates.ToDictionary(candidate => candidate.NodeId, _ => -1m));

        var result = CreateBoundedSolver(problem).Solve(
            new Graph(),
            "target",
            Array.Empty<string>());

        Assert.IsTrue(result.ThresholdReached);
        Assert.AreEqual(1L, result.SubsetEvaluations);
        Assert.AreEqual(0, result.LargestCardinalityFullyExhausted);
        Assert.AreEqual(0, result.CounterNodeIds.Count);
        Assert.AreEqual(MinimalCounterSetProofStatus.NotProven, result.ProofStatus);
        Assert.AreEqual(MinimalCounterSetStopReason.CandidateLimit, result.StopReason);
    }

    [TestMethod]
    public void Solvers_HonorCancellation()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var problem = new FakeProblem(
            0m,
            -1m,
            Array.Empty<MinimalCounterCandidate>(),
            new Dictionary<string, decimal>());

        Assert.ThrowsException<OperationCanceledException>(() =>
            CreateGreedySolver(problem).Solve(
                new Graph(),
                "target",
                Array.Empty<string>(),
                cancellation.Token));
        Assert.ThrowsException<OperationCanceledException>(() =>
            CreateBoundedSolver(problem).Solve(
                new Graph(),
                "target",
                Array.Empty<string>(),
                cancellation.Token));
    }

    [TestMethod]
    public void BoundedSolver_ChecksCancellationWhileEnumeratingSubsets()
    {
        using var cancellation = new CancellationTokenSource();
        var problem = new CancelingProblem(cancellation, cancelAfterContributionCalls: 3);

        Assert.ThrowsException<OperationCanceledException>(() =>
            CreateBoundedSolver(problem).Solve(
                new Graph(),
                "target",
                Array.Empty<string>(),
                cancellation.Token));
        Assert.AreEqual(3, problem.ContributionCalls);
    }

    [TestMethod]
    public void LegacyEvaluator_PreservesExistingPriorityAndContributionMath()
    {
        var graph = new Graph
        {
            Nodes =
            [
                Node("R", "root", priorOdds: 1m),
                Node("O1", "objection", priorOdds: -1m, posteriorOdds: 2m),
                Node("O2", "counter", priorOdds: -1m, posteriorOdds: 1m)
            ],
            Edges =
            [
                Edge("O1-R", "O1", "R", 0.5m),
                Edge("O2-R", "O2", "R", 0.5m)
            ]
        };
        var solver = new GreedyMinimalCounterSetSolver(
            new LegacyMinimalCounterSetEvaluator(
                new GraphLikelihoodCalculator()));

        var result = solver.Solve(
            graph,
            "R",
            graph.Nodes.Select(node => node.Id));

        var expectedInitialOdds = 1m +
            (decimal)Math.Log(0.5d);
        var expectedFinalOdds = expectedInitialOdds - 1m +
            (decimal)Math.Log(0.5d);
        CollectionAssert.AreEqual(
            new[] { "O1" },
            result.CounterNodeIds.ToArray());
        Assert.IsTrue(result.ThresholdReached);
        AssertApproximately(expectedInitialOdds, result.InitialTargetLogOdds);
        AssertApproximately(expectedFinalOdds, result.FinalTargetLogOdds);
    }

    [TestMethod]
    public void LegacyEvaluator_ExcludesUnreachableCounters()
    {
        var graph = new Graph
        {
            Nodes =
            [
                Node("R", "root"),
                Node("reachable", "objection", posteriorOdds: 1m),
                Node("unreachable", "objection", posteriorOdds: 10m)
            ],
            Edges = [Edge("reachable-R", "reachable", "R", 1m)]
        };
        var evaluator = new LegacyMinimalCounterSetEvaluator(
            new GraphLikelihoodCalculator());

        var problem = evaluator.CreateProblem(
            graph,
            "R",
            graph.Nodes.Select(node => node.Id));

        CollectionAssert.AreEqual(
            new[] { "reachable" },
            problem.Candidates.Select(candidate => candidate.NodeId).ToArray());
    }

    [TestMethod]
    public void BoundedSolver_HandlesNinetySixNodeGraphWithNoCounters()
    {
        var graph = new Graph
        {
            Nodes =
            [
                Node("R", "root"),
                .. Enumerable.Range(1, 95)
                    .Select(index => Node($"E{index:00}", "evidence"))
            ],
            Edges = Enumerable.Range(1, 95)
                .Select(index => Edge(
                    $"E{index:00}-R",
                    $"E{index:00}",
                    "R",
                    likelihoodRatio: 2m))
                .ToList()
        };
        var solver = new BoundedBruteForceMinimalCounterSetSolver(
            new LegacyMinimalCounterSetEvaluator(
                new GraphLikelihoodCalculator()));

        var result = solver.Solve(
            graph,
            "R",
            graph.Nodes.Select(node => node.Id));

        Assert.AreEqual(96, graph.Nodes.Count);
        Assert.AreEqual(0, result.TotalCandidateCount);
        Assert.AreEqual(1L, result.SubsetEvaluations);
        Assert.IsFalse(result.ThresholdReached);
        Assert.AreEqual(MinimalCounterSetProofStatus.Proven, result.ProofStatus);
    }

    [DataTestMethod]
    [DataRow(11, 3)]
    [DataRow(27, 9)]
    public void BoundedSolver_ProvesMinimalSetOnSmallGraphSizes(
        int nodeCount,
        int counterCount)
    {
        var graph = CreateSmallCounterGraph(nodeCount, counterCount);
        var solver = new BoundedBruteForceMinimalCounterSetSolver(
            new LegacyMinimalCounterSetEvaluator(
                new GraphLikelihoodCalculator()));

        var result = solver.Solve(
            graph,
            "R",
            graph.Nodes.Select(node => node.Id));

        Assert.AreEqual(nodeCount, graph.Nodes.Count);
        Assert.AreEqual(counterCount, result.TotalCandidateCount);
        Assert.AreEqual(counterCount, result.CounterNodeIds.Count);
        Assert.IsTrue(result.ThresholdReached);
        Assert.AreEqual(MinimalCounterSetProofStatus.Proven, result.ProofStatus);
    }

    [TestMethod]
    public void GreedySolver_TargetCounterIsNotItsOwnCandidate()
    {
        var graph = new Graph
        {
            Nodes = [Node("O3", "objection")]
        };
        var solver = new GreedyMinimalCounterSetSolver(
            new LegacyMinimalCounterSetEvaluator(
                new GraphLikelihoodCalculator()));

        var result = solver.Solve(
            graph,
            "O3",
            graph.Nodes.Select(node => node.Id));

        Assert.IsFalse(result.ThresholdReached);
        Assert.AreEqual(0, result.TotalCandidateCount);
        Assert.AreEqual(0, result.CounterNodeIds.Count);
    }

    private static GreedyMinimalCounterSetSolver CreateGreedySolver(
        IMinimalCounterSetProblem problem)
    {
        return new GreedyMinimalCounterSetSolver(new FakeEvaluator(problem));
    }

    private static BoundedBruteForceMinimalCounterSetSolver CreateBoundedSolver(
        IMinimalCounterSetProblem problem)
    {
        return new BoundedBruteForceMinimalCounterSetSolver(
            new FakeEvaluator(problem));
    }

    private static GraphNode Node(
        string id,
        string kind,
        decimal priorOdds = 0m,
        decimal posteriorOdds = 0m)
    {
        return new GraphNode
        {
            Id = id,
            Kind = kind,
            PriorOdds = priorOdds,
            PosteriorOdds = posteriorOdds
        };
    }

    private static GraphEdge Edge(
        string id,
        string from,
        string to,
        decimal likelihoodRatio)
    {
        return new GraphEdge
        {
            Id = id,
            From = from,
            To = to,
            Kind = "support",
            ProbabilityGivenParent = likelihoodRatio >= 1m
                ? 1m
                : likelihoodRatio,
            ProbabilityGivenNotParent = likelihoodRatio >= 1m
                ? 1m / likelihoodRatio
                : 1m
        };
    }

    private static Graph CreateSmallCounterGraph(int nodeCount, int counterCount)
    {
        var counterLogLikelihoodRatio = (decimal)Math.Log(0.1d);
        var nodes = new List<GraphNode>
        {
            Node(
                "R",
                "root",
                priorOdds: (counterCount * 2m) - 0.5m -
                    (counterCount * counterLogLikelihoodRatio))
        };
        var edges = new List<GraphEdge>();

        for (var index = 0; index < counterCount; index++)
        {
            var nodeId = $"O{index:00}";
            nodes.Add(Node(nodeId, "objection"));
            edges.Add(Edge($"{nodeId}-R", nodeId, "R", 0.1m));
        }

        for (var index = nodes.Count; index < nodeCount; index++)
        {
            var nodeId = $"E{index:00}";
            nodes.Add(Node(nodeId, "evidence"));
            edges.Add(Edge($"{nodeId}-R", nodeId, "R", 1m));
        }

        return new Graph
        {
            Nodes = nodes,
            Edges = edges
        };
    }

    private static void AssertApproximately(decimal expected, decimal actual)
    {
        Assert.IsTrue(
            Math.Abs(expected - actual) < 0.000000001m,
            $"Expected {expected}, but found {actual}.");
    }

    private sealed class FakeEvaluator(IMinimalCounterSetProblem problem)
        : IMinimalCounterSetEvaluator
    {
        public IMinimalCounterSetProblem CreateProblem(
            Graph graph,
            string targetNodeId,
            IEnumerable<string> nodeIds,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return problem;
        }
    }

    private sealed class FakeProblem(
        decimal initialTargetLogOdds,
        decimal thresholdLogOdds,
        IReadOnlyList<MinimalCounterCandidate> candidates,
        IReadOnlyDictionary<string, decimal> contributions)
        : IMinimalCounterSetProblem
    {
        public decimal ThresholdLogOdds => thresholdLogOdds;

        public decimal InitialTargetLogOdds => initialTargetLogOdds;

        public IReadOnlyList<MinimalCounterCandidate> Candidates => candidates;

        public decimal GetTargetLogOddsContribution(
            string counterNodeId,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return contributions[counterNodeId];
        }
    }

    private sealed class CancelingProblem(
        CancellationTokenSource cancellation,
        int cancelAfterContributionCalls)
        : IMinimalCounterSetProblem
    {
        public int ContributionCalls { get; private set; }

        public decimal ThresholdLogOdds => -1m;

        public decimal InitialTargetLogOdds => 0m;

        public IReadOnlyList<MinimalCounterCandidate> Candidates { get; } =
            Enumerable.Range(0, 20)
                .Select(index => new MinimalCounterCandidate($"C{index:00}", 1m))
                .ToArray();

        public decimal GetTargetLogOddsContribution(
            string counterNodeId,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ContributionCalls++;
            if (ContributionCalls == cancelAfterContributionCalls)
            {
                cancellation.Cancel();
            }

            return 1m;
        }
    }
}
