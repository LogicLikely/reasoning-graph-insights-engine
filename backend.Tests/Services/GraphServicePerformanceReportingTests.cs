using Backend.Calculation;
using Backend.Calculation.MinimalCounterSets;
using Backend.Models.Domain;
using Backend.Models.Dto;
using Backend.Reporting;
using Backend.Repositories;
using Backend.Services;
using Moq;

namespace backend.Tests.Services;

[TestClass]
public sealed class GraphServicePerformanceReportingTests
{
    [TestMethod]
    public async Task GetMinimalCounterSetAsync_CapturesGreedyRun()
    {
        var graph = GraphWith(
            [
                Node("R", 5m, "root"),
                Node("O1", kind: "objection"),
                Node("O2", kind: "objection")
            ],
            [
                Edge("E-O1-R", "O1", "R", "rebut", 0.1m),
                Edge("E-O2-R", "O2", "R", "rebut", 0.1m)
            ]);
        var repository = RepositoryReturning(graph);
        var store = new CapturingPerformanceRunStore();
        var service = CreateService(repository.Object, store);

        var result = await service.GetMinimalCounterSetAsync(
            graph.Slug,
            "R",
            CancellationToken.None);

        Assert.IsNotNull(result);
        CollectionAssert.AreEqual(new[] { "O1" }, result);
        var run = AssertSingleRun(store);
        Assert.AreEqual(1L, run.RunNumber);
        Assert.AreEqual(PerformanceAlgorithmNames.MinimalCounterSet, run.Algorithm.Name);
        Assert.AreEqual(PerformanceAlgorithmImplementations.Greedy, run.Algorithm.Implementation);
        Assert.AreEqual("graph-likelihood-calculator", run.Algorithm.CalculationModel);
        Assert.AreEqual("database", run.Invocation.DataSource);
        Assert.AreEqual("R", run.Invocation.TargetNodeId);
        Assert.AreEqual(PerformanceRunStatuses.Completed, run.Outcome.Status);
        Assert.AreEqual(1, run.Outcome.ResultCount);
        Assert.IsNotNull(run.Outcome.ResultDigest);
        Assert.AreEqual(2, Detail<int>(run, "totalCandidateCount"));
        Assert.AreEqual(1, Detail<int>(run, "candidatesExamined"));
        Assert.AreEqual(1, Detail<int>(run, "returnedSetSize"));
        Assert.AreEqual(0L, Detail<long>(run, "subsetEvaluations"));
        Assert.AreEqual("notApplicable", Detail<string>(run, "proofStatus"));
        Assert.IsTrue(Detail<bool>(run, "thresholdReached"));
        AssertCommonTiming(run, expectLoad: true, expectPersist: false);
    }

    [TestMethod]
    public async Task GetBoundedMinimalCounterSetAsync_ReturnsStoredRunNumberAndCapturesNotProvenDetails()
    {
        var graph = CreateGraphWithTwentyOneCounters();
        var repository = RepositoryReturning(graph);
        var store = new CapturingPerformanceRunStore();
        var service = CreateService(repository.Object, store);

        var result = await service.GetBoundedMinimalCounterSetAsync(
            graph.Slug,
            "R",
            CancellationToken.None);

        Assert.IsNotNull(result);
        Assert.AreEqual(1L, result.RunNumber);
        Assert.AreEqual("notProven", result.ProofStatus);
        Assert.IsNotNull(result.CounterNodeIds);
        CollectionAssert.AreEqual(new[] { "O00", "O01" }, result.CounterNodeIds);

        var run = AssertSingleRun(store);
        Assert.AreEqual(result.RunNumber, run.RunNumber);
        Assert.AreEqual(PerformanceAlgorithmNames.MinimalCounterSet, run.Algorithm.Name);
        Assert.AreEqual(
            PerformanceAlgorithmImplementations.BoundedBruteForce,
            run.Algorithm.Implementation);
        Assert.AreEqual(PerformanceRunStatuses.NotProven, run.Outcome.Status);
        Assert.AreEqual(
            BoundedBruteForceMinimalCounterSetSolver.CandidateLimit,
            run.Invocation.Parameters["candidateLimit"]!.GetValue<int>());
        Assert.AreEqual(21, Detail<int>(run, "totalCandidateCount"));
        Assert.AreEqual(20, Detail<int>(run, "searchedCandidateCount"));
        Assert.AreEqual(1, Detail<int>(run, "excludedCandidateCount"));
        Assert.AreEqual(22L, Detail<long>(run, "subsetEvaluations"));
        Assert.AreEqual(1, Detail<int>(run, "largestCardinalityFullyExhausted"));
        Assert.AreEqual(2, Detail<int>(run, "returnedSetSize"));
        Assert.AreEqual("notProven", Detail<string>(run, "proofStatus"));
        Assert.AreEqual("candidateLimit", Detail<string>(run, "stopReason"));
        Assert.IsTrue(Detail<bool>(run, "thresholdReached"));
        AssertCommonTiming(run, expectLoad: true, expectPersist: false);
    }

