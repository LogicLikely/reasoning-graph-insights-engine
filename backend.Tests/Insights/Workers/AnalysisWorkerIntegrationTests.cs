using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Backend.AnalysisWorker;
using Backend.Calculation;
using Backend.Insights.Analysis;
using Backend.Insights.Contracts;
using Backend.Insights.Workers;
using Backend.Models.Domain;

namespace Backend.Tests.Insights.Workers;

[TestClass]
[DoNotParallelize]
public sealed class AnalysisWorkerIntegrationTests
{
    private static readonly JsonSerializerOptions SerializerOptions =
        CanonicalJson.CreateSerializerOptions();
    private static readonly TimeSpan NormalTimeout = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan NormalGrace = TimeSpan.FromSeconds(3);

    [TestMethod]
    [Timeout(25_000)]
    public async Task CriticalExact_EmitsOneNormalizedOutputAndSucceededTerminal()
    {
        var graph = CriticalGraph();
        var input = new CriticalCounterV1WorkerInput(
            "worker-critical-exact",
            graph,
            "target",
            OperationStrategyNames.Exact,
            CriticalCounterV1Contract.DefaultThresholdLogOdds,
            null);
        var request = CreateRequest(
            OperationKeys.CounterCriticalSet,
            AlgorithmSemanticIdentities.CriticalCounterV1,
            input,
            new
            {
                targetNodeId = "target",
                requestedStrategy = OperationStrategyNames.Exact,
                thresholdLogOdds = CriticalCounterV1Contract.DefaultThresholdLogOdds,
                autoCandidateCutoff = (int?)null
            });

        var result = await RunWorkerAsync(request);

        AssertSuccessfulWorker(result);
        var output = result.Outputs.Single();
        Assert.AreEqual("worker-critical-exact", output.ScenarioKey);
        Assert.AreEqual(OperationKeys.CounterCriticalSet, output.OperationKey);
        Assert.AreEqual(AlgorithmSemanticIdentities.CriticalCounterV1,
            output.AlgorithmSemanticIdentity);
        Assert.AreEqual(OperationStrategyNames.Exact, output.Strategy.Requested);
        Assert.AreEqual(OperationStrategyNames.Exact, output.Strategy.Used);
        Assert.AreEqual("worker-critical", output.Identifiers.GraphSlug);
        Assert.AreEqual("42", output.Identifiers.GraphId);
        Assert.AreEqual("target", output.Identifiers.TargetNodeId);
        Assert.AreEqual(VisualizationAdmission.NotRequested, output.VisualizationAdmission);
        Assert.AreEqual(1L, output.TotalResultCardinality);
        Assert.AreEqual(1, output.Items.Count);
        Assert.AreEqual(2, output.OrderedPaths.Count);
        Assert.AreEqual(
            "sha256:25faf12463764574c1a3d7d17ba73f822e3b10789d38dc19030ad57c01214439",
            output.ResultDigest);
        Assert.AreEqual(CanonicalJson.ComputeSha256(output.Items), output.ResultDigest);
        Assert.AreEqual("exact", output.Summary.GetProperty("usedStrategy").GetString());
        Assert.AreEqual(4L,
            output.Distribution.GetProperty("evaluatedSubsetCount").GetInt64());

        var expected = new CriticalCounterV1Analyzer().Analyze(
            new CriticalCounterV1AnalysisRequest(
                graph,
                "target",
                OperationStrategyNames.Exact,
                CriticalCounterV1Contract.DefaultThresholdLogOdds,
                null));
        Assert.AreEqual(
            CanonicalJson.Canonicalize(expected.Items),
            CanonicalJson.Canonicalize(output.Items));
    }

