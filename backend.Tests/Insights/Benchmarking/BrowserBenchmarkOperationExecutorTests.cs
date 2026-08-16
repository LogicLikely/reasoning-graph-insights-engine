using System.Text;
using System.Text.Json;
using Backend.Insights.Benchmarking;
using Backend.Insights.Contracts;
using Backend.Insights.Measurement;
using Backend.Insights.Persistence;
using Backend.Insights.Workers;
using Backend.Seeding;

namespace backend.Tests.Insights.Benchmarking;

[TestClass]
public sealed class BrowserBenchmarkOperationExecutorTests
{
    private static readonly BrowserJourneyOptions Options = new(
        new Uri("http://127.0.0.1:6006/iframe.html"),
        new Uri("http://127.0.0.1:5080/"),
        "16.4",
        "0.2.0");

    [TestMethod]
    public async Task CollapsedJourney_MapsCorrelatedRawEvidenceWithoutSummingNestedPhases()
    {
        var (operation, scenario, fixture) = Case("quick.browser.collapsed.balanced-1k");
        var phases = GraphPhases();
        var terminal = Terminal(operation, scenario, fixture);
        var driver = new StubDriver(async (_, _, _) =>
        {
            await Task.Delay(5);
            return Success(phases, terminal);
        });
        var executor = new BrowserBenchmarkOperationExecutor(driver, Options);

        var result = await executor.ExecuteAsync(
            operation, scenario, fixture, BenchmarkProfiles.Quick,
            TimeSpan.FromSeconds(2), CancellationToken.None);

        Assert.AreEqual(ExecutionStatus.Succeeded, result.Execution.Status);
        Assert.AreEqual(phases.Count + 1, result.Samples.Count);
        Assert.IsTrue(result.Samples.Where(sample => sample.Layer != InsightMeasurementLayers.BenchmarkOrchestration)
            .All(sample => sample.Iteration == 0 && sample.Execution.Status == ExecutionStatus.Succeeded));
        var orchestration = result.Samples.Single(sample =>
            sample.Layer == InsightMeasurementLayers.BenchmarkOrchestration &&
            sample.Classification.IterationKind == IterationClassificationTokens.Measured);
        Assert.IsTrue(orchestration.WallClockDuration < phases.Sum(phase => phase.DurationMilliseconds));
        Assert.AreEqual(TimingBoundaryProvenance.Estimated, result.Samples.Single(sample =>
            sample.Layer == InsightMeasurementLayers.GraphMap &&
            sample.Phase == InsightMeasurementPhases.ViewportFit).TimingBoundaryProvenance);
        var output = result.Outputs.Single();
        Assert.AreEqual(1_000, output.Summary.GetProperty("actualNodeCount").GetInt64());
        Assert.AreEqual("decoded-response-body-utf8-bytes",
            output.Summary.GetProperty("responseByteSemantics").GetString());
        Assert.AreEqual("http", output.Summary.GetProperty("browserApiScheme").GetString());
        Assert.AreEqual("http/1.1", output.Summary.GetProperty("nextHopProtocol").GetString());
        Assert.AreEqual("http/1.1", output.Distribution.GetProperty("networkProtocol")
            .GetProperty("nextHopProtocol").GetString());
        Assert.IsTrue(output.Distribution.GetProperty("phaseEvidence").GetArrayLength() >= phases.Count);
        Assert.AreEqual(CanonicalJson.ComputeSha256(output.Items), output.ResultDigest);
    }

    [TestMethod]
    public async Task LatePageFailure_PreservesCompletedPhaseRowsAsSucceeded()
    {
        var (operation, scenario, fixture) = Case("quick.browser.collapsed.balanced-1k");
        var completedPhase = Phase(
            InsightMeasurementLayers.Transport,
            InsightMeasurementPhases.TimeToFirstByte,
            2,
            TimingBoundaryProvenance.DirectlyInstrumented,
            "consumer-fetch-promise");
        var failure = BenchmarkOperationExecutor.Failure(
            ExecutionStatus.Failed,
            FailureKind.Execution,
            "browser-page-error",
            "A page error occurred after response headers.");
        var terminal = Terminal(operation, scenario, fixture) with
        {
            Status = "failed",
            Failure = new BrowserJourneyFailure("browser-page-error", "late page error"),
            PageErrors = ["late page error"]
        };
        var executor = new BrowserBenchmarkOperationExecutor(
            new StubDriver((_, _, _) => Task.FromResult(new BrowserJourneyDriverResult(
                failure, [completedPhase], terminal, terminal.Environment, 42, 0, false, string.Empty))),
            Options);

        var result = await executor.ExecuteAsync(
            operation, scenario, fixture, BenchmarkProfiles.Quick,
            TimeSpan.FromSeconds(2), CancellationToken.None);

        Assert.AreEqual(ExecutionStatus.Failed, result.Execution.Status);
        Assert.AreEqual(ExecutionStatus.Succeeded, result.Samples.Single(sample =>
            sample.Phase == InsightMeasurementPhases.TimeToFirstByte).Execution.Status);
        Assert.AreEqual(ExecutionStatus.Failed, result.Samples.Single(sample =>
            sample.Layer == InsightMeasurementLayers.BenchmarkOrchestration &&
            sample.Classification.IterationKind == IterationClassificationTokens.Measured).Execution.Status);
        var evidenceOutput = result.Outputs.Single();
        Assert.IsTrue(evidenceOutput.Summary.GetProperty("browserEvidenceOnly").GetBoolean());
        Assert.AreEqual(0, evidenceOutput.TotalResultCardinality);
        Assert.AreEqual(CanonicalJson.ComputeSha256(Array.Empty<JsonElement>()), evidenceOutput.ResultDigest);
        Assert.AreEqual("consumer-fetch-promise",
            evidenceOutput.Distribution.GetProperty("phaseEvidence")[0].GetProperty("source").GetString());
    }

    [TestMethod]
    public async Task DirectViewportClaim_IsRejectedBecauseGraphMapHasNoLifecycleBoundary()
    {
        var (operation, scenario, fixture) = Case("quick.browser.collapsed.balanced-1k");
        var phases = GraphPhases().Select(phase =>
            phase.Layer == InsightMeasurementLayers.GraphMap &&
            phase.Phase == InsightMeasurementPhases.ViewportFit
                ? phase with { TimingBoundaryProvenance = TimingBoundaryProvenance.DirectlyInstrumented }
                : phase).ToArray();
        var terminal = Terminal(operation, scenario, fixture);
        var executor = new BrowserBenchmarkOperationExecutor(
            new StubDriver((_, _, _) => Task.FromResult(Success(phases, terminal))),
            Options);

        var result = await executor.ExecuteAsync(
            operation, scenario, fixture, BenchmarkProfiles.Quick,
            TimeSpan.FromSeconds(2), CancellationToken.None);

        Assert.AreEqual(ExecutionStatus.Failed, result.Execution.Status);
        Assert.AreEqual("browser-phase-provenance-invalid", result.Execution.Failure?.Code);
        Assert.IsTrue(result.Outputs.Single().Summary.GetProperty("browserEvidenceOnly").GetBoolean());
    }

