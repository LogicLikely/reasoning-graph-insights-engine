using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Backend.Insights.Analysis;
using Backend.Insights.Benchmarking;
using Backend.Insights.Contracts;
using Backend.Insights.Measurement;
using Backend.Seeding;

namespace backend.Tests.Insights.Benchmarking;

[TestClass]
public sealed class RestApiBenchmarkOperationExecutorTests
{
    private static readonly Uri BaseAddress = new("http://127.0.0.1:43127/");

    [TestMethod]
    public async Task GraphFetch_PreservesCorrelationHeaderTrailerTransferCountsAndFingerprint()
    {
        HttpRequestMessage? observedRequest = null;
        var body = GraphBody(StressGraphSeedIds.Balanced1K, 1_000, 999);
        using var client = Client((request, _) =>
        {
            observedRequest = request;
            return Task.FromResult(CorrelatedResponse(
                request,
                body,
                "postgresql-repository.graph-lookup;dur=1.25, " +
                "postgresql-repository.node-query;dur=2.5, " +
                "postgresql-repository.edge-query;dur=0.75",
                "backend-service-api.serialization;dur=3.125"));
        });
        var (operation, scenario, fixture, profile) = FetchCase();
        var executor = new RestApiBenchmarkOperationExecutor(
            client,
            new RestApiJourneyOptions(BaseAddress, RestApiJourneyBoundary.InMemoryTestDouble));

        var result = await executor.ExecuteAsync(
            operation, scenario, fixture, profile, TimeSpan.FromSeconds(2), CancellationToken.None);

        Assert.AreEqual(ExecutionStatus.Succeeded, result.Execution.Status);
        Assert.IsNotNull(observedRequest);
        Assert.AreEqual(HttpVersion.Version20, observedRequest.Version);
        Assert.AreEqual(HttpVersionPolicy.RequestVersionExact, observedRequest.VersionPolicy);
        Assert.AreEqual(
            operation.Request.RunId.ToString("D"),
            observedRequest.Headers.GetValues(InsightCorrelationHeaders.RunId).Single());
        Assert.AreEqual(
            operation.Request.SampleId.ToString("D"),
            observedRequest.Headers.GetValues(InsightCorrelationHeaders.SampleId).Single());
        CollectionAssert.IsSubsetOf(
            new[]
            {
                InsightMeasurementPhases.GraphLookup,
                InsightMeasurementPhases.NodeQuery,
                InsightMeasurementPhases.EdgeQuery,
                InsightMeasurementPhases.Serialization,
                InsightMeasurementPhases.TimeToFirstByte,
                InsightMeasurementPhases.ResponseBytes,
                InsightMeasurementPhases.FullTransfer,
                InsightMeasurementPhases.OperationExecution
            },
            result.Samples.Select(sample => sample.Phase).ToArray());
        Assert.IsTrue(result.Samples
            .Where(sample => sample.Layer == InsightMeasurementLayers.Transport)
            .All(sample => sample.TimingBoundaryProvenance == TimingBoundaryProvenance.Estimated),
            "An in-memory handler must never be labeled as an externally observed network boundary.");
        Assert.IsTrue(result.Samples
            .Where(sample => sample.Phase is InsightMeasurementPhases.GraphLookup or
                InsightMeasurementPhases.NodeQuery or
                InsightMeasurementPhases.EdgeQuery or
                InsightMeasurementPhases.Serialization)
            .All(sample => sample.TimingBoundaryProvenance == TimingBoundaryProvenance.DirectlyInstrumented));
        Assert.IsTrue(result.Samples.All(sample =>
            sample.RunId == operation.Request.RunId && sample.SampleId == operation.Request.SampleId));

        var responseBytes = Encoding.UTF8.GetByteCount(body);
        Assert.IsTrue(result.Samples.Any(sample =>
            sample.Phase == InsightMeasurementPhases.ResponseBytes &&
            sample.Transport.ResponseBytes == responseBytes));
        Assert.IsTrue(result.Samples.Any(sample =>
            sample.NodeCounts.Canonical == 1_000 &&
            sample.EdgeCounts.Requested == 999));
        var output = result.Outputs.Single();
        Assert.AreEqual(1_000, output.Summary.GetProperty("actualNodeCount").GetInt64());
        Assert.AreEqual(999, output.Summary.GetProperty("actualEdgeCount").GetInt64());
        StringAssert.StartsWith(
            output.Summary.GetProperty("observedDatasetFingerprint").GetString(),
            "sha256:");
        Assert.AreEqual(CanonicalJson.ComputeSha256(output.Items), output.ResultDigest);
        Assert.AreEqual(1, output.Distribution.GetProperty("trailerTimingCount").GetInt32());
    }