    [TestMethod]
    [Timeout(25_000)]
    public async Task CriticalAuto_ReportsTheActuallyUsedStrategyAndReason()
    {
        var graph = CriticalGraph();
        var input = new CriticalCounterV1WorkerInput(
            "worker-critical-auto",
            graph,
            "target",
            OperationStrategyNames.Auto,
            CriticalCounterV1Contract.DefaultThresholdLogOdds,
            1);
        var request = CreateRequest(
            OperationKeys.CounterCriticalSet,
            AlgorithmSemanticIdentities.CriticalCounterV1,
            input,
            new
            {
                targetNodeId = "target",
                requestedStrategy = OperationStrategyNames.Auto,
                thresholdLogOdds = CriticalCounterV1Contract.DefaultThresholdLogOdds,
                autoCandidateCutoff = 1
            });

        var result = await RunWorkerAsync(request);

        AssertSuccessfulWorker(result);
        var output = result.Outputs.Single();
        Assert.AreEqual(OperationStrategyNames.Auto, output.Strategy.Requested);
        Assert.AreEqual(OperationStrategyNames.Greedy, output.Strategy.Used);
        Assert.AreEqual(OperationStrategyNames.Greedy,
            output.Summary.GetProperty("usedStrategy").GetString());
        Assert.AreEqual(CriticalCounterV1Analyzer.AutoGreedyReason,
            output.Summary.GetProperty("strategySelectionReason").GetString());
        Assert.AreEqual(CanonicalJson.ComputeSha256(output.Items), output.ResultDigest);
    }

    [TestMethod]
    [Timeout(25_000)]
    public async Task StrongestPath_EmitsOnlyRetainedItemPathsAndCompleteDigest()
    {
        var graph = GraphWith(
            "worker-strongest",
            0,
            [Node("root"), Node("support"), Node("counter")],
            [
                Edge("edge-support", "support", "root", 2m),
                Edge("edge-counter", "counter", "root", 0.5m)
            ]);
        var input = new StrongestPathV1WorkerInput(
            "worker-strongest",
            graph,
            "root",
            PathDirection.Down);
        var request = CreateRequest(
            OperationKeys.PathStrongest,
            AlgorithmSemanticIdentities.StrongestPathV1,
            input,
            new { startNodeId = "root", direction = PathDirection.Down });

        var result = await RunWorkerAsync(request);

        AssertSuccessfulWorker(result);
        var output = result.Outputs.Single();
        Assert.IsNull(output.Strategy.Requested);
        Assert.IsNull(output.Strategy.Used);
        Assert.AreEqual(3L, output.TotalResultCardinality);
        Assert.AreEqual(3, output.Items.Count);
        Assert.AreEqual(output.Items.Count, output.OrderedPaths.Count);
        Assert.AreEqual(CanonicalJson.ComputeSha256(output.Items), output.ResultDigest);
        Assert.AreEqual("down", output.Summary.GetProperty("direction").GetString());
        Assert.IsTrue(output.OrderedPaths.Any(path =>
            path.NodeIds.SequenceEqual(new[] { "root" }) && path.EdgeIds.Count == 0));
    }