    [TestMethod]
    public async Task RenderedGraphWithoutEstimatedDagreBoundary_IsRejected()
    {
        var (operation, scenario, fixture) = Case("quick.browser.collapsed.balanced-1k");
        var phases = GraphPhases().Where(phase =>
            phase.Phase != InsightMeasurementPhases.DagreLayout).ToArray();
        var terminal = Terminal(operation, scenario, fixture);
        var executor = new BrowserBenchmarkOperationExecutor(
            new StubDriver((_, _, _) => Task.FromResult(Success(phases, terminal))),
            Options);

        var result = await executor.ExecuteAsync(
            operation, scenario, fixture, BenchmarkProfiles.Quick,
            TimeSpan.FromSeconds(2), CancellationToken.None);

        Assert.AreEqual(ExecutionStatus.Failed, result.Execution.Status);
        Assert.AreEqual("browser-dagre-layout-boundary-missing", result.Execution.Failure?.Code);
    }

    [TestMethod]
    public async Task DevelopmentStorybookBuildIdentity_IsRejected()
    {
        var (operation, scenario, fixture) = Case("quick.browser.collapsed.balanced-1k");
        var terminal = Terminal(operation, scenario, fixture) with
        {
            Evidence = JsonSerializer.SerializeToElement(new
            {
                stableSelector = "[data-benchmark-state='stable']",
                harnessBuildIdentity = "storybook-development",
                nextHopProtocol = "http/1.1",
                resourceTimingLimitation = (string?)null
            })
        };
        var executor = new BrowserBenchmarkOperationExecutor(
            new StubDriver((_, _, _) => Task.FromResult(Success(GraphPhases(), terminal))),
            Options);

        var result = await executor.ExecuteAsync(
            operation, scenario, fixture, BenchmarkProfiles.Quick,
            TimeSpan.FromSeconds(2), CancellationToken.None);

        Assert.AreEqual(ExecutionStatus.Failed, result.Execution.Status);
        Assert.AreEqual("browser-harness-build-identity-invalid", result.Execution.Failure?.Code);
    }

    [TestMethod]
    public async Task MissingResourceTimingProtocolAndLimitation_IsRejected()
    {
        var (operation, scenario, fixture) = Case("quick.browser.collapsed.balanced-1k");
        var phases = GraphPhases().Select(phase =>
            phase.Layer == InsightMeasurementLayers.Transport &&
            phase.Phase == InsightMeasurementPhases.FullTransfer
                ? Phase(
                    phase.Layer,
                    phase.Phase,
                    phase.DurationMilliseconds,
                    phase.TimingBoundaryProvenance,
                    phase.Source)
                : phase).ToArray();
        var terminal = Terminal(operation, scenario, fixture) with
        {
            Evidence = JsonSerializer.SerializeToElement(new
            {
                stableSelector = "[data-benchmark-state='stable']",
                harnessBuildIdentity = BrowserBenchmarkOperationExecutor.ExpectedHarnessBuildIdentity
            })
        };
        var executor = new BrowserBenchmarkOperationExecutor(
            new StubDriver((_, _, _) => Task.FromResult(Success(phases, terminal))),
            Options);

        var result = await executor.ExecuteAsync(
            operation, scenario, fixture, BenchmarkProfiles.Quick,
            TimeSpan.FromSeconds(2), CancellationToken.None);

        Assert.AreEqual(ExecutionStatus.Failed, result.Execution.Status);
        Assert.AreEqual("browser-network-protocol-evidence-missing", result.Execution.Failure?.Code);
    }

    [TestMethod]
    public async Task ResourceTimingLimitation_IsAcceptedAndRetainedWithoutInventingProtocol()
    {
        var (operation, scenario, fixture) = Case("quick.browser.collapsed.balanced-1k");
        const string limitation = "The browser did not expose nextHopProtocol for the graph resource.";
        var phases = GraphPhases().Select(phase =>
            phase.Layer == InsightMeasurementLayers.Transport &&
            phase.Phase == InsightMeasurementPhases.FullTransfer
                ? Phase(
                    phase.Layer,
                    phase.Phase,
                    phase.DurationMilliseconds,
                    phase.TimingBoundaryProvenance,
                    phase.Source,
                    new { nextHopProtocol = (string?)null, resourceTimingLimitation = limitation })
                : phase).ToArray();
        var terminal = Terminal(operation, scenario, fixture) with
        {
            Evidence = JsonSerializer.SerializeToElement(new
            {
                stableSelector = "[data-benchmark-state='stable']",
                harnessBuildIdentity = BrowserBenchmarkOperationExecutor.ExpectedHarnessBuildIdentity,
                nextHopProtocol = (string?)null,
                resourceTimingLimitation = limitation
            })
        };
        var executor = new BrowserBenchmarkOperationExecutor(
            new StubDriver((_, _, _) => Task.FromResult(Success(phases, terminal))),
            Options);

        var result = await executor.ExecuteAsync(
            operation, scenario, fixture, BenchmarkProfiles.Quick,
            TimeSpan.FromSeconds(2), CancellationToken.None);

        Assert.AreEqual(ExecutionStatus.Succeeded, result.Execution.Status);
        Assert.AreEqual(JsonValueKind.Null,
            result.Outputs.Single().Summary.GetProperty("nextHopProtocol").ValueKind);
        Assert.AreEqual(limitation,
            result.Outputs.Single().Summary.GetProperty("resourceTimingLimitation").GetString());
    }

    [TestMethod]
    public async Task Search_AllowsUnavailableMatchAndUnionIdsButRetainsCountsAndVisibleStatus()
    {
        var (operation, scenario, fixture) = Case("quick.browser.search.compact.balanced-1k");
        var phases = GraphPhases().Concat([
            Phase(
                InsightMeasurementLayers.BrowserData,
                InsightMeasurementPhases.SearchCompletion,
                3,
                TimingBoundaryProvenance.ExternallyObserved,
                "playwright-visible-status",
                new { searchStatus = "1 matching node · 4 total shown" })
        ]).ToArray();
        var terminal = Terminal(operation, scenario, fixture) with
        {
            MatchCount = 1,
            RequiredAncestorUnionCount = 4,
            RequiredAncestorNodeIds = null,
            MatchNodeIds = null,
            TotalResultCardinality = 1,
            IdentityLimitation = "GraphMap 0.2.0 exposes counts and visible union, not match membership."
        };
        var executor = new BrowserBenchmarkOperationExecutor(
            new StubDriver((_, _, _) => Task.FromResult(Success(phases, terminal))),
            Options);

        var result = await executor.ExecuteAsync(
            operation, scenario, fixture, BenchmarkProfiles.Quick,
            TimeSpan.FromSeconds(2), CancellationToken.None);

        Assert.AreEqual(ExecutionStatus.Succeeded, result.Execution.Status);
        var item = result.Outputs.Single().Items.Single();
        Assert.AreEqual(JsonValueKind.Null, item.GetProperty("matchNodeIds").ValueKind);
        Assert.AreEqual(JsonValueKind.Null, item.GetProperty("requiredAncestorNodeIds").ValueKind);
        Assert.AreEqual(1, result.Samples.Single(sample =>
            sample.Phase == InsightMeasurementPhases.SearchCompletion).SearchCounts.Matches);
        Assert.AreEqual(4, result.Samples.Single(sample =>
            sample.Phase == InsightMeasurementPhases.SearchCompletion)
            .SearchCounts.CompleteRequiredAncestorUnion);
    }