    [TestMethod]
    public async Task GraphCatalog_InstallsAndValidatesEveryCanonicalStressGraph()
    {
        IReadOnlyList<string>? installedIds = null;
        using var client = Client(async (request, cancellationToken) =>
        {
            if (request.Method == HttpMethod.Post)
            {
                using var document = JsonDocument.Parse(
                    await request.Content!.ReadAsStringAsync(cancellationToken));
                installedIds = document.RootElement.GetProperty("stressGraphIds")
                    .EnumerateArray().Select(item => item.GetString()!).ToArray();
                return CorrelatedResponse(request, string.Empty);
            }

            return CorrelatedResponse(
                request,
                CatalogBody(),
                "postgresql-repository.catalog-aggregation;dur=5, " +
                "backend-service-api.dto-mapping;dur=1",
                "backend-service-api.serialization;dur=2");
        });
        var rest = new RestApiBenchmarkOperationExecutor(
            client,
            new RestApiJourneyOptions(BaseAddress, RestApiJourneyBoundary.InMemoryTestDouble));
        var router = new BenchmarkOperationExecutorRouter(
            new BenchmarkOperationExecutor(),
            rest,
            new RestBenchmarkDatasetInstaller(client, BaseAddress));
        var runner = new SerialBenchmarkRunner(router);

        var result = (await runner.RunAsync(new BenchmarkRunSelection(
            BenchmarkProfiles.QuickKey,
            ScenarioKey: "quick.graph-catalog.rest",
            Timeout: TimeSpan.FromSeconds(2)))).Runs.Single();

        Assert.AreEqual(ExecutionStatus.Succeeded, result.Manifest.Execution.Status);
        CollectionAssert.AreEqual(
            StressGraphSeedCatalog.All.Select(specification => specification.Id).ToArray(),
            installedIds!.ToArray());
        Assert.AreEqual(StressGraphSeedCatalog.All.Count,
            result.Outputs.Single().Summary.GetProperty("canonicalStressGraphCount").GetInt32());
        Assert.AreEqual(
            StressGraphSeedCatalog.All.Sum(specification => (long)specification.NodeCount),
            result.Outputs.Single().Summary.GetProperty("actualNodeCount").GetInt64());
        Assert.IsTrue(result.Samples.Any(sample =>
            sample.Classification.IterationKind == IterationClassificationTokens.Setup));
        Assert.IsTrue(result.Samples.Any(sample =>
            sample.Phase == InsightMeasurementPhases.CatalogAggregation));
        Assert.IsTrue(result.Samples.Any(sample =>
            sample.Phase == InsightMeasurementPhases.Serialization));
        Assert.AreEqual("canonical-stress-catalog", result.Manifest.Graph.Slug);
        Assert.AreEqual("catalog-aggregate", result.Manifest.Graph.Shape);
        Assert.AreEqual("public-domain-stress-corpus-v1", result.Manifest.Dataset.CorpusId);
    }