    [TestMethod]
    [Timeout(30_000)]
    public async Task StrongestPath_LargeCompletePathsRetainTheLargestByteBoundedPrefix()
    {
        const int configuredLineLimit =
            IsolatedWorkerRunOptions.DefaultMaximumProtocolLineBytes;
        var graph = LongStrongestPathGraph(300);
        var startNodeId = LongChainNodeId(299);
        var input = new StrongestPathV1WorkerInput(
            "worker-strongest-byte-bound",
            graph,
            startNodeId,
            PathDirection.Down);
        var request = CreateRequest(
            OperationKeys.PathStrongest,
            AlgorithmSemanticIdentities.StrongestPathV1,
            input,
            new { startNodeId, direction = PathDirection.Down });
        var complete = new AnalysisWorkerDispatcher().Dispatch(request);
        var completeFrame = WorkerProtocolJson.Serialize(
            WorkerEventFrame.ForOutput(0, complete));
        Assert.IsTrue(
            Encoding.UTF8.GetByteCount(completeFrame) >
            IsolatedWorkerRunOptions.DefaultMaximumProtocolLineBytes,
            "The fixture must reproduce the default transport overflow before retention shaping.");

        var result = await RunWorkerAsync(
            request,
            new IsolatedWorkerRunOptions(
                NormalTimeout,
                NormalGrace,
                configuredLineLimit));

        AssertSuccessfulWorker(result);
        var output = result.Outputs.Single();
        Assert.AreEqual(300L, output.TotalResultCardinality);
        Assert.AreEqual(complete.ResultDigest, output.ResultDigest);
        Assert.IsTrue(output.Items.Count > 0);
        Assert.IsTrue(output.Items.Count < complete.Items.Count);
        Assert.AreEqual(output.Items.Count, output.OrderedPaths.Count);
        CollectionAssert.Contains(
            output.Warnings.ToArray(),
            AnalysisWorkerDispatcher.RetainedItemsReducedWarning);

        for (var index = 0; index < output.Items.Count; index++)
        {
            Assert.AreEqual(
                CanonicalJson.Canonicalize(complete.Items[index]),
                CanonicalJson.Canonicalize(output.Items[index]));
            var item = output.Items[index];
            var path = output.OrderedPaths[index];
            CollectionAssert.AreEqual(
                item.GetProperty("nodeIds")
                    .EnumerateArray()
                    .Select(value => value.GetString())
                    .ToArray(),
                path.NodeIds.ToArray());
            CollectionAssert.AreEqual(
                item.GetProperty("edgeIds")
                    .EnumerateArray()
                    .Select(value => value.GetString())
                    .ToArray(),
                path.EdgeIds.ToArray());
            Assert.AreEqual(
                item.GetProperty("accumulatedLogLikelihoodRatio").GetDecimal(),
                path.AccumulatedScore);
        }

        var retainedFrame = WorkerProtocolJson.Serialize(
            WorkerEventFrame.ForOutput(0, output));
        Assert.IsTrue(
            Encoding.UTF8.GetByteCount(retainedFrame) <= configuredLineLimit);

        var nextItem = complete.Items[output.Items.Count].Clone();
        var nextPath = complete.OrderedPaths[output.OrderedPaths.Count];
        var oneMoreOutput = CopyOutput(
            output,
            output.Items.Append(nextItem),
            output.OrderedPaths.Append(nextPath));
        var oneMoreFrame = WorkerProtocolJson.Serialize(
            WorkerEventFrame.ForOutput(0, oneMoreOutput));
        Assert.IsTrue(
            Encoding.UTF8.GetByteCount(oneMoreFrame) > configuredLineLimit,
            "The emitted output must retain the largest complete prefix that fits.");
    }

    [TestMethod]
    [Timeout(30_000)]
    public async Task StrongestPath_WhenOneCompleteItemCannotFitRetainsZeroItemsAndPaths()
    {
        const int configuredLineLimit = 8_000;
        var graph = LongStrongestPathGraph(300);
        var startNodeId = LongChainNodeId(299);
        var input = new StrongestPathV1WorkerInput(
            "worker-strongest-zero-retention",
            graph,
            startNodeId,
            PathDirection.Down);
        var request = CreateRequest(
            OperationKeys.PathStrongest,
            AlgorithmSemanticIdentities.StrongestPathV1,
            input,
            new { startNodeId, direction = PathDirection.Down });
        var complete = new AnalysisWorkerDispatcher().Dispatch(request);

        var result = await RunWorkerAsync(
            request,
            new IsolatedWorkerRunOptions(
                NormalTimeout,
                NormalGrace,
                configuredLineLimit));

        AssertSuccessfulWorker(result);
        var output = result.Outputs.Single();
        Assert.AreEqual(0, output.Items.Count);
        Assert.AreEqual(0, output.OrderedPaths.Count);
        Assert.AreEqual(complete.TotalResultCardinality, output.TotalResultCardinality);
        Assert.AreEqual(complete.ResultDigest, output.ResultDigest);
        CollectionAssert.Contains(
            output.Warnings.ToArray(),
            AnalysisWorkerDispatcher.RetainedItemsReducedWarning);
        var retainedFrame = WorkerProtocolJson.Serialize(
            WorkerEventFrame.ForOutput(0, output));
        Assert.IsTrue(
            Encoding.UTF8.GetByteCount(retainedFrame) <= configuredLineLimit);
    }