    [TestMethod]
    public async Task Search_AcceptsCompleteDistinctRequiredNodeUnionIds()
    {
        var (operation, scenario, fixture) = Case("quick.browser.search.compact.balanced-1k");
        var phases = GraphPhases().Concat([
            Phase(
                InsightMeasurementLayers.BrowserData,
                InsightMeasurementPhases.SearchCompletion,
                3,
                TimingBoundaryProvenance.ExternallyObserved,
                "playwright-visible-status",
                new { searchStatus = "1 matching node · 4 total shown" })
        ]).ToArray();
        var terminal = Terminal(operation, scenario, fixture) with
        {
            MatchCount = 1,
            RequiredAncestorUnionCount = 4,
            RequiredAncestorNodeIds = ["node-3", "node-1", "node-2", "node-0"],
            MatchNodeIds = null,
            TotalResultCardinality = 1
        };
        var executor = new BrowserBenchmarkOperationExecutor(
            new StubDriver((_, _, _) => Task.FromResult(Success(phases, terminal))),
            Options);

        var result = await executor.ExecuteAsync(
            operation, scenario, fixture, BenchmarkProfiles.Quick,
            TimeSpan.FromSeconds(2), CancellationToken.None);

        Assert.AreEqual(ExecutionStatus.Succeeded, result.Execution.Status);
        CollectionAssert.AreEqual(
            new[] { "node-0", "node-1", "node-2", "node-3" },
            result.Outputs.Single().Items.Single().GetProperty("requiredAncestorNodeIds")
                .EnumerateArray().Select(item => item.GetString()).ToArray());
    }

    [TestMethod]
    public async Task Search_RejectsIncompleteRequiredNodeUnionIds()
    {
        var result = await ExecuteSearchWithRequiredNodeIdsAsync(
            ["node-0", "node-1", "node-2"]);

        Assert.AreEqual(ExecutionStatus.Failed, result.Execution.Status);
        Assert.AreEqual("browser-search-evidence-invalid", result.Execution.Failure?.Code);
    }

    [TestMethod]
    public async Task Search_RejectsDuplicateRequiredNodeUnionIds()
    {
        var result = await ExecuteSearchWithRequiredNodeIdsAsync(
            ["node-0", "node-1", "node-1", "node-3"]);

        Assert.AreEqual(ExecutionStatus.Failed, result.Execution.Status);
        Assert.AreEqual("browser-search-evidence-invalid", result.Execution.Failure?.Code);
    }

    [TestMethod]
    public async Task NoHitSearch_UsesEmptyCanonicalItemsWithoutLosingCountEvidence()
    {
        var (operation, scenario, fixture) = Case("quick.browser.search.no-hit.balanced-1k");
        var phases = GraphPhases().Concat([
            Phase(
                InsightMeasurementLayers.BrowserData,
                InsightMeasurementPhases.SearchCompletion,
                2,
                TimingBoundaryProvenance.ExternallyObserved,
                "playwright-visible-status",
                new { searchStatus = "0 matching nodes · 0 total shown" })
        ]).ToArray();
        var terminal = Terminal(operation, scenario, fixture) with
        {
            MatchCount = 0,
            RequiredAncestorUnionCount = 0,
            RequiredAncestorNodeIds = null,
            MatchNodeIds = null,
            TotalResultCardinality = 0,
            IdentityLimitation = "GraphMap 0.2.0 exposes counts, not match membership."
        };
        var executor = new BrowserBenchmarkOperationExecutor(
            new StubDriver((_, _, _) => Task.FromResult(Success(phases, terminal))),
            Options);

        var result = await executor.ExecuteAsync(
            operation, scenario, fixture, BenchmarkProfiles.Quick,
            TimeSpan.FromSeconds(2), CancellationToken.None);

        Assert.AreEqual(ExecutionStatus.Succeeded, result.Execution.Status);
        var output = result.Outputs.Single();
        Assert.AreEqual(0, output.TotalResultCardinality);
        Assert.AreEqual(0, output.Items.Count);
        Assert.AreEqual(CanonicalJson.ComputeSha256(Array.Empty<JsonElement>()), output.ResultDigest);
        Assert.AreEqual(0, output.Summary.GetProperty("matchCount").GetInt64());
        Assert.AreEqual(0, output.Summary.GetProperty("requiredAncestorUnionCount").GetInt64());
    }

    [TestMethod]
    public async Task ResultRender_PreservesCanonicalResultDigestAndAddsBoundedBrowserEvidence()
    {
        var (operation, scenario, fixture) = Case("quick.browser.result-render.strongest.balanced-1k");
        var canonical = new AnalysisWorkerDispatcher().Dispatch(operation.Request, CancellationToken.None);
        var phases = new[]
        {
            Phase(
                InsightMeasurementLayers.LabResult,
                InsightMeasurementPhases.ResultRender,
                2,
                TimingBoundaryProvenance.DirectlyInstrumented,
                "consumer-performance-mark"),
            Phase(
                InsightMeasurementLayers.LabResult,
                InsightMeasurementPhases.ReactCommit,
                1,
                TimingBoundaryProvenance.DirectlyInstrumented,
                "react-profiler",
                new { actualDurationMilliseconds = 0.75m }),
            Phase(
                InsightMeasurementLayers.EndToEnd,
                InsightMeasurementPhases.ActionToStableResultAndView,
                4,
                TimingBoundaryProvenance.ExternallyObserved,
                "playwright-completion-event-observation")
        };
        var terminal = Terminal(operation, scenario, fixture) with
        {
            ActualNodeCount = null,
            ActualEdgeCount = null,
            RenderedNodeCount = null,
            RenderedEdgeCount = null,
            RequestBytes = null,
            ResponseBytes = null,
            ResponsePayloadSha256 = null,
            TotalResultCardinality = canonical.TotalResultCardinality,
            BoundedResultItemCount = canonical.Items.Count
        };
        var driver = new StubDriver((request, _, _) =>
        {
            Assert.AreEqual(scenario.OperationKey,
                request.ResultPayload!.Value.GetProperty("operationId").GetString());
            Assert.AreEqual("succeeded", request.ResultPayload.Value.GetProperty("status").GetString());
            Assert.AreEqual(canonical.Items.Count,
                request.ResultPayload.Value.GetProperty("items").GetArrayLength());
            return Task.FromResult(Success(phases, terminal));
        });
        var executor = new BrowserBenchmarkOperationExecutor(driver, Options);

        var result = await executor.ExecuteAsync(
            operation, scenario, fixture, BenchmarkProfiles.Quick,
            TimeSpan.FromSeconds(2), CancellationToken.None);

        Assert.AreEqual(ExecutionStatus.Succeeded, result.Execution.Status);
        var output = result.Outputs.Single();
        Assert.AreEqual(canonical.ResultDigest, output.ResultDigest);
        Assert.AreEqual(canonical.Items.Count, output.Items.Count);
        Assert.IsTrue(output.Distribution.TryGetProperty("browserJourneyEvidence", out _));
        Assert.IsTrue(result.Samples.Any(sample =>
            sample.Classification.IterationKind == IterationClassificationTokens.Setup &&
            sample.Phase == InsightMeasurementPhases.OperationExecution));
        Assert.IsFalse(result.Samples.Any(sample => sample.Layer == InsightMeasurementLayers.GraphMap));
    }