    [TestMethod]
    public async Task TransferFailure_PreservesHeaderAndPartialByteEvidenceWithoutClaimingFullTransfer()
    {
        using var client = Client((request, _) =>
        {
            var response = CorrelatedResponse(
                request,
                "ignored",
                "postgresql-repository.graph-lookup;dur=1.5");
            response.Content = new StreamContent(new ThrowingAfterFirstReadStream(
                Encoding.UTF8.GetBytes("{\"slug\":\"partial")));
            return Task.FromResult(response);
        });
        var (operation, scenario, fixture, profile) = FetchCase();
        var executor = new RestApiBenchmarkOperationExecutor(
            client,
            new RestApiJourneyOptions(BaseAddress, RestApiJourneyBoundary.RealProcessNetwork));

        var result = await executor.ExecuteAsync(
            operation, scenario, fixture, profile, TimeSpan.FromSeconds(2), CancellationToken.None);

        Assert.AreEqual(ExecutionStatus.Failed, result.Execution.Status);
        Assert.AreEqual("rest-api-journey-failed", result.Execution.Failure?.Code);
        Assert.AreEqual(0, result.Outputs.Count);
        Assert.IsTrue(result.Samples.Any(sample =>
            sample.Phase == InsightMeasurementPhases.GraphLookup &&
            sample.WallClockDuration == 1.5m));
        Assert.IsTrue(result.Samples.Any(sample =>
            sample.Phase == InsightMeasurementPhases.TimeToFirstByte &&
            sample.TimingBoundaryProvenance == TimingBoundaryProvenance.ExternallyObserved));
        Assert.IsFalse(result.Samples.Any(sample => sample.Phase == InsightMeasurementPhases.FullTransfer));
        var bytes = result.Samples.Single(sample =>
            sample.Phase == InsightMeasurementPhases.ResponseBytes).Transport.ResponseBytes;
        Assert.IsTrue(bytes > 0);
    }

    [TestMethod]
    public async Task TransferTimeout_PreservesCompletedHeadersAndPartialBytesAsTimedOut()
    {
        using var client = Client((request, _) =>
        {
            var response = CorrelatedResponse(
                request,
                "ignored",
                "postgresql-repository.node-query;dur=4.25");
            response.Content = new StreamContent(new BlockingAfterFirstReadStream(
                Encoding.UTF8.GetBytes("{\"nodes\":[")));
            return Task.FromResult(response);
        });
        var (operation, scenario, fixture, profile) = FetchCase();
        var executor = new RestApiBenchmarkOperationExecutor(
            client,
            new RestApiJourneyOptions(BaseAddress, RestApiJourneyBoundary.RealProcessNetwork));

        var result = await executor.ExecuteAsync(
            operation, scenario, fixture, profile, TimeSpan.FromMilliseconds(50), CancellationToken.None);

        Assert.AreEqual(ExecutionStatus.TimedOut, result.Execution.Status);
        Assert.AreEqual(FailureKind.Timeout, result.Execution.Failure?.Kind);
        Assert.IsTrue(result.Samples.Any(sample =>
            sample.Phase == InsightMeasurementPhases.NodeQuery &&
            sample.WallClockDuration == 4.25m));
        Assert.IsTrue(result.Samples.Single(sample =>
            sample.Phase == InsightMeasurementPhases.ResponseBytes).Transport.ResponseBytes > 0);
        Assert.IsFalse(result.Samples.Any(sample => sample.Phase == InsightMeasurementPhases.FullTransfer));
    }

    [TestMethod]
    public async Task CorrelationMismatch_PreservesCompletedServerAndTransferEvidence()
    {
        var body = GraphBody(StressGraphSeedIds.Balanced1K, 1_000, 999);
        using var client = Client((request, _) =>
        {
            var response = CorrelatedResponse(
                request,
                body,
                "postgresql-repository.graph-lookup;dur=2");
            response.Headers.Remove(InsightCorrelationHeaders.SampleId);
            response.Headers.TryAddWithoutValidation(
                InsightCorrelationHeaders.SampleId,
                Guid.NewGuid().ToString("D"));
            return Task.FromResult(response);
        });
        var (operation, scenario, fixture, profile) = FetchCase();
        var executor = new RestApiBenchmarkOperationExecutor(
            client,
            new RestApiJourneyOptions(BaseAddress, RestApiJourneyBoundary.RealProcessNetwork));

        var result = await executor.ExecuteAsync(
            operation, scenario, fixture, profile, TimeSpan.FromSeconds(2), CancellationToken.None);

        Assert.AreEqual(ExecutionStatus.Failed, result.Execution.Status);
        Assert.AreEqual("rest-api-correlation-mismatch", result.Execution.Failure?.Code);
        Assert.IsTrue(result.Samples.Any(sample => sample.Phase == InsightMeasurementPhases.GraphLookup));
        Assert.IsTrue(result.Samples.Any(sample => sample.Phase == InsightMeasurementPhases.FullTransfer));
        Assert.AreEqual(0, result.Outputs.Count);
    }