    [TestMethod]
    [Timeout(25_000)]
    public async Task EvidenceImpact_EmitsFrozenPartitionsAndRetainedPaths()
    {
        var graph = GraphWith(
            "worker-evidence",
            0,
            [
                Node("target", kind: "claim"),
                Node("support", kind: "evidence"),
                Node("counter", kind: "objection")
            ],
            [
                Edge("edge-support", "support", "target", 2m),
                Edge("edge-counter", "counter", "target", 0.5m)
            ]);
        var input = new EvidenceImpactV0WorkerInput(
            "worker-evidence",
            graph,
            "target");
        var request = CreateRequest(
            OperationKeys.EvidenceImpactRanking,
            AlgorithmSemanticIdentities.EvidenceImpactV0,
            input,
            new { targetNodeId = "target" });

        var result = await RunWorkerAsync(request);

        AssertSuccessfulWorker(result);
        var output = result.Outputs.Single();
        Assert.AreEqual(2L, output.TotalResultCardinality);
        Assert.AreEqual(2, output.Items.Count);
        Assert.AreEqual(2, output.OrderedPaths.Count);
        Assert.AreEqual(CanonicalJson.ComputeSha256(output.Items), output.ResultDigest);
        Assert.AreEqual("supporting",
            output.Items[0].GetProperty("partition").GetString());
        Assert.AreEqual("counter",
            output.Items[1].GetProperty("partition").GetString());
    }

    [TestMethod]
    [Timeout(25_000)]
    public async Task Robustness_EmitsAnalyzerNormalizedItemsSummaryAndDistribution()
    {
        var graph = GraphWith(
            "worker-robustness",
            7,
            [
                Node("target", kind: "claim", posteriorOdds: 0.4m, title: "Target"),
                Node("leaf", kind: "evidence", posteriorOdds: -0.2m, title: "Leaf")
            ],
            [Edge("edge-leaf-target", "leaf", "target", 2m)]);
        var input = new RobustnessV0WorkerInput("worker-robustness", graph);
        var request = CreateRequest(
            OperationKeys.NodeRobustness,
            AlgorithmSemanticIdentities.RobustnessV0,
            input,
            new { });

        var result = await RunWorkerAsync(request);

        AssertSuccessfulWorker(result);
        var output = result.Outputs.Single();
        Assert.AreEqual("7", output.Identifiers.GraphId);
        Assert.IsNull(output.Identifiers.TargetNodeId);
        Assert.AreEqual(2L, output.TotalResultCardinality);
        Assert.AreEqual(2, output.Items.Count);
        Assert.AreEqual(2, output.OrderedPaths.Count);
        Assert.AreEqual(CanonicalJson.ComputeSha256(output.Items), output.ResultDigest);
        Assert.AreEqual(2L, output.Summary.GetProperty("rankedNodeCount").GetInt64());
        Assert.AreEqual(2L, output.Distribution.GetProperty("count").GetInt64());

        var expected = new RobustnessV0Analyzer().Analyze(graph);
        Assert.AreEqual(expected.ResultDigest, output.ResultDigest);
        Assert.AreEqual(
            CanonicalJson.Canonicalize(expected.RetainedItems),
            CanonicalJson.Canonicalize(output.Items));
    }

    [TestMethod]
    [Timeout(25_000)]
    public async Task CanonicalParametersThatDisagreeWithInputFailValidation()
    {
        var graph = GraphWith(
            "worker-invalid-parameters",
            0,
            [Node("target", kind: "claim")],
            []);
        var input = new EvidenceImpactV0WorkerInput(
            "worker-invalid-parameters",
            graph,
            "target");
        var request = CreateRequest(
            OperationKeys.EvidenceImpactRanking,
            AlgorithmSemanticIdentities.EvidenceImpactV0,
            input,
            new { targetNodeId = "different-target" });

        var result = await RunWorkerAsync(request);

        Assert.AreEqual(ExecutionStatus.Failed, result.Execution.Status);
        Assert.AreEqual(FailureKind.Validation, result.Execution.Failure?.Kind);
        Assert.AreEqual("analysis-input-invalid",
            result.Execution.Failure?.ValidationFailures.Single().Code);
        Assert.AreEqual(0, result.Outputs.Count);
        Assert.IsTrue(result.ReceivedTerminalEvent);
        Assert.IsFalse(result.ForcedTermination);
        Assert.AreEqual(string.Empty, result.StandardError);
        AssertProcessWasReaped(result);
    }

