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
                Node("R", 0.2m, "root"),
                Node(
                    "O1",
                    kind: "objection",
                    posteriorOdds: (decimal)Math.Log(100d)),
                Node(
                    "O2",
                    kind: "objection",
                    posteriorOdds: (decimal)Math.Log(100d))
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
            "  benchmark-set-success  ",
            CancellationToken.None);

        Assert.IsNotNull(result);
        CollectionAssert.AreEqual(new[] { "O1" }, result);
        var run = AssertSingleRun(store);
        Assert.AreEqual("benchmark-set-success", run.BenchmarkSetId);
        Assert.AreEqual(1L, run.RunNumber);
        Assert.AreEqual(PerformanceAlgorithmNames.MinimalCounterSet, run.Algorithm.Name);
        Assert.AreEqual(PerformanceAlgorithmImplementations.Greedy, run.Algorithm.Implementation);
        Assert.AreEqual("graph-posterior-odds-calculator", run.Algorithm.CalculationModel);
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
        CollectionAssert.AreEqual(
            new[] { "O1" },
            DetailStringArray(run, "returnedNodeIds"));
        Assert.IsFalse(Detail<bool>(run, "returnedNodeIdsTruncated"));
        AssertCommonTiming(run, expectLoad: true, expectPersist: false);
    }

    [TestMethod]
    public async Task GetBoundedMinimalCounterSetAsync_CapturesCompletedExhaustiveDetails()
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
        Assert.AreEqual("proven", result.ProofStatus);
        Assert.AreEqual("completed", result.Status);
        Assert.AreEqual("completed", result.StopReason);
        Assert.AreEqual(
            BoundedBruteForceMinimalCounterSetSolver.TimeBudgetMilliseconds,
            result.TimeBudgetMilliseconds);
        Assert.IsNotNull(result.CounterNodeIds);
        CollectionAssert.AreEqual(new[] { "O00", "O01" }, result.CounterNodeIds);

        var run = AssertSingleRun(store);
        Assert.AreEqual(result.RunNumber, run.RunNumber);
        Assert.AreEqual(PerformanceAlgorithmNames.MinimalCounterSet, run.Algorithm.Name);
        Assert.AreEqual(
            PerformanceAlgorithmImplementations.TimeBoundedExhaustive,
            run.Algorithm.Implementation);
        Assert.AreEqual(
            "graph-posterior-odds-calculator",
            run.Algorithm.CalculationModel);
        Assert.AreEqual(PerformanceRunStatuses.Completed, run.Outcome.Status);
        Assert.AreEqual(
            BoundedBruteForceMinimalCounterSetSolver.TimeBudgetMilliseconds,
            run.Invocation.Parameters["timeBudgetMilliseconds"]!.GetValue<double>());
        Assert.AreEqual(21, Detail<int>(run, "totalCandidateCount"));
        Assert.AreEqual(21, Detail<int>(run, "searchedCandidateCount"));
        Assert.AreEqual(0, Detail<int>(run, "excludedCandidateCount"));
        Assert.AreEqual(23L, Detail<long>(run, "subsetEvaluations"));
        Assert.AreEqual(1, Detail<int>(run, "largestCardinalityFullyExhausted"));
        Assert.AreEqual("2097152", Detail<string>(run, "totalPossibleSubsets"));
        Assert.AreEqual(
            BoundedBruteForceMinimalCounterSetSolver.TimeBudgetMilliseconds,
            Detail<double>(run, "timeBudgetMilliseconds"));
        Assert.IsNull(run.Details["activeCardinality"]);
        Assert.IsNull(run.Details["timeoutStage"]);
        Assert.AreEqual(2, Detail<int>(run, "returnedSetSize"));
        Assert.AreEqual("proven", Detail<string>(run, "proofStatus"));
        Assert.AreEqual("completed", Detail<string>(run, "stopReason"));
        Assert.IsTrue(Detail<bool>(run, "thresholdReached"));
        CollectionAssert.AreEqual(
            new[] { "O00", "O01" },
            DetailStringArray(run, "returnedNodeIds"));
        Assert.IsFalse(Detail<bool>(run, "returnedNodeIdsTruncated"));
        AssertCommonTiming(run, expectLoad: true, expectPersist: false);
    }

    [TestMethod]
    public async Task GetBoundedMinimalCounterSetAsync_PersistsAndReturnsExpectedTimeout()
    {
        var graph = GraphWith([Node("R", kind: "root")], []);
        var repository = RepositoryReturning(graph);
        var store = new CapturingPerformanceRunStore();
        var calculator = new GraphLikelihoodCalculator();
        var timeProvider = new ManualTimeProvider();
        var evaluator = new FixedMinimalCounterSetEvaluator(
            candidateCount: 4,
            initialTargetLogOdds: 0m,
            contribution: 0.1m,
            beforeContribution: () =>
                timeProvider.Advance(TimeSpan.FromMilliseconds(3)));
        var service = new GraphService(
            repository.Object,
            calculator,
            new GreedyMinimalCounterSetSolver(evaluator),
            new BoundedBruteForceMinimalCounterSetSolver(
                evaluator,
                timeProvider,
                TimeSpan.FromMilliseconds(5)),
            store);

        var result = await service.GetBoundedMinimalCounterSetAsync(
            graph.Slug,
            "R",
            "timeout-set",
            CancellationToken.None);

        Assert.IsNotNull(result);
        Assert.IsNull(result.CounterNodeIds);
        Assert.AreEqual("notProven", result.ProofStatus);
        Assert.AreEqual("timedOut", result.Status);
        Assert.AreEqual("timeBudget", result.StopReason);
        Assert.AreEqual(5d, result.TimeBudgetMilliseconds);
        Assert.AreEqual(1L, result.RunNumber);

        var run = AssertSingleRun(store);
        Assert.AreEqual("timeout-set", run.BenchmarkSetId);
        Assert.AreEqual(
            PerformanceAlgorithmImplementations.TimeBoundedExhaustive,
            run.Algorithm.Implementation);
        Assert.AreEqual(
            "graph-posterior-odds-calculator",
            run.Algorithm.CalculationModel);
        Assert.AreEqual(PerformanceRunStatuses.TimedOut, run.Outcome.Status);
        Assert.AreEqual(0, run.Outcome.ResultCount);
        Assert.IsNotNull(run.Outcome.ResultDigest);
        Assert.AreEqual(
            5d,
            run.Invocation.Parameters["timeBudgetMilliseconds"]!.GetValue<double>());
        Assert.AreEqual(4, Detail<int>(run, "totalCandidateCount"));
        Assert.AreEqual(4, Detail<int>(run, "searchedCandidateCount"));
        Assert.AreEqual(0, Detail<int>(run, "excludedCandidateCount"));
        Assert.AreEqual(2, Detail<int>(run, "candidatesExamined"));
        Assert.AreEqual(3L, Detail<long>(run, "subsetEvaluations"));
        Assert.AreEqual(0, Detail<int>(run, "largestCardinalityFullyExhausted"));
        Assert.AreEqual(1, Detail<int>(run, "activeCardinality"));
        Assert.AreEqual(
            2L,
            Detail<long>(run, "subsetEvaluationsAtActiveCardinality"));
        Assert.AreEqual(
            "4",
            Detail<string>(run, "totalSubsetsAtActiveCardinality"));
        Assert.AreEqual("16", Detail<string>(run, "totalPossibleSubsets"));
        Assert.AreEqual(5d, Detail<double>(run, "timeBudgetMilliseconds"));
        Assert.AreEqual(0d, Detail<double>(run, "preparationElapsedMilliseconds"));
        Assert.AreEqual(6d, Detail<double>(run, "searchElapsedMilliseconds"));
        Assert.IsTrue(Detail<double>(run, "subsetEvaluationsPerSecond") > 0d);
        Assert.AreEqual("search", Detail<string>(run, "timeoutStage"));
        Assert.AreEqual("notProven", Detail<string>(run, "proofStatus"));
        Assert.AreEqual("timeBudget", Detail<string>(run, "stopReason"));
        Assert.IsFalse(Detail<bool>(run, "thresholdReached"));
        AssertCommonTiming(run, expectLoad: true, expectPersist: false);
    }

    [TestMethod]
    public async Task GetBoundedMinimalCounterSetAsync_ReportsEmptySetAsProven()
    {
        var graph = GraphWith([Node("R", kind: "root")], []);
        var repository = RepositoryReturning(graph);
        var store = new CapturingPerformanceRunStore();
        var calculator = new GraphLikelihoodCalculator();
        var evaluator = new FixedMinimalCounterSetEvaluator(
            candidateCount: 25,
            initialTargetLogOdds: -1m);
        var service = new GraphService(
            repository.Object,
            calculator,
            new GreedyMinimalCounterSetSolver(evaluator),
            new BoundedBruteForceMinimalCounterSetSolver(evaluator),
            store);

        var result = await service.GetBoundedMinimalCounterSetAsync(
            graph.Slug,
            "R",
            CancellationToken.None);

        Assert.IsNotNull(result);
        Assert.AreEqual("proven", result.ProofStatus);
        Assert.AreEqual("completed", result.Status);
        Assert.AreEqual("completed", result.StopReason);
        Assert.IsNotNull(result.CounterNodeIds);
        Assert.AreEqual(0, result.CounterNodeIds.Count);

        var run = AssertSingleRun(store);
        Assert.AreEqual(PerformanceRunStatuses.Completed, run.Outcome.Status);
        Assert.AreEqual(0, run.Outcome.ResultCount);
        Assert.AreEqual("proven", Detail<string>(run, "proofStatus"));
        Assert.AreEqual("completed", Detail<string>(run, "stopReason"));
        Assert.AreEqual(0, Detail<int>(run, "returnedSetSize"));
        Assert.AreEqual(1L, Detail<long>(run, "subsetEvaluations"));
        Assert.IsTrue(Detail<bool>(run, "thresholdReached"));
        Assert.AreEqual(0, DetailStringArray(run, "returnedNodeIds").Length);
    }

    [TestMethod]
    public async Task MinimalCounterSetPreview_IsCappedAtTwentyAndMarksTruncation()
    {
        var graph = GraphWith([Node("R", kind: "root")], []);
        var repository = RepositoryReturning(graph);
        var store = new CapturingPerformanceRunStore();
        var calculator = new GraphLikelihoodCalculator();
        var evaluator = new FixedMinimalCounterSetEvaluator(25);
        var service = new GraphService(
            repository.Object,
            calculator,
            new GreedyMinimalCounterSetSolver(evaluator),
            new BoundedBruteForceMinimalCounterSetSolver(evaluator),
            store);

        var result = await service.GetMinimalCounterSetAsync(
            graph.Slug,
            "R",
            CancellationToken.None);

        Assert.IsNull(result);
        var run = AssertSingleRun(store);
        Assert.AreEqual(25, Detail<int>(run, "returnedSetSize"));
        CollectionAssert.AreEqual(
            Enumerable.Range(0, 20).Select(index => $"O{index:00}").ToArray(),
            DetailStringArray(run, "returnedNodeIds"));
        Assert.IsTrue(Detail<bool>(run, "returnedNodeIdsTruncated"));
    }

    [TestMethod]
    public async Task EvidenceAndRobustnessPreviews_AreBoundedAndPreserveResultOrder()
    {
        var nodes = new List<GraphNode> { Node("R", kind: "root") };
        var edges = new List<GraphEdge>();
        for (var index = 0; index < 6; index++)
        {
            var supportingNodeId = $"S{index:00}";
            var counterNodeId = $"C{index:00}";
            nodes.Add(Node(supportingNodeId, kind: "evidence"));
            nodes.Add(Node(counterNodeId, kind: "objection"));
            edges.Add(Edge(
                $"E-{supportingNodeId}-R",
                supportingNodeId,
                "R",
                "support",
                2m + index));
            edges.Add(Edge(
                $"E-{counterNodeId}-R",
                counterNodeId,
                "R",
                "rebut",
                0.5m - (index * 0.05m)));
        }

        var graph = GraphWith(nodes, edges);
        var repository = RepositoryReturning(graph);
        var store = new CapturingPerformanceRunStore();
        var service = CreateService(repository.Object, store);

        var evidence = await service.GetEvidenceImpactRankingAsync(
            graph.Slug,
            "R",
            CancellationToken.None);
        var ranking = await service.GetNodeRobustnessRankingAsync(
            graph.Slug,
            CancellationToken.None);

        Assert.IsNotNull(evidence);
        Assert.IsNotNull(ranking);
        Assert.AreEqual(6, evidence.SupportingEvidence.Count);
        Assert.AreEqual(6, evidence.CounterEvidence.Count);
        Assert.AreEqual(13, ranking.Count);

        var supportingPreview = store.Runs[0].Details["supportingPreview"]!.AsArray();
        var counterPreview = store.Runs[0].Details["counterPreview"]!.AsArray();
        Assert.AreEqual(5, supportingPreview.Count);
        Assert.AreEqual(5, counterPreview.Count);
        CollectionAssert.AreEqual(
            evidence.SupportingEvidence.Take(5).Select(item => item.NodeId).ToArray(),
            supportingPreview
                .Select(item => item!["nodeId"]!.GetValue<string>())
                .ToArray());
        CollectionAssert.AreEqual(
            evidence.CounterEvidence.Take(5).Select(item => item.NodeId).ToArray(),
            counterPreview
                .Select(item => item!["nodeId"]!.GetValue<string>())
                .ToArray());

        var rankingPreview = store.Runs[1].Details["rankingPreview"]!.AsArray();
        Assert.AreEqual(10, rankingPreview.Count);
        CollectionAssert.AreEqual(
            ranking.Take(10).Select(item => item.NodeId).ToArray(),
            rankingPreview
                .Select(item => item!["nodeId"]!.GetValue<string>())
                .ToArray());
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
        var supportingPreview = evidenceRun.Details["supportingPreview"]!.AsArray();
        var counterPreview = evidenceRun.Details["counterPreview"]!.AsArray();
        Assert.AreEqual(1, supportingPreview.Count);
        Assert.AreEqual("E1", supportingPreview[0]!["nodeId"]!.GetValue<string>());
        Assert.IsNotNull(supportingPreview[0]!["targetLogOddsImpact"]);
        Assert.AreEqual(1, counterPreview.Count);
        Assert.AreEqual("O1", counterPreview[0]!["nodeId"]!.GetValue<string>());
        Assert.IsNotNull(counterPreview[0]!["targetLogOddsImpact"]);

        var leastRobustRun = store.Runs[1];
        AssertCommonCalculationRun(
            leastRobustRun,
            PerformanceAlgorithmNames.LeastRobustNode,
            graph);
        Assert.AreEqual(3, Detail<int>(leastRobustRun, "nodesEvaluated"));
        Assert.AreEqual(2, Detail<int>(leastRobustRun, "edgesExamined"));
        Assert.AreEqual(2, Detail<int>(leastRobustRun, "leafCount"));
        Assert.IsNotNull(leastRobustRun.Details["selectedNodeId"]);
        Assert.IsNotNull(leastRobustRun.Details["selectedNodeTitle"]);

        var rankingRun = store.Runs[2];
        AssertCommonCalculationRun(
            rankingRun,
            PerformanceAlgorithmNames.RobustnessRanking,
            graph);
        Assert.AreEqual(3, Detail<int>(rankingRun, "nodesEvaluated"));
        Assert.AreEqual(2, Detail<int>(rankingRun, "edgesExamined"));
        Assert.AreEqual(3, Detail<int>(rankingRun, "rankedItemCount"));
        var rankingPreview = rankingRun.Details["rankingPreview"]!.AsArray();
        Assert.AreEqual(3, rankingPreview.Count);
        Assert.AreEqual(
            ranking![0].NodeId,
            rankingPreview[0]!["nodeId"]!.GetValue<string>());
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
        var graphAfterUpdate = GraphWith(
            [
                Node("A"),
                Node("B"),
                Node("F", 0m, "evidence")
            ],
            [
                Edge("E-F-B", "F", "B", "support", 10m),
                Edge("E-B-A", "B", "A", "support", 10m)
            ]);
        graphAfterUpdate.Nodes.Single(candidate => candidate.Id == "F").PosteriorOdds =
            -0.5m;
        var update = new GraphNodeUpdateDto { PriorOdds = 1m };
        var repository = new Mock<IGraphRepository>();
        repository
            .SetupSequence(candidate => candidate.GetBySlugAsync(
                graphBeforeUpdate.Slug,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(graphBeforeUpdate)
            .ReturnsAsync(graphAfterUpdate);
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
            "benchmark-set-leaf",
            CancellationToken.None);

        Assert.IsTrue(updated);
        Assert.AreEqual(
            -0.5m,
            graphBeforeUpdate.Nodes.Single(candidate => candidate.Id == "F").PriorOdds);
        Assert.AreEqual(
            0m,
            graphAfterUpdate.Nodes.Single(candidate => candidate.Id == "F").PriorOdds);
        var leafBayesFactor = Math.Exp(-0.5d);
        var expectedIntermediatePosterior = (decimal)Math.Log(
            leafBayesFactor / ((leafBayesFactor * 0.1d) + 0.9d));
        var run = AssertSingleRun(store);
        Assert.AreEqual("benchmark-set-leaf", run.BenchmarkSetId);
        Assert.AreEqual(PerformanceAlgorithmNames.LeafUpdate, run.Algorithm.Name);
        Assert.AreEqual(PerformanceAlgorithmImplementations.Current, run.Algorithm.Implementation);
        Assert.AreEqual(
            "graph-posterior-odds-calculator",
            run.Algorithm.CalculationModel);
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
        Assert.AreEqual(3, run.Outcome.ResultCount);
        Assert.AreEqual(3, Detail<int>(run, "affectedNodeCount"));
        Assert.AreEqual(2, Detail<int>(run, "maximumAncestorDistance"));
        Assert.AreEqual(3, Detail<int>(run, "persistedRowCount"));
        Assert.AreEqual("node-and-ancestors", Detail<string>(run, "recalculationScope"));
        Assert.AreEqual("evidence", Detail<string>(run, "changedNodeKind"));
        Assert.IsTrue(Detail<bool>(run, "triggered"));
        Assert.IsTrue(Detail<bool>(run, "isLeaf"));
        AssertCommonTiming(run, expectLoad: true, expectPersist: true);
        repository.Verify(candidate => candidate.GetBySlugAsync(
            graphBeforeUpdate.Slug,
            It.IsAny<CancellationToken>()), Times.Exactly(2));
        repository.Verify(candidate => candidate.UpdateNodePosteriorOddsBatchAsync(
            graphAfterUpdate.Id,
            It.Is<IReadOnlyDictionary<string, decimal>>(values =>
                values.Count == 3 &&
                values.ContainsKey("A") &&
                Math.Abs(values["B"] - expectedIntermediatePosterior) < 0.000001m &&
                values.ContainsKey("F")),
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
        var graphAfterUpdate = GraphWith(
            [
                Node("A"),
                Node("B"),
                Node("F", 0m, "evidence")
            ],
            [
                Edge("E-F-B", "F", "B", "support", 10m),
                Edge("E-B-A", "B", "A", "support", 10m)
            ]);
        graphAfterUpdate.Nodes.Single(candidate => candidate.Id == "F").PosteriorOdds =
            -0.5m;
        var update = new GraphNodeUpdateDto { PriorOdds = 1m };
        var persistenceFailure = new InvalidOperationException("persist failed");
        var repository = new Mock<IGraphRepository>();
        repository
            .SetupSequence(candidate => candidate.GetBySlugAsync(
                graph.Slug,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(graph)
            .ReturnsAsync(graphAfterUpdate);
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
                "benchmark-set-leaf-failed",
                CancellationToken.None));

        Assert.AreSame(persistenceFailure, thrown);
        var run = AssertSingleRun(store);
        Assert.AreEqual("benchmark-set-leaf-failed", run.BenchmarkSetId);
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
        Assert.AreEqual(3, Detail<int>(run, "affectedNodeCount"));
        Assert.AreEqual(0, Detail<int>(run, "persistedRowCount"));
        Assert.AreEqual("node-and-ancestors", Detail<string>(run, "recalculationScope"));
        Assert.IsTrue(Detail<bool>(run, "triggered"));
        Assert.IsTrue(Detail<bool>(run, "isLeaf"));
        AssertCommonTiming(run, expectLoad: true, expectPersist: true);
        repository.Verify(candidate => candidate.GetBySlugAsync(
            graph.Slug,
            It.IsAny<CancellationToken>()), Times.Exactly(2));
        repository.Verify(candidate => candidate.UpdateNodePosteriorOddsBatchAsync(
            graphAfterUpdate.Id,
            It.Is<IReadOnlyDictionary<string, decimal>>(values =>
                values.Count == 3 &&
                values.ContainsKey("A") &&
                values.ContainsKey("B") &&
                values.ContainsKey("F")),
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
                "benchmark-set-failed",
                CancellationToken.None));

        StringAssert.Contains(exception.Message, "missing");
        var run = AssertSingleRun(store);
        Assert.AreEqual("benchmark-set-failed", run.BenchmarkSetId);
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
                "benchmark-set-cancelled",
                cancellation.Token));

        var run = AssertSingleRun(store);
        Assert.AreEqual("benchmark-set-cancelled", run.BenchmarkSetId);
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
        Assert.AreEqual("graph-posterior-odds-calculator", run.Algorithm.CalculationModel);
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

    private static string[] DetailStringArray(
        PerformanceRunRecord run,
        string name)
    {
        return run.Details[name]!
            .AsArray()
            .Select(item => item!.GetValue<string>())
            .ToArray();
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
        var evaluator = new BayesianMinimalCounterSetEvaluator(
            new GraphPosteriorOddsCalculator());
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
            Node("R", 3m, "root")
        };
        var edges = new List<GraphEdge>();

        for (var index = 0; index < 21; index++)
        {
            var nodeId = $"O{index:00}";
            nodes.Add(Node(
                nodeId,
                kind: "objection",
                posteriorOdds: (decimal)Math.Log(100d)));
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
        string kind = "claim",
        decimal? posteriorOdds = null)
    {
        return new GraphNode
        {
            Id = id,
            Kind = kind,
            Title = id,
            BodyText = id,
            PriorOdds = logOdds,
            PosteriorOdds = posteriorOdds ?? logOdds
        };
    }

    private static GraphEdge Edge(
        string id,
        string from,
        string to,
        string kind,
        decimal likelihoodRatio)
    {
        return new GraphEdge
        {
            Id = id,
            From = from,
            To = to,
            Kind = kind,
            ProbabilityGivenParent = likelihoodRatio >= 1m
                ? 1m
                : likelihoodRatio,
            ProbabilityGivenNotParent = likelihoodRatio >= 1m
                ? 1m / likelihoodRatio
                : 1m
        };
    }

    private sealed class CapturingPerformanceRunStore : IPerformanceRunStore
    {
        public List<PerformanceRunRecord> Runs { get; } = [];

        public Task<PerformanceReportDocument> ReadAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(new PerformanceReportDocument
            {
                Runs = Runs.ToList()
            });
        }

        public Task<PerformanceRunRecord> AppendAsync(
            PerformanceRunRecord run,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var stored = run with { RunNumber = Runs.Count + 1L };
            Runs.Add(stored);
            return Task.FromResult(stored);
        }

        public Task<PerformanceBenchmarkSet> CreateBenchmarkSetAsync(
            string name,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(new PerformanceBenchmarkSet
            {
                Id = Guid.NewGuid().ToString("N"),
                Name = name,
                CreatedAtUtc = DateTimeOffset.UtcNow
            });
        }
    }

    private sealed class FixedMinimalCounterSetEvaluator : IMinimalCounterSetEvaluator
    {
        private readonly int _candidateCount;
        private readonly decimal _initialTargetLogOdds;
        private readonly decimal _contribution;
        private readonly Action? _beforeContribution;

        public FixedMinimalCounterSetEvaluator(
            int candidateCount,
            decimal initialTargetLogOdds = 30m,
            decimal contribution = -1m,
            Action? beforeContribution = null)
        {
            _candidateCount = candidateCount;
            _initialTargetLogOdds = initialTargetLogOdds;
            _contribution = contribution;
            _beforeContribution = beforeContribution;
        }

        public IMinimalCounterSetProblem CreateProblem(
            Graph graph,
            string targetNodeId,
            IEnumerable<string> nodeIds,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return new FixedMinimalCounterSetProblem(
                _candidateCount,
                _initialTargetLogOdds,
                _contribution,
                _beforeContribution);
        }
    }

    private sealed class FixedMinimalCounterSetProblem : IMinimalCounterSetProblem
    {
        private readonly decimal _initialTargetLogOdds;
        private readonly decimal _contribution;
        private readonly Action? _beforeContribution;

        public FixedMinimalCounterSetProblem(
            int candidateCount,
            decimal initialTargetLogOdds,
            decimal contribution,
            Action? beforeContribution)
        {
            _initialTargetLogOdds = initialTargetLogOdds;
            _contribution = contribution;
            _beforeContribution = beforeContribution;
            Candidates = Enumerable.Range(0, candidateCount)
                .Select(index => new MinimalCounterCandidate(
                    $"O{index:00}",
                    candidateCount - index))
                .ToArray();
        }

        public decimal ThresholdLogOdds => -1m;

        public decimal InitialTargetLogOdds => _initialTargetLogOdds;

        public IReadOnlyList<MinimalCounterCandidate> Candidates { get; }

        public decimal GetTargetLogOddsContribution(
            string counterNodeId,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _beforeContribution?.Invoke();
            return _contribution;
        }
    }

    private sealed class ManualTimeProvider : TimeProvider
    {
        private long _timestamp;

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public override long GetTimestamp() => _timestamp;

        public override ITimer CreateTimer(
            TimerCallback callback,
            object? state,
            TimeSpan dueTime,
            TimeSpan period)
        {
            return new InertTimer();
        }

        public void Advance(TimeSpan duration)
        {
            _timestamp += duration.Ticks;
        }

        private sealed class InertTimer : ITimer
        {
            public bool Change(TimeSpan dueTime, TimeSpan period) => true;

            public void Dispose()
            {
            }

            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }
}