    [TestMethod]
    public async Task Router_RecordsDatasetResetAsSetupAndSerialRunnerExportsMeasuredFetchSeparately()
    {
        var requestOrder = new List<string>();
        var body = GraphBody(StressGraphSeedIds.Balanced1K, 1_000, 999);
        using var client = Client(async (request, cancellationToken) =>
        {
            requestOrder.Add($"{request.Method}:{request.RequestUri!.AbsolutePath}");
            if (request.Method == HttpMethod.Post)
            {
                var resetBody = await request.Content!.ReadAsStringAsync(cancellationToken);
                using var resetDocument = JsonDocument.Parse(resetBody);
                CollectionAssert.AreEqual(
                    new[] { StressGraphSeedIds.Balanced1K },
                    resetDocument.RootElement.GetProperty("stressGraphIds")
                        .EnumerateArray().Select(item => item.GetString()).ToArray());
                return CorrelatedResponse(request, string.Empty);
            }

            return CorrelatedResponse(
                request,
                body,
                "postgresql-repository.graph-lookup;dur=1, " +
                "postgresql-repository.node-query;dur=2, " +
                "postgresql-repository.edge-query;dur=1",
                "backend-service-api.serialization;dur=2");
        });
        var rest = new RestApiBenchmarkOperationExecutor(
            client,
            new RestApiJourneyOptions(BaseAddress, RestApiJourneyBoundary.InMemoryTestDouble));
        var router = new BenchmarkOperationExecutorRouter(
            new BenchmarkOperationExecutor(),
            rest,
            new RestBenchmarkDatasetInstaller(client, BaseAddress));
        var runner = new SerialBenchmarkRunner(router);

        var run = (await runner.RunAsync(new BenchmarkRunSelection(
            BenchmarkProfiles.QuickKey,
            ScenarioKey: "quick.graph-fetch.balanced-1k.rest"))).Runs.Single();

        CollectionAssert.AreEqual(
            new[]
            {
                "POST:/api/graphs/reset",
                "GET:/api/graphs/stress-balanced-1k",
                "GET:/api/graphs/stress-balanced-1k"
            },
            requestOrder);
        Assert.AreEqual(ExecutionStatus.Succeeded, run.Manifest.Execution.Status);
        var setupSamples = run.Samples.Where(sample =>
            sample.Classification.IterationKind == IterationClassificationTokens.Setup).ToArray();
        Assert.IsTrue(setupSamples.Length >= 2);
        Assert.IsTrue(setupSamples.Any(sample =>
            sample.SampleId == run.Outputs.Single().SampleId &&
            sample.Phase == InsightMeasurementPhases.FixtureConstruction &&
            sample.Transport.RequestBytes > 0));
        Assert.IsTrue(run.Samples.Any(sample =>
            sample.Classification.IterationKind == IterationClassificationTokens.Measured &&
            sample.Phase == InsightMeasurementPhases.FullTransfer));
        Assert.AreEqual(run.Export.Digests.SamplesDigest, run.DeserializedExport.Digests.SamplesDigest);
        Assert.AreEqual(run.Export.Digests.OutputsDigest, run.DeserializedExport.Digests.OutputsDigest);
    }