    [TestMethod]
    public async Task EvidenceAndRobustnessOperations_CaptureCommonRecordsAndTiming()
    {
        var graph = GraphWith(
            [
                Node("R", kind: "root"),
                Node("E1", kind: "evidence"),
                Node("O1", kind: "objection")
            ],
            [
                Edge("E-E1-R", "E1", "R", "support", 2m),
                Edge("E-O1-R", "O1", "R", "rebut", 0.5m)
            ]);
        var repository = RepositoryReturning(graph);
        var store = new CapturingPerformanceRunStore();
        var service = CreateService(repository.Object, store);

        var evidence = await service.GetEvidenceImpactRankingAsync(
            graph.Slug,
            "R",
            CancellationToken.None);
        var leastRobust = await service.GetLeastRobustNodeAsync(
            graph.Slug,
            CancellationToken.None);
        var ranking = await service.GetNodeRobustnessRankingAsync(
            graph.Slug,
            CancellationToken.None);

        Assert.IsNotNull(evidence);
        Assert.IsNotNull(leastRobust);
        Assert.IsNotNull(ranking);
        Assert.AreEqual(3, store.Runs.Count);

        var evidenceRun = store.Runs[0];
        AssertCommonCalculationRun(
            evidenceRun,
            PerformanceAlgorithmNames.EvidenceImpactRanking,
            graph);
        Assert.AreEqual("R", evidenceRun.Invocation.TargetNodeId);
        Assert.AreEqual(3, Detail<int>(evidenceRun, "reachableNodeCount"));
        Assert.AreEqual(2, Detail<int>(evidenceRun, "reachableEvidenceCount"));
        Assert.AreEqual(1, Detail<int>(evidenceRun, "supportingResultCount"));
        Assert.AreEqual(1, Detail<int>(evidenceRun, "counterResultCount"));

        var leastRobustRun = store.Runs[1];
        AssertCommonCalculationRun(
            leastRobustRun,
            PerformanceAlgorithmNames.LeastRobustNode,
            graph);
        Assert.AreEqual(3, Detail<int>(leastRobustRun, "nodesEvaluated"));
        Assert.AreEqual(2, Detail<int>(leastRobustRun, "edgesExamined"));
        Assert.AreEqual(2, Detail<int>(leastRobustRun, "leafCount"));
        Assert.IsNotNull(leastRobustRun.Details["selectedNodeId"]);

        var rankingRun = store.Runs[2];
        AssertCommonCalculationRun(
            rankingRun,
            PerformanceAlgorithmNames.RobustnessRanking,
            graph);
        Assert.AreEqual(3, Detail<int>(rankingRun, "nodesEvaluated"));
        Assert.AreEqual(2, Detail<int>(rankingRun, "edgesExamined"));
        Assert.AreEqual(3, Detail<int>(rankingRun, "rankedItemCount"));
    }