    [TestMethod]
    public async Task ResultRender_ProjectsDeepStrongestPathsBeforePlaywrightWithoutChangingCanonicalOutput()
    {
        var fixture = DeterministicStressGraphFixtureFactory.Create(StressGraphSeedIds.Deep1K);
        var scenario = new BenchmarkScenarioDefinition(
            "test.browser.result-render.strongest.deep-1k",
            new string('t', 700),
            BenchmarkProfiles.QuickKey,
            OperationKeys.PathStrongest,
            StressGraphSeedIds.Deep1K,
            JsonSerializer.SerializeToElement(new
            {
                startNodeId = "n-00999",
                direction = "up"
            }),
            null,
            false,
            executionTarget: BenchmarkScenarioExecutionTarget.Browser,
            browserJourney: new BrowserJourneyDefinition(BrowserJourneyActions.ResultRender));
        var operation = BenchmarkOperationRequestFactory.Create(
            scenario,
            fixture,
            scenario.Parameters,
            Guid.NewGuid(),
            Guid.NewGuid());
        var canonical = new AnalysisWorkerDispatcher().Dispatch(operation.Request, CancellationToken.None);
        Assert.IsTrue(canonical.OrderedPaths.Any(path => path.NodeIds.Count > 128));
        var deepItem = canonical.Items
            .Select((item, index) => (Item: item, Index: index))
            .First(candidate => candidate.Item.GetProperty("nodeIds").GetArrayLength() > 128);
        JsonElement capturedPayload = default;
        var phases = new[]
        {
            Phase(
                InsightMeasurementLayers.LabResult,
                InsightMeasurementPhases.ResultRender,
                2,
                TimingBoundaryProvenance.DirectlyInstrumented,
                "consumer-performance-mark"),
            Phase(
                InsightMeasurementLayers.LabResult,
                InsightMeasurementPhases.ReactCommit,
                1,
                TimingBoundaryProvenance.DirectlyInstrumented,
                "react-profiler"),
            Phase(
                InsightMeasurementLayers.EndToEnd,
                InsightMeasurementPhases.ActionToStableResultAndView,
                4,
                TimingBoundaryProvenance.ExternallyObserved,
                "playwright-completion-event-observation")
        };
        var driver = new StubDriver((request, _, _) =>
        {
            capturedPayload = request.ResultPayload!.Value.Clone();
            var boundedItemCount = capturedPayload.GetProperty("items").GetArrayLength();
            var terminal = Terminal(operation, scenario, fixture) with
            {
                ActualNodeCount = null,
                ActualEdgeCount = null,
                RenderedNodeCount = null,
                RenderedEdgeCount = null,
                RequestBytes = null,
                ResponseBytes = null,
                ResponsePayloadSha256 = null,
                TotalResultCardinality = canonical.TotalResultCardinality,
                BoundedResultItemCount = boundedItemCount
            };
            return Task.FromResult(Success(phases, terminal));
        });
        var executor = new BrowserBenchmarkOperationExecutor(driver, Options);

        var result = await executor.ExecuteAsync(
            operation, scenario, fixture, BenchmarkProfiles.Quick,
            TimeSpan.FromSeconds(10), CancellationToken.None);

        Assert.AreEqual(ExecutionStatus.Succeeded, result.Execution.Status);
        Assert.IsTrue(Encoding.UTF8.GetByteCount(capturedPayload.GetRawText()) <= 786_432);
        Assert.IsTrue(capturedPayload.GetProperty("title").GetString()!.Length <= 512);
        Assert.AreEqual(
            Math.Min(canonical.Items.Count, OperationResultEnvelope.MaximumRetainedItems),
            capturedPayload.GetProperty("items").GetArrayLength());
        var boundedDeepItem = capturedPayload.GetProperty("items")[deepItem.Index];
        Assert.IsTrue(boundedDeepItem.GetProperty("nodeIds").GetArrayLength() <= 128);
        Assert.IsTrue(boundedDeepItem.GetProperty("_browserProjection")
            .GetProperty("omissionCount").GetInt32() > 0);
        var boundedPaths = capturedPayload.GetProperty("orderedPaths").EnumerateArray().ToArray();
        Assert.IsTrue(boundedPaths.All(path => path.GetProperty("nodeIds").GetArrayLength() <= 128));
        for (var index = 0; index < boundedPaths.Length; index++)
        {
            Assert.AreEqual(
                canonical.OrderedPaths[index].NodeIds.Count -
                boundedPaths[index].GetProperty("nodeIds").GetArrayLength(),
                boundedPaths[index].GetProperty("omittedNodeIdCount").GetInt32());
        }
        Assert.AreEqual(canonical.ResultDigest, result.Outputs.Single().ResultDigest);
        Assert.AreEqual(
            canonical.Items[0].GetRawText(),
            result.Outputs.Single().Items[0].GetRawText());
        Assert.AreEqual(
            canonical.OrderedPaths.Max(path => path.NodeIds.Count),
            result.Outputs.Single().OrderedPaths.Max(path => path.NodeIds.Count));
    }

    [TestMethod]
    public async Task UnsafeLargeFullExpansion_IsStructuredSkipAndNeverStartsDriver()
    {
        var fixture = DeterministicStressGraphFixtureFactory.Create(StressGraphSeedIds.Balanced10K);
        var scenario = new BenchmarkScenarioDefinition(
            "test.browser.full-expansion.balanced-10k",
            "Unsafe test-only large expansion.",
            BenchmarkProfiles.QuickKey,
            OperationKeys.GraphFetch,
            StressGraphSeedIds.Balanced10K,
            JsonSerializer.SerializeToElement(new { }),
            null,
            false,
            executionTarget: BenchmarkScenarioExecutionTarget.Browser,
            browserJourney: new BrowserJourneyDefinition(BrowserJourneyActions.FullExpansion));
        var operation = BenchmarkOperationRequestFactory.Create(
            scenario, fixture, scenario.Parameters, Guid.NewGuid(), Guid.NewGuid());
        var driver = new StubDriver((_, _, _) => throw new AssertFailedException("Driver must not start."));
        var executor = new BrowserBenchmarkOperationExecutor(driver, Options);

        var result = await executor.ExecuteAsync(
            operation, scenario, fixture, BenchmarkProfiles.Quick,
            TimeSpan.FromSeconds(2), CancellationToken.None);

        Assert.AreEqual(ExecutionStatus.Skipped, result.Execution.Status);
        Assert.AreEqual("browser-full-expansion-small-only", result.Execution.Failure?.Code);
        Assert.AreEqual(0, driver.RunCount);
    }

    [TestMethod]
    public async Task Router_PreparesGraphBrowserManifestWithActualDatabaseIdentity()
    {
        var (operation, scenario, fixture) = Case("quick.browser.collapsed.balanced-1k");
        var graphIdentity = new GraphRunIdentity(
            fixture.Specification.Slug,
            fixture.Specification.GraphId.ToString(),
            fixture.Specification.Shape,
            fixture.NodeCount,
            fixture.EdgeCount,
            fixture.Specification.MaximumDepth);
        var datasetIdentity = new DatasetRunIdentity(
            "postgresql-stress-seed-v1",
            "postgresql-corpus",
            Sha('1'),
            Sha('2'),
            Sha('3'),
            Sha('4'));
        var rest = new PreparationExecutor(graphIdentity, datasetIdentity);
        var browser = new BrowserBenchmarkOperationExecutor(
            new StubDriver((_, _, _) => throw new AssertFailedException()),
            Options);
        var router = new BenchmarkOperationExecutorRouter(
            new BenchmarkOperationExecutor(),
            rest,
            browserExecutor: browser);

        var prepared = await router.PrepareAsync(
            operation, scenario, fixture, TimeSpan.FromSeconds(2), CancellationToken.None);

        Assert.AreEqual(ExecutionStatus.Succeeded, prepared.Execution.Status);
        Assert.AreEqual("postgresql-stress-seed-v1", prepared.DatasetIdentity?.GeneratorVersion);
        Assert.AreEqual(fixture.NodeCount, prepared.GraphIdentity?.ActualNodeCount);
        Assert.AreEqual("0.2.0", prepared.Dependencies?.GraphMap);
        Assert.AreEqual("16.4", prepared.Dependencies?.PostgreSql);
        Assert.AreEqual("http", prepared.Dependencies?.RelevantDependencies["browser-api-scheme"]);
        Assert.AreEqual("cross-origin",
            prepared.Dependencies?.RelevantDependencies["browser-api-origin-topology"]);
        Assert.AreEqual(BrowserBenchmarkOperationExecutor.ExpectedHarnessBuildIdentity,
            prepared.Dependencies?.RelevantDependencies["browser-harness-build"]);
        StringAssert.Contains(prepared.EnvironmentProfile, "api-loopback-http");
        StringAssert.Contains(prepared.EnvironmentProfile, "cross-origin");
        Assert.AreEqual(1, rest.PrepareCount);
    }