    [DataTestMethod]
    [DataRow(
        "quick.evidence.wide-1k.rest.database-loaded",
        "quick.evidence.wide-1k.rest.supplied-graph",
        "evidence")]
    [DataRow(
        "quick.robustness.balanced-1k.rest.database-loaded",
        "quick.robustness.balanced-1k.rest.supplied-graph",
        "robustness")]
    public async Task DatabaseLoadedAndSuppliedGraphJourneys_PreserveCanonicalAndObservedParity(
        string databaseScenarioKey,
        string suppliedScenarioKey,
        string responseKind)
    {
        var databaseScenario = BenchmarkScenarioRegistry.Get(databaseScenarioKey);
        var fixture = DeterministicStressGraphFixtureFactory.Create(databaseScenario.DatasetId);
        var graph = fixture.CreateGraph();
        var prepared = BenchmarkOperationRequestFactory.Create(
            databaseScenario,
            fixture,
            databaseScenario.Parameters,
            Guid.NewGuid(),
            Guid.NewGuid());
        var graphJson = JsonSerializer.Serialize(
            graph,
            CanonicalJson.CreateSerializerOptions());
        var responseJson = responseKind == "evidence"
            ? EvidenceResponse(graph, prepared.TargetNodeId!)
            : RobustnessResponse(graph);
        var measuredRequestBytes = new List<long>();
        using var client = Client(async (request, cancellationToken) =>
        {
            if (request.Method == HttpMethod.Post && request.RequestUri!.AbsolutePath.EndsWith(
                    "/reset", StringComparison.Ordinal))
            {
                return CorrelatedResponse(request, string.Empty);
            }

            if (request.Method == HttpMethod.Get)
            {
                return CorrelatedResponse(request, graphJson);
            }

            measuredRequestBytes.Add(request.Content is null
                ? 0
                : Encoding.UTF8.GetByteCount(await request.Content.ReadAsStringAsync(cancellationToken)));
            return CorrelatedResponse(
                request,
                responseJson,
                "backend-service-api.algorithm;dur=1, backend-service-api.result-shaping;dur=0.5",
                "backend-service-api.serialization;dur=0.25");
        });
        var rest = new RestApiBenchmarkOperationExecutor(
            client,
            new RestApiJourneyOptions(BaseAddress, RestApiJourneyBoundary.InMemoryTestDouble));
        var runner = new SerialBenchmarkRunner(new BenchmarkOperationExecutorRouter(
            new BenchmarkOperationExecutor(),
            rest,
            new RestBenchmarkDatasetInstaller(client, BaseAddress)));

        var database = (await runner.RunAsync(new BenchmarkRunSelection(
            BenchmarkProfiles.QuickKey,
            ScenarioKey: databaseScenarioKey,
            Timeout: TimeSpan.FromSeconds(10)))).Runs.Single();
        var supplied = (await runner.RunAsync(new BenchmarkRunSelection(
            BenchmarkProfiles.QuickKey,
            ScenarioKey: suppliedScenarioKey,
            Timeout: TimeSpan.FromSeconds(10)))).Runs.Single();

        Assert.AreEqual(
            ExecutionStatus.Succeeded,
            database.Manifest.Execution.Status,
            JsonSerializer.Serialize(database.Manifest.Execution));
        Assert.AreEqual(
            ExecutionStatus.Succeeded,
            supplied.Manifest.Execution.Status,
            JsonSerializer.Serialize(supplied.Manifest.Execution));
        Assert.AreEqual(database.Manifest.Dataset.DatasetInputFingerprint,
            supplied.Manifest.Dataset.DatasetInputFingerprint);
        Assert.AreEqual(database.Outputs.Single().ResultDigest,
            supplied.Outputs.Single().ResultDigest,
            "Parity must use the frozen canonical rich-result digest.");
        Assert.AreEqual(
            database.Outputs.Single().Summary.GetProperty("observedApi")
                .GetProperty("responseFingerprint").GetString(),
            supplied.Outputs.Single().Summary.GetProperty("observedApi")
                .GetProperty("responseFingerprint").GetString(),
            "The independently observed legacy REST payloads must also match.");
        Assert.AreEqual(
            database.Outputs.Single().TotalResultCardinality,
            database.Outputs.Single().Summary.GetProperty("observedApi")
                .GetProperty("responseCardinality").GetInt64(),
            "The observed legacy payload must account for the complete canonical result.");
        CollectionAssert.AreEqual(new long[] { 0, measuredRequestBytes[1] }, measuredRequestBytes);
        Assert.IsTrue(measuredRequestBytes[1] > 0);
        Assert.AreEqual(0, database.Samples.Single(sample =>
            sample.Phase == InsightMeasurementPhases.FullTransfer).Transport.RequestBytes);
        Assert.IsTrue(supplied.Samples.Single(sample =>
            sample.Phase == InsightMeasurementPhases.FullTransfer).Transport.RequestBytes > 0);
    }