    [TestMethod]
    [Timeout(25_000)]
    public async Task ExactWork_AcceptsCooperativeCancellationWithoutForcedTermination()
    {
        var request = CreateLongExactRequest(25, "worker-cooperative-cancel");
        using var callerCancellation = new CancellationTokenSource(
            TimeSpan.FromMilliseconds(750));

        var result = await RunWorkerAsync(
            request,
            new IsolatedWorkerRunOptions(
                TimeSpan.FromSeconds(15),
                TimeSpan.FromSeconds(4)),
            callerCancellation.Token);

        Assert.AreEqual(ExecutionStatus.Cancelled, result.Execution.Status);
        Assert.AreEqual(FailureKind.Cancellation, result.Execution.Failure?.Kind);
        Assert.AreEqual(0, result.Outputs.Count);
        Assert.IsTrue(result.ReceivedTerminalEvent);
        Assert.IsFalse(result.ForcedTermination);
        Assert.AreEqual(string.Empty, result.StandardError);
        AssertProcessWasReaped(result);
    }

    [TestMethod]
    [Timeout(25_000)]
    public async Task ExactWork_HardDeadlineKillsTheWholeWorkerProcess()
    {
        var request = CreateLongExactRequest(25, "worker-hard-timeout");

        var result = await RunWorkerAsync(
            request,
            new IsolatedWorkerRunOptions(
                TimeSpan.FromMilliseconds(350),
                TimeSpan.Zero));

        Assert.AreEqual(ExecutionStatus.TimedOut, result.Execution.Status);
        Assert.AreEqual(FailureKind.Timeout, result.Execution.Failure?.Kind);
        Assert.AreEqual(0, result.Outputs.Count);
        Assert.IsTrue(result.ForcedTermination);
        AssertProcessWasReaped(result);
    }

    private static WorkerRequestFrame CreateLongExactRequest(
        int candidateCount,
        string scenarioKey)
    {
        var nodes = new List<GraphNode> { Node("target", kind: "claim") };
        var edges = new List<GraphEdge>();
        for (var index = 0; index < candidateCount; index++)
        {
            var nodeId = $"counter-{index:D2}";
            nodes.Add(Node(nodeId, kind: "objection"));
            edges.Add(Edge($"edge-{index:D2}", nodeId, "target", 1m));
        }

        var input = new CriticalCounterV1WorkerInput(
            scenarioKey,
            GraphWith("worker-long-exact", 0, nodes, edges),
            "target",
            OperationStrategyNames.Exact,
            CriticalCounterV1Contract.DefaultThresholdLogOdds,
            null);
        return CreateRequest(
            OperationKeys.CounterCriticalSet,
            AlgorithmSemanticIdentities.CriticalCounterV1,
            input,
            new
            {
                targetNodeId = "target",
                requestedStrategy = OperationStrategyNames.Exact,
                thresholdLogOdds = CriticalCounterV1Contract.DefaultThresholdLogOdds,
                autoCandidateCutoff = (int?)null
            });
    }

    private static CompactRunOutput CopyOutput(
        CompactRunOutput output,
        IEnumerable<JsonElement> items,
        IEnumerable<OrderedPathProjection> orderedPaths) => new(
        output.RunId,
        output.SampleId,
        output.ScenarioKey,
        output.OperationKey,
        output.AlgorithmSemanticIdentity,
        output.Strategy,
        output.Identifiers,
        output.CanonicalParameters,
        output.Execution,
        output.VisualizationAdmission,
        output.Summary.Clone(),
        output.Distribution.Clone(),
        output.TotalResultCardinality,
        items.ToArray(),
        output.ResultDigest,
        output.FullResultArtifactReference,
        orderedPaths.ToArray(),
        output.Warnings);

    private static WorkerRequestFrame CreateRequest<TInput, TParameters>(
        string operationKey,
        string semanticIdentity,
        TInput input,
        TParameters parameters)
    {
        var parameterValue = JsonSerializer.SerializeToElement(parameters, SerializerOptions);
        var request = new WorkerRequestFrame(
            Guid.NewGuid(),
            Guid.NewGuid(),
            operationKey,
            semanticIdentity,
            new CanonicalParameters(
                parameterValue,
                CanonicalJson.ComputeSha256(parameterValue)),
            JsonSerializer.SerializeToElement(input, SerializerOptions));
        Assert.IsTrue(
            Encoding.UTF8.GetByteCount(WorkerProtocolJson.Serialize(request)) <
            IsolatedWorkerRunOptions.DefaultMaximumProtocolLineBytes);
        return request;
    }