    [TestMethod]
    public async Task Router_ResultRenderRetainsInMemoryIdentityAndMarksDatabaseUnused()
    {
        var (operation, scenario, fixture) = Case("quick.browser.result-render.strongest.balanced-1k");
        var rest = new PreparationExecutor(
            new GraphRunIdentity("wrong-db", "99", "db", 1, 0, 0),
            new DatasetRunIdentity("wrong-db", "wrong", Sha('1'), Sha('2'), Sha('3'), Sha('4')));
        var browser = new BrowserBenchmarkOperationExecutor(
            new StubDriver((_, _, _) => throw new AssertFailedException()),
            Options);
        var router = new BenchmarkOperationExecutorRouter(
            new BenchmarkOperationExecutor(),
            rest,
            browserExecutor: browser);

        var prepared = await router.PrepareAsync(
            operation, scenario, fixture, TimeSpan.FromSeconds(2), CancellationToken.None);

        Assert.AreEqual(ExecutionStatus.Succeeded, prepared.Execution.Status);
        Assert.IsNull(prepared.GraphIdentity);
        Assert.IsNull(prepared.DatasetIdentity);
        Assert.AreEqual("not-used", prepared.Dependencies?.PostgreSql);
        Assert.AreEqual("not-used", prepared.Dependencies?.RelevantDependencies["api-host-class"]);
        Assert.AreEqual("not-used", prepared.Dependencies?.RelevantDependencies["browser-api-scheme"]);
        Assert.AreEqual(0, rest.PrepareCount);
    }

    [TestMethod]
    public async Task BrowserPreparation_RequiresSeparateBrowserApiOnlyForGraphJourneys()
    {
        var driver = new StubDriver((_, _, _) => throw new AssertFailedException());
        var executor = new BrowserBenchmarkOperationExecutor(
            driver,
            new BrowserJourneyOptions(
                new Uri("http://127.0.0.1:6006/iframe.html"),
                null,
                "not-used"));
        var graphCase = Case("quick.browser.collapsed.balanced-1k");
        var resultCase = Case("quick.browser.result-render.strongest.balanced-1k");

        var graphPreparation = await executor.PrepareAsync(
            graphCase.Operation,
            graphCase.Scenario,
            graphCase.Fixture,
            TimeSpan.FromSeconds(2),
            CancellationToken.None);
        var resultPreparation = await executor.PrepareAsync(
            resultCase.Operation,
            resultCase.Scenario,
            resultCase.Fixture,
            TimeSpan.FromSeconds(2),
            CancellationToken.None);

        Assert.AreEqual(ExecutionStatus.Failed, graphPreparation.Execution.Status);
        Assert.AreEqual("browser-api-base-url-required", graphPreparation.Execution.Failure?.Code);
        Assert.AreEqual(ExecutionStatus.Succeeded, resultPreparation.Execution.Status);
        Assert.AreEqual("not-used", resultPreparation.Dependencies?.PostgreSql);
        Assert.AreEqual("not-used", resultPreparation.Dependencies?.RelevantDependencies["api-host-class"]);
        Assert.AreEqual(1, driver.ProbeCount);
    }

    [TestMethod]
    public async Task BrowserPreparation_DistinguishesSameOriginFromCorsPreflightTopology()
    {
        var (operation, scenario, fixture) = Case("quick.browser.collapsed.balanced-1k");
        var sameOrigin = new BrowserBenchmarkOperationExecutor(
            new StubDriver((_, _, _) => throw new AssertFailedException()),
            new BrowserJourneyOptions(
                new Uri("https://benchmark.example/iframe.html"),
                new Uri("https://benchmark.example/api/"),
                "16.4"));
        var crossOrigin = new BrowserBenchmarkOperationExecutor(
            new StubDriver((_, _, _) => throw new AssertFailedException()),
            new BrowserJourneyOptions(
                new Uri("https://benchmark.example:6006/iframe.html"),
                new Uri("https://benchmark.example:5080/api/"),
                "16.4"));

        var same = await sameOrigin.PrepareAsync(
            operation, scenario, fixture, TimeSpan.FromSeconds(2), CancellationToken.None);
        var cross = await crossOrigin.PrepareAsync(
            operation, scenario, fixture, TimeSpan.FromSeconds(2), CancellationToken.None);

        Assert.AreEqual("same-origin",
            same.Dependencies?.RelevantDependencies["browser-api-origin-topology"]);
        Assert.AreEqual("cross-origin",
            cross.Dependencies?.RelevantDependencies["browser-api-origin-topology"]);
        Assert.AreEqual("no-cross-origin-cors-preflight",
            same.Dependencies?.RelevantDependencies["browser-fetch-ttfb-semantics"]);
        Assert.AreEqual("correlation-header-cors-preflight-may-be-included",
            cross.Dependencies?.RelevantDependencies["browser-fetch-ttfb-semantics"]);
        Assert.AreNotEqual(same.EnvironmentProfile, cross.EnvironmentProfile);
    }

    [TestMethod]
    public async Task FailedBrowserEvidence_PersistsReloadsAndValidatesThroughVersionedExport()
    {
        var driver = new StubDriver((request, _, _) =>
        {
            var phase = Phase(
                InsightMeasurementLayers.Transport,
                InsightMeasurementPhases.TimeToFirstByte,
                2,
                TimingBoundaryProvenance.DirectlyInstrumented,
                "consumer-fetch-promise");
            var failure = BenchmarkOperationExecutor.Failure(
                ExecutionStatus.Failed,
                FailureKind.Execution,
                "browser-page-error",
                "The page failed after headers.");
            var terminal = new BrowserJourneyTerminalEvidence(
                BrowserBenchmarkOperationExecutor.HarnessVersion,
                request.ScenarioId,
                request.RunId,
                request.SampleId,
                "failed",
                1_000,
                999,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                1_024,
                Sha('0'),
                null,
                null,
                new BrowserJourneyFailure("browser-page-error", "late page error"),
                [],
                ["late page error"],
                [],
                JsonSerializer.SerializeToElement(new { stableSelector = (string?)null }),
                new BrowserJourneyEnvironment("v24", "Chromium 140", "1.60.0", "0.2.0"));
            return Task.FromResult(new BrowserJourneyDriverResult(
                failure,
                [phase],
                terminal,
                terminal.Environment,
                42,
                0,
                false,
                string.Empty));
        });
        var repository = new MemoryBenchmarkRunRepository();
        var runner = new SerialBenchmarkRunner(
            new BrowserBenchmarkOperationExecutor(driver, Options),
            repository);

        var run = (await runner.RunAsync(new BenchmarkRunSelection(
            BenchmarkProfiles.QuickKey,
            ScenarioKey: "quick.browser.collapsed.balanced-1k",
            Persist: true))).Runs.Single();

        Assert.AreEqual(ExecutionStatus.Failed, run.Manifest.Execution.Status);
        Assert.IsTrue(run.WasPersisted);
        Assert.IsTrue(run.WasReloaded);
        Assert.IsTrue(run.Outputs.Single().Summary.GetProperty("browserEvidenceOnly").GetBoolean());
        Assert.AreEqual("consumer-fetch-promise",
            run.Outputs.Single().Distribution.GetProperty("phaseEvidence")[0]
                .GetProperty("source").GetString());
        Assert.AreEqual(run.Export.Digests.OutputsDigest, run.DeserializedExport.Digests.OutputsDigest);
        Assert.AreEqual("browser-page-error", run.DeserializedExport.Manifest.Execution.Failure?.Code);
    }