    [TestMethod]
    public async Task EvidenceJourney_AcceptsLegacyFullPrecisionThatNormalizesToCanonicalDigestValue()
    {
        var scenario = BenchmarkScenarioRegistry.Get(
            "quick.evidence.wide-1k.rest.database-loaded");
        var fixture = DeterministicStressGraphFixtureFactory.Create(scenario.DatasetId);
        var graph = fixture.CreateGraph();
        var prepared = BenchmarkOperationRequestFactory.Create(
            scenario,
            fixture,
            scenario.Parameters,
            Guid.NewGuid(),
            Guid.NewGuid());
        var graphJson = JsonSerializer.Serialize(
            graph,
            CanonicalJson.CreateSerializerOptions());
        var responseJson = EvidenceResponseWithFirstLogLrDelta(
            graph,
            prepared.TargetNodeId!,
            0.000000000000083423m);
        using var client = Client((request, _) => Task.FromResult(CorrelatedResponse(
            request,
            request.Method == HttpMethod.Get ? graphJson : responseJson,
            request.Method == HttpMethod.Post
                ? "backend-service-api.algorithm;dur=1"
                : null)));
        var runner = new SerialBenchmarkRunner(new BenchmarkOperationExecutorRouter(
            new BenchmarkOperationExecutor(),
            new RestApiBenchmarkOperationExecutor(
                client,
                new RestApiJourneyOptions(BaseAddress, RestApiJourneyBoundary.InMemoryTestDouble))));

        var run = (await runner.RunAsync(new BenchmarkRunSelection(
            BenchmarkProfiles.QuickKey,
            ScenarioKey: scenario.Key,
            Timeout: TimeSpan.FromSeconds(10)))).Runs.Single();

        Assert.AreEqual(
            ExecutionStatus.Succeeded,
            run.Manifest.Execution.Status,
            JsonSerializer.Serialize(run.Manifest.Execution));
        Assert.AreEqual(
            run.Outputs.Single().TotalResultCardinality,
            run.Outputs.Single().Summary.GetProperty("observedApi")
                .GetProperty("responseCardinality").GetInt64());
    }

    [DataTestMethod]
    [DataRow("quick.evidence.wide-1k.rest.database-loaded", "{\"supportingEvidence\":[],\"counterEvidence\":[]}")]
    [DataRow("quick.robustness.balanced-1k.rest.database-loaded", "[]")]
    public async Task AnalysisJourney_RejectsLegacyPayloadThatDoesNotMatchCanonicalResult(
        string scenarioKey,
        string invalidResponse)
    {
        var scenario = BenchmarkScenarioRegistry.Get(scenarioKey);
        var fixture = DeterministicStressGraphFixtureFactory.Create(scenario.DatasetId);
        var graphJson = JsonSerializer.Serialize(
            fixture.CreateGraph(),
            CanonicalJson.CreateSerializerOptions());
        using var client = Client((request, _) => Task.FromResult(CorrelatedResponse(
            request,
            request.Method == HttpMethod.Get ? graphJson : invalidResponse,
            request.Method == HttpMethod.Post
                ? "backend-service-api.algorithm;dur=1"
                : null)));
        var runner = new SerialBenchmarkRunner(new BenchmarkOperationExecutorRouter(
            new BenchmarkOperationExecutor(),
            new RestApiBenchmarkOperationExecutor(
                client,
                new RestApiJourneyOptions(BaseAddress, RestApiJourneyBoundary.InMemoryTestDouble))));

        var run = (await runner.RunAsync(new BenchmarkRunSelection(
            BenchmarkProfiles.QuickKey,
            ScenarioKey: scenarioKey,
            Timeout: TimeSpan.FromSeconds(10)))).Runs.Single();

        Assert.AreEqual(ExecutionStatus.Failed, run.Manifest.Execution.Status);
        Assert.AreEqual(FailureKind.Validation, run.Manifest.Execution.Failure?.Kind);
        Assert.IsTrue(run.Manifest.Execution.Failure!.ValidationFailures.Any(failure =>
            failure.Code.EndsWith("cardinality-mismatch", StringComparison.Ordinal)));
        Assert.AreEqual(0, run.Outputs.Count);
    }