    private static async Task<IsolatedWorkerRunResult> RunWorkerAsync(
        WorkerRequestFrame request,
        IsolatedWorkerRunOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        return await new IsolatedWorkerRunner().RunAsync(
            CreateWorkerCommand(),
            request,
            options ?? new IsolatedWorkerRunOptions(NormalTimeout, NormalGrace),
            cancellationToken);
    }

    private static WorkerProcessCommand CreateWorkerCommand()
    {
        var workerAssembly = typeof(AnalysisWorkerMarker).Assembly.Location;
        var testRuntimeConfiguration = Path.Combine(
            AppContext.BaseDirectory,
            "backend.Tests.runtimeconfig.json");
        var testDependencies = Path.Combine(
            AppContext.BaseDirectory,
            "backend.Tests.deps.json");
        var dotNetHost = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH");
        if (string.IsNullOrWhiteSpace(dotNetHost))
        {
            dotNetHost = "dotnet";
        }

        return new WorkerProcessCommand(
            dotNetHost,
            [
                "exec",
                "--runtimeconfig",
                testRuntimeConfiguration,
                "--depsfile",
                testDependencies,
                workerAssembly
            ],
            AppContext.BaseDirectory);
    }

    private static void AssertSuccessfulWorker(IsolatedWorkerRunResult result)
    {
        Assert.AreEqual(ExecutionStatus.Succeeded, result.Execution.Status,
            result.Execution.Failure?.Message);
        Assert.AreEqual(0, result.ExitCode);
        Assert.AreEqual(0, result.Samples.Count);
        Assert.AreEqual(1, result.Outputs.Count);
        Assert.IsTrue(result.ReceivedTerminalEvent);
        Assert.IsFalse(result.ForcedTermination);
        Assert.AreEqual(string.Empty, result.StandardError);
        AssertProcessWasReaped(result);
    }

    private static void AssertProcessWasReaped(IsolatedWorkerRunResult result)
    {
        Assert.IsTrue(result.ProcessExited);
        Assert.IsNotNull(result.ProcessId);
        try
        {
            using var process = Process.GetProcessById(result.ProcessId.Value);
            Assert.IsTrue(process.HasExited,
                $"Analysis worker process {result.ProcessId.Value} is still running.");
        }
        catch (ArgumentException)
        {
            // A reaped child no longer has an addressable process ID.
        }
    }

    private static Graph CriticalGraph() => GraphWith(
        "worker-critical",
        42,
        [
            Node("target", kind: "claim", priorOdds: 1m),
            Node("counter-a", kind: "objection"),
            Node("counter-b", kind: "objection")
        ],
        [
            Edge("edge-a-b", "counter-a", "counter-b", 0.01m),
            Edge("edge-b-target", "counter-b", "target", 0.5m)
        ]);

    private static Graph LongStrongestPathGraph(int nodeCount)
    {
        var nodes = Enumerable.Range(0, nodeCount)
            .Select(index => Node(LongChainNodeId(index)))
            .ToArray();
        var edges = Enumerable.Range(0, nodeCount - 1)
            .Select(index => Edge(
                $"chain-edge-{index:D3}-with-stable-long-identifier",
                LongChainNodeId(index),
                LongChainNodeId(index + 1),
                2m))
            .ToArray();
        return GraphWith("worker-long-strongest", 0, nodes, edges);
    }

    private static string LongChainNodeId(int index) =>
        $"chain-node-{index:D3}-with-stable-long-identifier";

    private static Graph GraphWith(
        string slug,
        int id,
        IEnumerable<GraphNode> nodes,
        IEnumerable<GraphEdge> edges) => new()
        {
            Id = id,
            Slug = slug,
            Title = slug,
            Nodes = nodes.ToList(),
            Edges = edges.ToList()
        };

    private static GraphNode Node(
        string id,
        string kind = "claim",
        decimal priorOdds = 0m,
        decimal posteriorOdds = 0m,
        string? title = null) => new()
        {
            Id = id,
            Kind = kind,
            Title = title ?? id,
            PriorOdds = priorOdds,
            PosteriorOdds = posteriorOdds
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
            Kind = "worker-test",
            ImportanceToParent = likelihoodRatio
        };
}