    [TestMethod]
    [Timeout(10_000)]
    public async Task ActualNodeProcessTimeout_RetainsAlreadyFlushedPhaseFrame()
    {
        const string script = "const r=require('readline').createInterface({input:process.stdin});" +
            "r.once('line',l=>{const q=JSON.parse(l);" +
            "console.log(JSON.stringify({eventKind:'phase',runId:q.runId,sampleId:q.sampleId,sequence:0," +
            "phase:{layer:'transport',phase:'time-to-first-byte',durationMilliseconds:1," +
            "timingBoundaryProvenance:'directly-instrumented',source:'fixture'," +
            "evidence:{startMilliseconds:0,endMilliseconds:1}}}));setInterval(()=>{},1000)});";
        var driver = new PlaywrightBrowserJourneyDriver(
            new FixedCommandProvider(new WorkerProcessCommand("node", ["-e", script])));
        var request = new BrowserJourneyDriverRequest(
            "journey",
            Guid.NewGuid(),
            Guid.NewGuid(),
            "process-timeout",
            OperationKeys.GraphFetch,
            "http://127.0.0.1/",
            "http://127.0.0.1/",
            StressGraphSeedIds.Balanced1K,
            BrowserJourneyActions.Collapsed,
            null,
            1_000,
            null);

        var result = await driver.RunAsync(
            request, TimeSpan.FromSeconds(1), CancellationToken.None);

        Assert.AreEqual(ExecutionStatus.TimedOut, result.Execution.Status);
        Assert.AreEqual(1, result.Phases.Count);
        Assert.AreEqual(InsightMeasurementPhases.TimeToFirstByte, result.Phases[0].Phase);
        Assert.IsTrue(result.ForcedTermination);
    }

    [TestMethod]
    [Timeout(10_000)]
    public async Task ActualNodeProcessCrash_RetainsPhaseFlushedBeforeAbnormalExit()
    {
        const string script = "const r=require('readline').createInterface({input:process.stdin});" +
            "r.once('line',l=>{const q=JSON.parse(l);const f=JSON.stringify({eventKind:'phase'," +
            "runId:q.runId,sampleId:q.sampleId,sequence:0,phase:{layer:'transport'," +
            "phase:'time-to-first-byte',durationMilliseconds:1,timingBoundaryProvenance:" +
            "'directly-instrumented',source:'fixture',evidence:{startMilliseconds:0,endMilliseconds:1}}})+'\\n';" +
            "process.stdout.write(f,()=>process.exit(7))});";
        var driver = NodeDriver(script);
        var request = ProcessRequest();

        var result = await driver.RunAsync(
            request, TimeSpan.FromSeconds(2), CancellationToken.None);

        Assert.AreEqual(ExecutionStatus.Crashed, result.Execution.Status);
        Assert.AreEqual("browser-driver-crashed", result.Execution.Failure?.Code);
        Assert.AreEqual(1, result.Phases.Count);
        Assert.AreEqual(7, result.ExitCode);
    }

    [TestMethod]
    [Timeout(10_000)]
    public async Task ActualNodeProcessSequenceGap_IsRejectedAsProtocolFailure()
    {
        const string script = "const r=require('readline').createInterface({input:process.stdin});" +
            "r.once('line',l=>{const q=JSON.parse(l);console.log(JSON.stringify({eventKind:'phase'," +
            "runId:q.runId,sampleId:q.sampleId,sequence:1,phase:{layer:'transport'," +
            "phase:'time-to-first-byte',durationMilliseconds:1,timingBoundaryProvenance:" +
            "'directly-instrumented',source:'fixture',evidence:{startMilliseconds:0,endMilliseconds:1}}}))});";
        var driver = NodeDriver(script);

        var result = await driver.RunAsync(
            ProcessRequest(), TimeSpan.FromSeconds(2), CancellationToken.None);

        Assert.AreEqual(ExecutionStatus.Failed, result.Execution.Status);
        Assert.AreEqual("browser-driver-protocol-invalid", result.Execution.Failure?.Code);
        Assert.AreEqual(0, result.Phases.Count);
    }

    [TestMethod]
    [Timeout(10_000)]
    public async Task ActualNodeProcessCorrelationMismatch_IsRejectedAsProtocolFailure()
    {
        const string script = "const r=require('readline').createInterface({input:process.stdin});" +
            "r.once('line',l=>{const q=JSON.parse(l);console.log(JSON.stringify({eventKind:'phase'," +
            "runId:'00000000-0000-0000-0000-000000000001',sampleId:q.sampleId,sequence:0," +
            "phase:{layer:'transport',phase:'time-to-first-byte',durationMilliseconds:1," +
            "timingBoundaryProvenance:'directly-instrumented',source:'fixture'," +
            "evidence:{startMilliseconds:0,endMilliseconds:1}}}))});";
        var driver = NodeDriver(script);

        var result = await driver.RunAsync(
            ProcessRequest(), TimeSpan.FromSeconds(2), CancellationToken.None);

        Assert.AreEqual(ExecutionStatus.Failed, result.Execution.Status);
        Assert.AreEqual("browser-driver-protocol-invalid", result.Execution.Failure?.Code);
        Assert.AreEqual(0, result.Phases.Count);
    }

    [TestMethod]
    [Timeout(10_000)]
    public async Task ActualNodeProbe_DeserializesFrontendTerminalShapeAndExitsPromptly()
    {
        const string script = "const r=require('readline').createInterface({input:process.stdin});" +
            "r.once('line',l=>{const q=JSON.parse(l);console.log(JSON.stringify({eventKind:'terminal'," +
            "runId:q.runId,sampleId:q.sampleId,sequence:0,terminal:{version:'phase-4-browser-v1'," +
            "scenarioId:q.scenarioId,runId:q.runId,sampleId:q.sampleId,status:'succeeded'," +
            "actualNodeCount:null,actualEdgeCount:null,renderedNodeCount:null,renderedEdgeCount:null," +
            "matchCount:null,requiredAncestorUnionCount:null,requiredAncestorNodeIds:null,matchNodeIds:null," +
            "totalResultCardinality:null,boundedResultItemCount:null,requestBytes:null,responseBytes:null," +
            "responsePayloadSha256:null,identityLimitation:null,driverPayload:null,failure:null," +
            "unexpectedConsoleErrors:[],pageErrors:[],exactSuppressions:[],evidence:null," +
            "environment:{nodeVersion:'v24.0.0',browserVersion:'Chromium 140.0.0'," +
            "playwrightVersion:'1.60.0',graphMapVersion:'0.2.0'}}}))});";
        var driver = NodeDriver(script);
        var started = System.Diagnostics.Stopwatch.StartNew();

        var result = await driver.ProbeAsync(TimeSpan.FromSeconds(2), CancellationToken.None);

        started.Stop();
        Assert.AreEqual(ExecutionStatus.Succeeded, result.Execution.Status);
        Assert.AreEqual("0.2.0", result.Environment?.GraphMapVersion);
        Assert.IsTrue(started.Elapsed < TimeSpan.FromSeconds(2));
        Assert.IsFalse(result.ForcedTermination);
    }