    [TestMethod]
    public async Task Router_WithoutApiEndpointReturnsStructuredPortableFailure()
    {
        var router = new BenchmarkOperationExecutorRouter(new BenchmarkOperationExecutor());
        var runner = new SerialBenchmarkRunner(router);

        var run = (await runner.RunAsync(new BenchmarkRunSelection(
            BenchmarkProfiles.QuickKey,
            ScenarioKey: "quick.graph-fetch.balanced-1k.rest"))).Runs.Single();

        Assert.AreEqual(ExecutionStatus.Failed, run.Manifest.Execution.Status);
        Assert.AreEqual("rest-api-base-url-required", run.Manifest.Execution.Failure?.Code);
        Assert.IsTrue(run.Samples.Any(sample =>
            sample.Phase == InsightMeasurementPhases.FixtureConstruction));
        Assert.AreEqual(run.Export.Digests.ManifestDigest, run.DeserializedExport.Digests.ManifestDigest);
    }

    private static (
        PreparedBenchmarkOperation Operation,
        BenchmarkScenarioDefinition Scenario,
        DeterministicStressGraphFixture Fixture,
        BenchmarkProfileDefinition Profile) FetchCase()
    {
        var scenario = BenchmarkScenarioRegistry.Get("quick.graph-fetch.balanced-1k.rest");
        var fixture = DeterministicStressGraphFixtureFactory.Create(scenario.DatasetId);
        var operation = BenchmarkOperationRequestFactory.Create(
            scenario,
            fixture,
            scenario.Parameters,
            Guid.NewGuid(),
            Guid.NewGuid());
        return (operation, scenario, fixture, BenchmarkProfiles.Quick);
    }

    private static HttpClient Client(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler) =>
        new(new DelegateHandler(handler))
        {
            Timeout = Timeout.InfiniteTimeSpan
        };