    [TestMethod]
    public async Task UpdateNodeAsync_PriorOddsUpdateCapturesLeafRecalculationPhasesAndValues()
    {
        var graphBeforeUpdate = GraphWith(
            [
                Node("A"),
                Node("B"),
                Node("F", -0.5m, "evidence")
            ],
            [
                Edge("E-F-B", "F", "B", "support", 10m),
                Edge("E-B-A", "B", "A", "support", 10m)
            ]);
        var update = new GraphNodeUpdateDto { PriorOdds = 1m };
        var repository = new Mock<IGraphRepository>();
        repository
            .Setup(candidate => candidate.GetBySlugAsync(
                graphBeforeUpdate.Slug,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(graphBeforeUpdate);
        repository
            .Setup(candidate => candidate.UpdateNodeAsync(
                graphBeforeUpdate.Slug,
                "F",
                update,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        repository
            .Setup(candidate => candidate.UpdateNodePosteriorOddsBatchAsync(
                graphBeforeUpdate.Id,
                It.IsAny<IReadOnlyDictionary<string, decimal>>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var store = new CapturingPerformanceRunStore();
        var service = CreateService(repository.Object, store);

        var updated = await service.UpdateNodeAsync(
            graphBeforeUpdate.Slug,
            "F",
            update,
            CancellationToken.None);

        Assert.IsTrue(updated);
        Assert.AreEqual(
            1m,
            graphBeforeUpdate.Nodes.Single(candidate => candidate.Id == "F").PriorOdds);
        var run = AssertSingleRun(store);
        Assert.AreEqual(PerformanceAlgorithmNames.LeafUpdate, run.Algorithm.Name);
        Assert.AreEqual(PerformanceAlgorithmImplementations.Current, run.Algorithm.Implementation);
        Assert.AreEqual("database", run.Invocation.DataSource);
        Assert.AreEqual("F", run.Invocation.ChangedNodeId);
        Assert.AreEqual("priorOdds", run.Invocation.ChangedField);
        Assert.AreEqual(-0.5m, run.Invocation.OldValue!.GetValue<decimal>());
        Assert.AreEqual(1m, run.Invocation.NewValue!.GetValue<decimal>());
        CollectionAssert.AreEqual(
            new[] { "priorOdds" },
            run.Invocation.Parameters["changedFields"]!
                .AsArray()
                .Select(value => value!.GetValue<string>())
                .ToArray());
        Assert.AreEqual(PerformanceRunStatuses.Completed, run.Outcome.Status);
        Assert.AreEqual(2, run.Outcome.ResultCount);
        Assert.AreEqual(2, Detail<int>(run, "affectedNodeCount"));
        Assert.AreEqual(2, Detail<int>(run, "maximumAncestorDistance"));
        Assert.AreEqual(2, Detail<int>(run, "persistedRowCount"));
        Assert.AreEqual("ancestors-only", Detail<string>(run, "recalculationScope"));
        Assert.AreEqual("evidence", Detail<string>(run, "changedNodeKind"));
        Assert.IsTrue(Detail<bool>(run, "triggered"));
        Assert.IsTrue(Detail<bool>(run, "isLeaf"));
        AssertCommonTiming(run, expectLoad: true, expectPersist: true);
        repository.Verify(candidate => candidate.GetBySlugAsync(
            graphBeforeUpdate.Slug,
            It.IsAny<CancellationToken>()), Times.Once);
        repository.Verify(candidate => candidate.UpdateNodePosteriorOddsBatchAsync(
            graphBeforeUpdate.Id,
            It.Is<IReadOnlyDictionary<string, decimal>>(values =>
                values.Count == 2 &&
                values.ContainsKey("A") &&
                values.ContainsKey("B")),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestMethod]
    public async Task UpdateNodeAsync_PersistenceFailureRecordsFailedLeafRunAndRethrowsOriginalException()
    {
        var graph = GraphWith(
            [
                Node("A"),
                Node("B"),
                Node("F", -0.5m, "evidence")
            ],
            [
                Edge("E-F-B", "F", "B", "support", 10m),
                Edge("E-B-A", "B", "A", "support", 10m)
            ]);
        var update = new GraphNodeUpdateDto { PriorOdds = 1m };
        var persistenceFailure = new InvalidOperationException("persist failed");
        var repository = new Mock<IGraphRepository>();
        repository
            .Setup(candidate => candidate.GetBySlugAsync(
                graph.Slug,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(graph);
        repository
            .Setup(candidate => candidate.UpdateNodeAsync(
                graph.Slug,
                "F",
                update,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        repository
            .Setup(candidate => candidate.UpdateNodePosteriorOddsBatchAsync(
                graph.Id,
                It.IsAny<IReadOnlyDictionary<string, decimal>>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(persistenceFailure);
        var store = new CapturingPerformanceRunStore();
        var service = CreateService(repository.Object, store);

        var thrown = await Assert.ThrowsExceptionAsync<InvalidOperationException>(() =>
            service.UpdateNodeAsync(
                graph.Slug,
                "F",
                update,
                CancellationToken.None));

        Assert.AreSame(persistenceFailure, thrown);
        var run = AssertSingleRun(store);
        Assert.AreEqual(PerformanceAlgorithmNames.LeafUpdate, run.Algorithm.Name);
        Assert.AreEqual(PerformanceRunStatuses.Failed, run.Outcome.Status);
        Assert.AreEqual(typeof(InvalidOperationException).FullName, run.Outcome.ErrorType);
        Assert.AreEqual(persistenceFailure.Message, run.Outcome.ErrorMessage);
        Assert.IsNull(run.Outcome.ResultCount);
        Assert.IsNull(run.Outcome.ResultDigest);
        Assert.AreEqual("F", run.Invocation.ChangedNodeId);
        Assert.AreEqual("priorOdds", run.Invocation.ChangedField);
        Assert.AreEqual(-0.5m, run.Invocation.OldValue!.GetValue<decimal>());
        Assert.AreEqual(1m, run.Invocation.NewValue!.GetValue<decimal>());
        Assert.AreEqual(2, Detail<int>(run, "affectedNodeCount"));
        Assert.AreEqual(0, Detail<int>(run, "persistedRowCount"));
        Assert.AreEqual("ancestors-only", Detail<string>(run, "recalculationScope"));
        Assert.IsTrue(Detail<bool>(run, "triggered"));
        Assert.IsTrue(Detail<bool>(run, "isLeaf"));
        AssertCommonTiming(run, expectLoad: true, expectPersist: true);
        repository.Verify(candidate => candidate.GetBySlugAsync(
            graph.Slug,
            It.IsAny<CancellationToken>()), Times.Once);
        repository.Verify(candidate => candidate.UpdateNodePosteriorOddsBatchAsync(
            graph.Id,
            It.Is<IReadOnlyDictionary<string, decimal>>(values =>
                values.Count == 2 &&
                values.ContainsKey("A") &&
                values.ContainsKey("B")),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestMethod]
    public async Task FailedCalculation_IsRecordedBeforeOriginalExceptionIsRethrown()
    {
        var graph = GraphWith([Node("R", kind: "root")], []);
        var repository = RepositoryReturning(graph);
        var store = new CapturingPerformanceRunStore();
        var service = CreateService(repository.Object, store);

        var exception = await Assert.ThrowsExceptionAsync<InvalidOperationException>(() =>
            service.GetEvidenceImpactRankingAsync(
                graph.Slug,
                "missing",
                CancellationToken.None));

        StringAssert.Contains(exception.Message, "missing");
        var run = AssertSingleRun(store);
        Assert.AreEqual(PerformanceAlgorithmNames.EvidenceImpactRanking, run.Algorithm.Name);
        Assert.AreEqual(PerformanceRunStatuses.Failed, run.Outcome.Status);
        Assert.AreEqual(typeof(InvalidOperationException).FullName, run.Outcome.ErrorType);
        StringAssert.Contains(run.Outcome.ErrorMessage!, "missing");
        Assert.IsNull(run.Outcome.ResultCount);
        Assert.IsNull(run.Outcome.ResultDigest);
        AssertCommonTiming(run, expectLoad: true, expectPersist: false);
    }

    [TestMethod]
    public async Task CancelledCalculation_IsRecordedBeforeCancellationIsRethrown()
    {
        var graph = GraphWith([Node("R", kind: "root")], []);
        var repository = RepositoryReturning(graph);
        var store = new CapturingPerformanceRunStore();
        var service = CreateService(repository.Object, store);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsExceptionAsync<OperationCanceledException>(() =>
            service.GetNodeRobustnessRankingAsync(
                graph.Slug,
                cancellation.Token));

        var run = AssertSingleRun(store);
        Assert.AreEqual(PerformanceAlgorithmNames.RobustnessRanking, run.Algorithm.Name);
        Assert.AreEqual(PerformanceRunStatuses.Cancelled, run.Outcome.Status);
        Assert.AreEqual(typeof(OperationCanceledException).FullName, run.Outcome.ErrorType);
        AssertCommonTiming(run, expectLoad: true, expectPersist: false);
    }

    private static void AssertCommonCalculationRun(
        PerformanceRunRecord run,
        string algorithmName,
        Graph graph)
    {
        Assert.AreEqual(algorithmName, run.Algorithm.Name);
        Assert.AreEqual(PerformanceAlgorithmImplementations.Current, run.Algorithm.Implementation);
        Assert.AreEqual("graph-likelihood-calculator", run.Algorithm.CalculationModel);
        Assert.AreEqual("database", run.Invocation.DataSource);
        Assert.AreEqual(graph.Slug, run.Graph.Slug);
        Assert.AreEqual(graph.Nodes.Count, run.Graph.NodeCount);
        Assert.AreEqual(graph.Edges.Count, run.Graph.EdgeCount);
        Assert.AreEqual(PerformanceRunStatuses.Completed, run.Outcome.Status);
        Assert.IsNotNull(run.Outcome.ResultDigest);
        AssertCommonTiming(run, expectLoad: true, expectPersist: false);
    }

    private static void AssertCommonTiming(
        PerformanceRunRecord run,
        bool expectLoad,
        bool expectPersist)
    {
        if (expectLoad)
        {
            Assert.IsNotNull(run.Timing.LoadElapsedMilliseconds);
            Assert.IsTrue(run.Timing.LoadElapsedMilliseconds.Value >= 0d);
        }
        else
        {
            Assert.IsNull(run.Timing.LoadElapsedMilliseconds);
        }

        Assert.IsTrue(run.Timing.ComputeElapsedMilliseconds >= 0d);
        Assert.IsTrue(
            run.Timing.OperationElapsedMilliseconds >=
            run.Timing.ComputeElapsedMilliseconds);

        if (expectPersist)
        {
            Assert.IsNotNull(run.Timing.PersistElapsedMilliseconds);
            Assert.IsTrue(run.Timing.PersistElapsedMilliseconds.Value >= 0d);
        }
        else
        {
            Assert.IsNull(run.Timing.PersistElapsedMilliseconds);
        }

        Assert.IsTrue(run.Resources.CpuTimeMilliseconds >= 0d);
        Assert.IsNotNull(run.Resources.AllocatedBytes);
        Assert.IsTrue(run.Resources.AllocatedBytes.Value >= 0L);
        Assert.AreEqual("processCpuTimeDelta", run.Resources.CpuMeasurement);
        Assert.AreEqual(
            "currentThreadAllocatedBytesDelta",
            run.Resources.AllocationMeasurement);
        Assert.IsTrue(run.Resources.Gen0Collections >= 0);
        Assert.IsTrue(run.Resources.Gen1Collections >= 0);
        Assert.IsTrue(run.Resources.Gen2Collections >= 0);
    }

    private static T Detail<T>(PerformanceRunRecord run, string name)
    {
        return run.Details[name]!.GetValue<T>();
    }

    private static PerformanceRunRecord AssertSingleRun(
        CapturingPerformanceRunStore store)
    {
        Assert.AreEqual(1, store.Runs.Count);
        return store.Runs[0];
    }

    private static GraphService CreateService(
        IGraphRepository repository,
        IPerformanceRunStore store)
    {
        var calculator = new GraphLikelihoodCalculator();
        var evaluator = new LegacyMinimalCounterSetEvaluator(calculator);
        return new GraphService(
            repository,
            calculator,
            new GreedyMinimalCounterSetSolver(evaluator),
            new BoundedBruteForceMinimalCounterSetSolver(evaluator),
            store);
    }

    private static Mock<IGraphRepository> RepositoryReturning(Graph graph)
    {
        var repository = new Mock<IGraphRepository>();
        repository
            .Setup(candidate => candidate.GetBySlugAsync(
                graph.Slug,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(graph);
        return repository;
    }

    private static Graph CreateGraphWithTwentyOneCounters()
    {
        var nodes = new List<GraphNode>
        {
            Node("R", 50m, "root")
        };
        var edges = new List<GraphEdge>();

        for (var index = 0; index < 21; index++)
        {
            var nodeId = $"O{index:00}";
            nodes.Add(Node(nodeId, kind: "objection"));
            edges.Add(Edge($"E-{nodeId}-R", nodeId, "R", "rebut", 0.1m));
        }

        return GraphWith(nodes, edges);
    }

    private static Graph GraphWith(
        List<GraphNode> nodes,
        List<GraphEdge> edges)
    {
        return new Graph
        {
            Id = 10,
            Slug = "sample-medium",
            Title = "Sample",
            Nodes = nodes,
            Edges = edges
        };
    }

    private static GraphNode Node(
        string id,
        decimal logOdds = 0m,
        string kind = "claim")
    {
        return new GraphNode
        {
            Id = id,
            Kind = kind,
            Title = id,
            BodyText = id,
            PriorOdds = logOdds,
            PosteriorOdds = logOdds
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

    private sealed class CapturingPerformanceRunStore : IPerformanceRunStore
    {
        public List<PerformanceRunRecord> Runs { get; } = [];

        public Task<PerformanceRunRecord> AppendAsync(
            PerformanceRunRecord run,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var stored = run with { RunNumber = Runs.Count + 1L };
            Runs.Add(stored);
            return Task.FromResult(stored);
        }
    }
}