    [TestMethod]
    [Timeout(10_000)]
    public async Task ActualNodeProcess_DescendantHeldStderrPipeUsesBoundedDrainFallback()
    {
        const string script = "const {spawn}=require('child_process');" +
            "const r=require('readline').createInterface({input:process.stdin});" +
            "r.once('line',l=>{const q=JSON.parse(l);process.stderr.write('retained-prefix\\n');" +
            "const c=spawn(process.execPath,['-e','setTimeout(()=>{},3000)']," +
            "{detached:true,stdio:['ignore','ignore',process.stderr]});c.unref();" +
            "const t={eventKind:'terminal',runId:q.runId,sampleId:q.sampleId,sequence:0," +
            "terminal:{version:'phase-4-browser-v1',scenarioId:q.scenarioId,runId:q.runId," +
            "sampleId:q.sampleId,status:'succeeded',actualNodeCount:null,actualEdgeCount:null," +
            "renderedNodeCount:null,renderedEdgeCount:null,matchCount:null," +
            "requiredAncestorUnionCount:null,requiredAncestorNodeIds:null,matchNodeIds:null," +
            "totalResultCardinality:null,boundedResultItemCount:null,requestBytes:null," +
            "responseBytes:null,responsePayloadSha256:null,identityLimitation:null," +
            "driverPayload:null,failure:null,unexpectedConsoleErrors:[],pageErrors:[]," +
            "exactSuppressions:[],evidence:null,environment:{nodeVersion:'v24.0.0'," +
            "browserVersion:'Chromium 140.0.0',playwrightVersion:'1.60.0'," +
            "graphMapVersion:'0.2.0'}}};" +
            "process.stdout.write(JSON.stringify(t)+'\\n',()=>process.exit(0))});";
        var driver = NodeDriver(script);
        var started = System.Diagnostics.Stopwatch.StartNew();

        var result = await driver.ProbeAsync(TimeSpan.FromSeconds(5), CancellationToken.None);

        started.Stop();
        Assert.AreEqual(ExecutionStatus.Succeeded, result.Execution.Status);
        Assert.IsTrue(started.Elapsed < TimeSpan.FromSeconds(2.5));
        StringAssert.Contains(result.StandardError, "retained-prefix");
        StringAssert.Contains(result.StandardError, "stderr drain incomplete after bounded wait");
    }

    private static (
        PreparedBenchmarkOperation Operation,
        BenchmarkScenarioDefinition Scenario,
        DeterministicStressGraphFixture Fixture) Case(string scenarioKey)
    {
        var scenario = BenchmarkScenarioRegistry.Get(scenarioKey);
        var fixture = DeterministicStressGraphFixtureFactory.Create(scenario.DatasetId);
        var operation = BenchmarkOperationRequestFactory.Create(
            scenario,
            fixture,
            scenario.Parameters,
            Guid.NewGuid(),
            Guid.NewGuid());
        return (operation, scenario, fixture);
    }

    private static async Task<BenchmarkOperationExecutionResult>
        ExecuteSearchWithRequiredNodeIdsAsync(IReadOnlyList<string> requiredNodeIds)
    {
        var (operation, scenario, fixture) = Case("quick.browser.search.compact.balanced-1k");
        var phases = GraphPhases().Concat([
            Phase(
                InsightMeasurementLayers.BrowserData,
                InsightMeasurementPhases.SearchCompletion,
                3,
                TimingBoundaryProvenance.ExternallyObserved,
                "playwright-visible-status",
                new { searchStatus = "1 matching node · 4 total shown" })
        ]).ToArray();
        var terminal = Terminal(operation, scenario, fixture) with
        {
            MatchCount = 1,
            RequiredAncestorUnionCount = 4,
            RequiredAncestorNodeIds = requiredNodeIds,
            MatchNodeIds = null,
            TotalResultCardinality = 1
        };
        var executor = new BrowserBenchmarkOperationExecutor(
            new StubDriver((_, _, _) => Task.FromResult(Success(phases, terminal))),
            Options);
        return await executor.ExecuteAsync(
            operation, scenario, fixture, BenchmarkProfiles.Quick,
            TimeSpan.FromSeconds(2), CancellationToken.None);
    }

    private static PlaywrightBrowserJourneyDriver NodeDriver(string script) => new(
        new FixedCommandProvider(new WorkerProcessCommand("node", ["-e", script])));

    private static BrowserJourneyDriverRequest ProcessRequest() => new(
        "journey",
        Guid.NewGuid(),
        Guid.NewGuid(),
        "process-fixture",
        OperationKeys.GraphFetch,
        "http://127.0.0.1/",
        "http://127.0.0.1/",
        StressGraphSeedIds.Balanced1K,
        BrowserJourneyActions.Collapsed,
        null,
        2_000,
        null);

    private static string Sha(char value) => $"sha256:{new string(value, 64)}";

    private static IReadOnlyList<BrowserPhaseObservation> GraphPhases() =>
    [
        Phase(InsightMeasurementLayers.Transport, InsightMeasurementPhases.TimeToFirstByte,
            1, TimingBoundaryProvenance.DirectlyInstrumented, "consumer-fetch-promise"),
        Phase(InsightMeasurementLayers.Transport, InsightMeasurementPhases.FullTransfer,
            2, TimingBoundaryProvenance.DirectlyInstrumented, "consumer-response-array-buffer",
            new { nextHopProtocol = "http/1.1", resourceTimingLimitation = (string?)null }),
        Phase(InsightMeasurementLayers.BrowserData, InsightMeasurementPhases.JsonParse,
            1, TimingBoundaryProvenance.DirectlyInstrumented, "consumer-json-parse"),
        Phase(InsightMeasurementLayers.BrowserData, InsightMeasurementPhases.DomainMapping,
            1, TimingBoundaryProvenance.DirectlyInstrumented, "consumer-domain-mapper"),
        Phase(InsightMeasurementLayers.BrowserData, InsightMeasurementPhases.GraphMapAdapter,
            1, TimingBoundaryProvenance.DirectlyInstrumented, "consumer-adapter-wrapper"),
        Phase(InsightMeasurementLayers.GraphMap, InsightMeasurementPhases.DagreLayout,
            2, TimingBoundaryProvenance.Estimated, "consumer-observed-graphmap-layout"),
        Phase(InsightMeasurementLayers.GraphMap, InsightMeasurementPhases.NodeEdgeMaterialization,
            2, TimingBoundaryProvenance.ExternallyObserved, "playwright-dom-count"),
        Phase(InsightMeasurementLayers.GraphMap, InsightMeasurementPhases.ReactCommit,
            1, TimingBoundaryProvenance.DirectlyInstrumented, "react-profiler"),
        Phase(InsightMeasurementLayers.GraphMap, InsightMeasurementPhases.ViewportFit,
            3, TimingBoundaryProvenance.Estimated, "playwright-stable-transform"),
        Phase(InsightMeasurementLayers.EndToEnd, InsightMeasurementPhases.ActionToStableResultAndView,
            12, TimingBoundaryProvenance.ExternallyObserved,
            "playwright-completion-event-observation")
    ];