    private static HttpResponseMessage CorrelatedResponse(
        HttpRequestMessage request,
        string body,
        string? headerTiming = null,
        string? trailerTiming = null)
    {
        var response = new HttpResponseMessage(
            request.Method == HttpMethod.Post ? HttpStatusCode.NoContent : HttpStatusCode.OK)
        {
            Version = HttpVersion.Version20,
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
        response.Headers.TryAddWithoutValidation(
            InsightCorrelationHeaders.RunId,
            request.Headers.GetValues(InsightCorrelationHeaders.RunId));
        response.Headers.TryAddWithoutValidation(
            InsightCorrelationHeaders.SampleId,
            request.Headers.GetValues(InsightCorrelationHeaders.SampleId));
        if (headerTiming is not null)
        {
            response.Headers.TryAddWithoutValidation("Server-Timing", headerTiming);
        }

        if (trailerTiming is not null)
        {
            response.TrailingHeaders.TryAddWithoutValidation("Server-Timing", trailerTiming);
        }

        return response;
    }

    private static string GraphBody(string slug, int nodeCount, int edgeCount) =>
        JsonSerializer.Serialize(new
        {
            slug,
            title = "Fixture",
            description = "Fixture graph",
            nodes = Enumerable.Range(0, nodeCount).Select(index => new { id = $"n-{index:D5}" }),
            edges = Enumerable.Range(0, edgeCount).Select(index => new { id = $"e-{index:D5}" })
        });

    private static string CatalogBody() => JsonSerializer.Serialize(
        StressGraphSeedCatalog.All.Select(specification => new
        {
            slug = specification.Slug,
            title = specification.Title,
            description = specification.Description,
            nodeCount = specification.NodeCount,
            edgeCount = specification.EdgeCount
        }));

    private static string EvidenceResponse(Backend.Models.Domain.Graph graph, string targetNodeId)
    {
        var result = new EvidenceImpactV0Analysis().Analyze(graph, targetNodeId);
        return JsonSerializer.Serialize(new
        {
            supportingEvidence = result.SupportingEvidence.Select(item => new
            {
                nodeId = item.NodeId,
                logLr = item.AccumulatedPathLogLikelihoodRatio,
                probabilityDifference = (double)item.RawProbabilityDelta
            }),
            counterEvidence = result.CounterEvidence.Select(item => new
            {
                nodeId = item.NodeId,
                logLr = item.AccumulatedPathLogLikelihoodRatio,
                probabilityDifference = (double)item.RawProbabilityDelta
            })
        });
    }

    private static string EvidenceResponseWithFirstLogLrDelta(
        Backend.Models.Domain.Graph graph,
        string targetNodeId,
        decimal delta)
    {
        var root = JsonNode.Parse(EvidenceResponse(graph, targetNodeId))!.AsObject();
        foreach (var partitionName in new[] { "supportingEvidence", "counterEvidence" })
        {
            var partition = root[partitionName]!.AsArray();
            if (partition.Count == 0)
            {
                continue;
            }

            var first = partition[0]!.AsObject();
            first["logLr"] = first["logLr"]!.GetValue<decimal>() + delta;
            return root.ToJsonString();
        }

        Assert.Fail("The precision fixture must contain at least one ranked evidence item.");
        return string.Empty;
    }

    private static string RobustnessResponse(Backend.Models.Domain.Graph graph)
    {
        var result = new RobustnessV0Analyzer().Analyze(graph);
        return JsonSerializer.Serialize(result.Ranking.Select(item => new
        {
            nodeId = item.NodeId,
            nodeTitle = item.Title,
            robustness = item.RobustnessScore
        }));
    }

    private sealed class DelegateHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _handler;

        public DelegateHandler(
            Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler) =>
            _handler = handler;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) => _handler(request, cancellationToken);
    }

    private class ThrowingAfterFirstReadStream : Stream
    {
        private readonly byte[] _prefix;
        private bool _read;

        public ThrowingAfterFirstReadStream(byte[] prefix) => _prefix = prefix;

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => _prefix.LongLength;
        public override long Position { get => _read ? _prefix.LongLength : 0; set => throw new NotSupportedException(); }

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (_read) throw new IOException("Simulated transfer failure.");
            _read = true;
            var length = Math.Min(count, _prefix.Length);
            Array.Copy(_prefix, 0, buffer, offset, length);
            return length;
        }

        public override ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            if (_read) return ValueTask.FromException<int>(new IOException("Simulated transfer failure."));
            _read = true;
            var length = Math.Min(buffer.Length, _prefix.Length);
            _prefix.AsMemory(0, length).CopyTo(buffer);
            return ValueTask.FromResult(length);
        }

        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private sealed class BlockingAfterFirstReadStream : ThrowingAfterFirstReadStream
    {
        private readonly byte[] _prefix;
        private bool _first = true;

        public BlockingAfterFirstReadStream(byte[] prefix) : base(prefix) => _prefix = prefix;

        public override ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            if (_first)
            {
                _first = false;
                var length = Math.Min(buffer.Length, _prefix.Length);
                _prefix.AsMemory(0, length).CopyTo(buffer);
                return ValueTask.FromResult(length);
            }

            return new ValueTask<int>(WaitForCancellation(cancellationToken));
        }

        private static async Task<int> WaitForCancellation(CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return 0;
        }
    }
}