    private static BrowserPhaseObservation Phase(
        string layer,
        string phase,
        decimal duration,
        TimingBoundaryProvenance provenance,
        string source,
        object? extraEvidence = null)
    {
        var evidence = extraEvidence is null
            ? JsonSerializer.SerializeToElement(new
            {
                startMilliseconds = 10m,
                endMilliseconds = 10m + duration,
                terminalSelector = "[data-benchmark-state='stable']"
            })
            : MergeEvidence(duration, extraEvidence);
        return new BrowserPhaseObservation(layer, phase, duration, provenance, source, evidence);
    }

    private static JsonElement MergeEvidence(decimal duration, object extra)
    {
        var values = new Dictionary<string, object?>
        {
            ["startMilliseconds"] = 10m,
            ["endMilliseconds"] = 10m + duration
        };
        foreach (var property in JsonSerializer.SerializeToElement(extra).EnumerateObject())
        {
            values[property.Name] = property.Value.Clone();
        }

        return JsonSerializer.SerializeToElement(values);
    }

    private static BrowserJourneyTerminalEvidence Terminal(
        PreparedBenchmarkOperation operation,
        BenchmarkScenarioDefinition scenario,
        DeterministicStressGraphFixture fixture) => new(
            BrowserBenchmarkOperationExecutor.HarnessVersion,
            scenario.Key,
            operation.Request.RunId,
            operation.Request.SampleId,
            "succeeded",
            fixture.NodeCount,
            fixture.EdgeCount,
            1,
            0,
            null,
            null,
            null,
            null,
            1,
            null,
            null,
            12_345,
            $"sha256:{new string('0', 64)}",
            null,
            null,
            null,
            [],
            [],
            [],
            JsonSerializer.SerializeToElement(new
            {
                stableSelector = "[data-benchmark-state='stable']",
                harnessBuildIdentity = BrowserBenchmarkOperationExecutor.ExpectedHarnessBuildIdentity,
                nextHopProtocol = "http/1.1",
                resourceTimingLimitation = (string?)null
            }),
            new BrowserJourneyEnvironment("v24.0.0", "Chromium 140.0.0", "1.60.0", "0.2.0"));

    private static BrowserJourneyDriverResult Success(
        IReadOnlyList<BrowserPhaseObservation> phases,
        BrowserJourneyTerminalEvidence terminal) => new(
            new ExecutionOutcome(ExecutionStatus.Succeeded),
            phases,
            terminal,
            terminal.Environment,
            42,
            0,
            false,
            string.Empty);

    private sealed class StubDriver : IBrowserJourneyDriver
    {
        private readonly Func<BrowserJourneyDriverRequest, TimeSpan, CancellationToken,
            Task<BrowserJourneyDriverResult>> _run;

        public StubDriver(Func<BrowserJourneyDriverRequest, TimeSpan, CancellationToken,
            Task<BrowserJourneyDriverResult>> run) => _run = run;

        public int RunCount { get; private set; }

        public int ProbeCount { get; private set; }

        public Task<BrowserJourneyDriverResult> ProbeAsync(
            TimeSpan timeout,
            CancellationToken cancellationToken)
        {
            ProbeCount++;
            return Task.FromResult(new BrowserJourneyDriverResult(
                new ExecutionOutcome(ExecutionStatus.Succeeded),
                [],
                null,
                new BrowserJourneyEnvironment("v24.0.0", "Chromium 140.0.0", "1.60.0", "0.2.0"),
                42,
                0,
                false,
                string.Empty));
        }

        public Task<BrowserJourneyDriverResult> RunAsync(
            BrowserJourneyDriverRequest request,
            TimeSpan timeout,
            CancellationToken cancellationToken)
        {
            RunCount++;
            return _run(request, timeout, cancellationToken);
        }
    }

    private sealed class FixedCommandProvider : IBrowserJourneyCommandProvider
    {
        private readonly WorkerProcessCommand _command;

        public FixedCommandProvider(WorkerProcessCommand command) => _command = command;

        public WorkerProcessCommand GetCommand() => _command;
    }

    private sealed class PreparationExecutor : IBenchmarkOperationExecutor, IBenchmarkScenarioPreparer
    {
        private readonly GraphRunIdentity _graph;
        private readonly DatasetRunIdentity _dataset;

        public PreparationExecutor(GraphRunIdentity graph, DatasetRunIdentity dataset)
        {
            _graph = graph;
            _dataset = dataset;
        }

        public int PrepareCount { get; private set; }

        public Task<BenchmarkOperationExecutionResult> ExecuteAsync(
            PreparedBenchmarkOperation operation,
            BenchmarkScenarioDefinition scenario,
            DeterministicStressGraphFixture fixture,
            BenchmarkProfileDefinition profile,
            TimeSpan timeout,
            CancellationToken cancellationToken) => throw new AssertFailedException();

        public Task<BenchmarkScenarioPreparationResult> PrepareAsync(
            PreparedBenchmarkOperation operation,
            BenchmarkScenarioDefinition scenario,
            DeterministicStressGraphFixture fixture,
            TimeSpan timeout,
            CancellationToken cancellationToken)
        {
            PrepareCount++;
            return Task.FromResult(new BenchmarkScenarioPreparationResult(
                operation,
                new ExecutionOutcome(ExecutionStatus.Succeeded),
                [],
                _graph,
                _dataset,
                new DependencyVersions(
                    Environment.Version.ToString(),
                    "not-used",
                    "not-used",
                    "not-used",
                    "16.4",
                    new Dictionary<string, string> { ["api-boundary"] = "real-process-network" }),
                "rest-real-process",
                RunnerType.ApiBrowserJourney));
        }
    }

    private sealed class MemoryBenchmarkRunRepository : IBenchmarkRunRepository
    {
        private RunManifest? _manifest;
        private readonly List<RunSample> _samples = [];
        private readonly List<CompactRunOutput> _outputs = [];

        public Task CreateRunAsync(
            ExplicitBenchmarkRunIntent intent,
            RunManifest manifest,
            CancellationToken cancellationToken = default)
        {
            Assert.AreEqual(intent.RunId, manifest.RunId);
            _manifest = manifest;
            return Task.CompletedTask;
        }

        public Task UpdateLifecycleAsync(
            ExplicitBenchmarkRunIntent intent,
            ExecutionOutcome execution,
            DateTimeOffset? completedAt,
            CancellationToken cancellationToken = default)
        {
            Assert.IsNotNull(_manifest);
            Assert.AreEqual(intent.RunId, _manifest.RunId);
            _manifest = _manifest with { Execution = execution, CompletedAt = completedAt };
            return Task.CompletedTask;
        }

        public Task AppendSampleAsync(
            ExplicitBenchmarkRunIntent intent,
            RunSample sample,
            CancellationToken cancellationToken = default)
        {
            Assert.AreEqual(intent.RunId, sample.RunId);
            _samples.Add(sample);
            return Task.CompletedTask;
        }

        public Task AppendOutputAsync(
            ExplicitBenchmarkRunIntent intent,
            CompactRunOutput output,
            CancellationToken cancellationToken = default)
        {
            Assert.AreEqual(intent.RunId, output.RunId);
            _outputs.Add(output);
            return Task.CompletedTask;
        }

        public Task<BenchmarkRunSnapshot?> GetSnapshotAsync(
            Guid runId,
            CancellationToken cancellationToken = default)
        {
            Assert.IsNotNull(_manifest);
            Assert.AreEqual(runId, _manifest.RunId);
            return Task.FromResult<BenchmarkRunSnapshot?>(new BenchmarkRunSnapshot(
                _manifest,
                _samples.ToArray(),
                _outputs.ToArray()));
        }
    }
}
