using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Backend.Insights.Contracts;
using Backend.Insights.Measurement;
using Backend.Insights.Workers;
using Backend.Seeding;

namespace Backend.Insights.Benchmarking;

public enum RestApiJourneyBoundary
{
    RealProcessNetwork,
    InMemoryTestDouble
}

public sealed record RestApiJourneyOptions
{
    public RestApiJourneyOptions(
        Uri baseAddress,
        RestApiJourneyBoundary boundary,
        string postgreSqlVersion = "not-reported")
    {
        ArgumentNullException.ThrowIfNull(baseAddress);
        if (!baseAddress.IsAbsoluteUri ||
            (baseAddress.Scheme != Uri.UriSchemeHttp &&
             baseAddress.Scheme != Uri.UriSchemeHttps))
        {
            throw new ArgumentException(
                "The REST benchmark base address must be an absolute HTTP or HTTPS URI.",
                nameof(baseAddress));
        }

        BaseAddress = baseAddress;
        Boundary = boundary;
        PostgreSqlVersion = string.IsNullOrWhiteSpace(postgreSqlVersion)
            ? throw new ArgumentException(
                "PostgreSQL version evidence must not be empty.",
                nameof(postgreSqlVersion))
            : postgreSqlVersion;
    }

    public Uri BaseAddress { get; }

    public RestApiJourneyBoundary Boundary { get; }

    public string PostgreSqlVersion { get; }
}

public sealed record RestServerTiming(
    string Layer,
    string Phase,
    decimal Duration);

/// <summary>
/// Executes the graph catalog and complete graph fetch through HttpClient. A
/// CLI-created instance uses a sockets handler and therefore owns a real
/// process/network timing boundary. Test-double instances retain the same
/// evidence shape but conservatively mark client timings as estimated.
/// </summary>
public sealed class RestApiBenchmarkOperationExecutor :
    IBenchmarkOperationExecutor,
    IBenchmarkScenarioPreparer
{
    private const string ServerTimingHeader = "Server-Timing";
    private const string PostgreSqlGeneratorVersion = "postgresql-stress-seed-v1";
    private static readonly JsonSerializerOptions JsonOptions =
        CanonicalJson.CreateSerializerOptions();

    private readonly HttpClient _httpClient;
    private readonly RestApiJourneyOptions _options;

    public RestApiBenchmarkOperationExecutor(
        HttpClient httpClient,
        RestApiJourneyOptions options)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public async Task<BenchmarkScenarioPreparationResult> PrepareAsync(
        PreparedBenchmarkOperation operation,
        BenchmarkScenarioDefinition scenario,
        DeterministicStressGraphFixture fixture,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var started = Stopwatch.GetTimestamp();
        decimal? timeToFirstByte = null;
        decimal? fullTransfer = null;
        long responseBytes = 0;
        var actualNodes = (long?)null;
        var actualEdges = (long?)null;
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(timeout);

        try
        {
            var relativePath = scenario.OperationKey == OperationKeys.GraphCatalog
                ? "api/graphs"
                : $"api/graphs/{Uri.EscapeDataString(scenario.DatasetId)}";
            using var request = new HttpRequestMessage(
                HttpMethod.Get,
                new Uri(NormalizeBaseAddress(_options.BaseAddress), relativePath))
            {
                Version = HttpVersion.Version20,
                VersionPolicy = HttpVersionPolicy.RequestVersionExact
            };
            request.Headers.TryAddWithoutValidation(
                InsightCorrelationHeaders.RunId,
                operation.Request.RunId.ToString("D"));
            request.Headers.TryAddWithoutValidation(
                InsightCorrelationHeaders.SampleId,
                operation.Request.SampleId.ToString("D"));
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            using var response = await _httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                deadline.Token);
            timeToFirstByte = ElapsedMilliseconds(started);
            var correlationFailure = ValidateCorrelation(response.Headers, operation);
            await using var stream = await response.Content.ReadAsStreamAsync(deadline.Token);
            using var buffer = new MemoryStream();
            var chunk = new byte[64 * 1024];
            while (true)
            {
                var read = await stream.ReadAsync(chunk, deadline.Token);
                if (read == 0) break;
                responseBytes += read;
                await buffer.WriteAsync(chunk.AsMemory(0, read), deadline.Token);
            }

            fullTransfer = ElapsedMilliseconds(started);
            if (correlationFailure is not null)
            {
                return FailedPreparation(
                    operation, scenario, fixture, correlationFailure,
                    started, responseBytes, timeToFirstByte, fullTransfer);
            }

            if (!response.IsSuccessStatusCode)
            {
                return FailedPreparation(
                    operation,
                    scenario,
                    fixture,
                    BenchmarkOperationExecutor.Failure(
                        ExecutionStatus.Failed,
                        FailureKind.Execution,
                        "rest-dataset-probe-http-status",
                        $"Dataset identity probe returned HTTP {(int)response.StatusCode} ({response.StatusCode})."),
                    started,
                    responseBytes,
                    timeToFirstByte,
                    fullTransfer);
            }

            using var document = JsonDocument.Parse(buffer.ToArray());
            var corpus = LoadPostgreSqlCorpusIdentity();
            GraphRunIdentity graphIdentity;
            DatasetRunIdentity datasetIdentity;
            PreparedBenchmarkOperation preparedOperation = operation;
            if (scenario.OperationKey == OperationKeys.GraphCatalog)
            {
                (graphIdentity, datasetIdentity, actualNodes, actualEdges) =
                    CreateCatalogIdentity(document.RootElement, corpus);
            }
            else
            {
                (graphIdentity, datasetIdentity, actualNodes, actualEdges) =
                    CreateGraphIdentity(document.RootElement, fixture, corpus);
                if (scenario.OperationKey is
                    OperationKeys.EvidenceImpactRanking or OperationKeys.NodeRobustness)
                {
                    preparedOperation = WithSuppliedGraph(
                        operation,
                        fixture,
                        document.RootElement);
                }
            }

            var success = new ExecutionOutcome(ExecutionStatus.Succeeded);
            var sample = CreatePreparationSample(
                operation,
                scenario,
                fixture,
                success,
                ElapsedMilliseconds(started),
                responseBytes,
                timeToFirstByte,
                fullTransfer,
                actualNodes,
                actualEdges);
            var dependencies = new DependencyVersions(
                Environment.Version.ToString(),
                "not-used",
                "not-used",
                "not-used",
                _options.PostgreSqlVersion,
                new Dictionary<string, string>
                {
                    ["api-boundary"] = _options.Boundary == RestApiJourneyBoundary.RealProcessNetwork
                        ? "real-process-network"
                        : "in-memory-test-double",
                    ["api-http-version"] = response.Version.ToString(),
                    ["api-host-class"] = _options.BaseAddress.IsLoopback ? "loopback" : "remote"
                });
            return new BenchmarkScenarioPreparationResult(
                preparedOperation,
                success,
                [sample],
                graphIdentity,
                datasetIdentity,
                dependencies,
                CreateEnvironmentProfile(response.Version),
                RunnerType.ApiBrowserJourney);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return FailedPreparation(
                operation,
                scenario,
                fixture,
                BenchmarkOperationExecutor.Failure(
                    ExecutionStatus.Cancelled,
                    FailureKind.Cancellation,
                    "rest-dataset-probe-cancelled",
                    "Dataset identity preparation was cancelled by the caller."),
                started,
                responseBytes,
                timeToFirstByte,
                fullTransfer);
        }
        catch (OperationCanceledException) when (deadline.IsCancellationRequested)
        {
            return FailedPreparation(
                operation,
                scenario,
                fixture,
                BenchmarkOperationExecutor.Failure(
                    ExecutionStatus.TimedOut,
                    FailureKind.Timeout,
                    "rest-dataset-probe-timeout",
                    "Dataset identity preparation exceeded its setup timeout."),
                started,
                responseBytes,
                timeToFirstByte,
                fullTransfer);
        }
        catch (Exception exception) when (
            exception is HttpRequestException or IOException or JsonException or
                InvalidOperationException or ArgumentException or FileNotFoundException)
        {
            return FailedPreparation(
                operation,
                scenario,
                fixture,
                BenchmarkOperationExecutor.Failure(
                    ExecutionStatus.Failed,
                    FailureKind.Execution,
                    "rest-dataset-probe-failed",
                    "Dataset identity preparation failed before measured execution.",
                    exception.GetType().FullName),
                started,
                responseBytes,
                timeToFirstByte,
                fullTransfer);
        }
    }

    private static BenchmarkScenarioPreparationResult FailedPreparation(
        PreparedBenchmarkOperation operation,
        BenchmarkScenarioDefinition scenario,
        DeterministicStressGraphFixture fixture,
        ExecutionOutcome outcome,
        long started,
        long responseBytes,
        decimal? timeToFirstByte,
        decimal? fullTransfer)
    {
        var sample = CreatePreparationSample(
            operation,
            scenario,
            fixture,
            outcome,
            ElapsedMilliseconds(started),
            responseBytes,
            timeToFirstByte,
            fullTransfer,
            null,
            null);
        return new BenchmarkScenarioPreparationResult(
            operation,
            outcome,
            [sample]);
    }

    private static RunSample CreatePreparationSample(
        PreparedBenchmarkOperation operation,
        BenchmarkScenarioDefinition scenario,
        DeterministicStressGraphFixture fixture,
        ExecutionOutcome outcome,
        decimal duration,
        long responseBytes,
        decimal? timeToFirstByte,
        decimal? fullTransfer,
        long? actualNodes,
        long? actualEdges) => new(
            operation.Request.RunId,
            operation.Request.SampleId,
            scenario.Key,
            scenario.OperationKey,
            InsightMeasurementLayers.BenchmarkOrchestration,
            InsightMeasurementPhases.FixtureConstruction,
            duration,
            0,
            new IterationClassification(
                IterationClassificationTokens.Setup,
                IterationClassificationTokens.Cold,
                IterationClassificationTokens.PreJit,
                IterationClassificationTokens.ColdCache),
            new SampleNodeCounts(fixture.NodeCount, actualNodes, actualNodes, null),
            new SampleEdgeCounts(
                fixture.EdgeCount,
                null,
                (actualNodes ?? fixture.NodeCount) == 0
                    ? null
                    : (decimal)(actualEdges ?? fixture.EdgeCount) / (actualNodes ?? fixture.NodeCount)),
            new SampleSearchCounts(null, null),
            null,
            new SampleTransportMeasurements(0, responseBytes, timeToFirstByte, fullTransfer),
            new RuntimeResourceMeasurements(null, null, null, null, null, "ms", null),
            outcome,
            BenchmarkOperationExecutor.StandardUnits,
            TimingBoundaryProvenance.DirectlyInstrumented,
            null);

    private static (GraphRunIdentity Graph, DatasetRunIdentity Dataset, long Nodes, long Edges)
        CreateGraphIdentity(
            JsonElement root,
            DeterministicStressGraphFixture fixture,
            PostgreSqlCorpusIdentity corpus)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new JsonException("The dataset graph probe must return an object.");
        }

        var slug = RequiredString(root, "slug");
        if (!string.Equals(slug, fixture.Specification.Slug, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"Dataset graph probe returned '{slug}' instead of '{fixture.Specification.Slug}'.");
        }

        if (!root.TryGetProperty("nodes", out var nodes) || nodes.ValueKind != JsonValueKind.Array ||
            !root.TryGetProperty("edges", out var edges) || edges.ValueKind != JsonValueKind.Array)
        {
            throw new JsonException("The dataset graph probe must contain nodes and edges arrays.");
        }

        var nodeCount = nodes.GetArrayLength();
        var edgeCount = edges.GetArrayLength();
        if (nodeCount != fixture.Specification.NodeCount || edgeCount != fixture.Specification.EdgeCount)
        {
            throw new ArgumentException(
                $"Dataset graph probe returned {nodeCount} nodes/{edgeCount} edges; expected " +
                $"{fixture.Specification.NodeCount}/{fixture.Specification.EdgeCount}.");
        }

        var nodeIds = nodes.EnumerateArray()
            .Select(node => RequiredString(node, "id"))
            .Order(StringComparer.Ordinal)
            .ToArray();
        var edgeTopology = edges.EnumerateArray()
            .OrderBy(edge => RequiredString(edge, "id"), StringComparer.Ordinal)
            .Select(edge => edge.Clone())
            .ToArray();
        var topologyFingerprint = CanonicalJson.ComputeSha256(new
        {
            generatorVersion = PostgreSqlGeneratorVersion,
            fixture.Specification.Shape,
            nodeCount,
            edgeCount,
            nodeIds,
            edges = edgeTopology
        });
        var inputFingerprint = CanonicalJson.ComputeSha256(root);
        var datasetInputFingerprint = CanonicalJson.ComputeSha256(new
        {
            generatorVersion = PostgreSqlGeneratorVersion,
            corpus.CorpusId,
            corpus.CorpusFingerprint,
            topologyFingerprint,
            inputFingerprint
        });
        return (
            new GraphRunIdentity(
                slug,
                fixture.Specification.GraphId.ToString(CultureInfo.InvariantCulture),
                fixture.Specification.Shape,
                nodeCount,
                edgeCount,
                fixture.Specification.MaximumDepth),
            new DatasetRunIdentity(
                PostgreSqlGeneratorVersion,
                corpus.CorpusId,
                corpus.CorpusFingerprint,
                topologyFingerprint,
                inputFingerprint,
                datasetInputFingerprint),
            nodeCount,
            edgeCount);
    }

    private static (GraphRunIdentity Graph, DatasetRunIdentity Dataset, long Nodes, long Edges)
        CreateCatalogIdentity(
            JsonElement root,
            PostgreSqlCorpusIdentity corpus)
    {
        if (root.ValueKind != JsonValueKind.Array)
        {
            throw new JsonException("The dataset catalog probe must return an array.");
        }

        var catalog = root.EnumerateArray()
            .Select(item => new CatalogProjection(
                RequiredString(item, "slug"),
                RequiredString(item, "title"),
                RequiredLong(item, "nodeCount"),
                RequiredLong(item, "edgeCount")))
            .OrderBy(item => item.Slug, StringComparer.Ordinal)
            .ToArray();
        var bySlug = catalog.ToDictionary(item => item.Slug, StringComparer.Ordinal);
        foreach (var expected in StressGraphSeedCatalog.All)
        {
            if (!bySlug.TryGetValue(expected.Slug, out var actual) ||
                actual.NodeCount != expected.NodeCount || actual.EdgeCount != expected.EdgeCount)
            {
                throw new ArgumentException(
                    $"Dataset catalog probe did not confirm canonical stress graph '{expected.Slug}'.");
            }
        }

        var actualStress = StressGraphSeedCatalog.All
            .Select(specification => bySlug[specification.Slug])
            .ToArray();
        var nodeCount = actualStress.Sum(item => item.NodeCount);
        var edgeCount = actualStress.Sum(item => item.EdgeCount);
        var topologyFingerprint = CanonicalJson.ComputeSha256(new
        {
            generatorVersion = PostgreSqlGeneratorVersion,
            graphs = StressGraphSeedCatalog.All.Select(specification => new
            {
                specification.Slug,
                specification.Shape,
                specification.NodeCount,
                specification.EdgeCount,
                specification.MaximumDepth
            }).ToArray()
        });
        var inputFingerprint = CanonicalJson.ComputeSha256(actualStress);
        var datasetInputFingerprint = CanonicalJson.ComputeSha256(new
        {
            generatorVersion = PostgreSqlGeneratorVersion,
            corpus.CorpusId,
            corpus.CorpusFingerprint,
            topologyFingerprint,
            inputFingerprint
        });
        return (
            new GraphRunIdentity(
                "canonical-stress-catalog",
                null,
                "catalog-aggregate",
                nodeCount,
                edgeCount,
                StressGraphSeedCatalog.All.Max(specification => specification.MaximumDepth)),
            new DatasetRunIdentity(
                PostgreSqlGeneratorVersion,
                corpus.CorpusId,
                corpus.CorpusFingerprint,
                topologyFingerprint,
                inputFingerprint,
                datasetInputFingerprint),
            nodeCount,
            edgeCount);
    }

    private static PreparedBenchmarkOperation WithSuppliedGraph(
        PreparedBenchmarkOperation operation,
        DeterministicStressGraphFixture fixture,
        JsonElement graph)
    {
        var input = JsonNode.Parse(operation.Request.Input.GetRawText())?.AsObject()
                    ?? throw new JsonException("Prepared worker input must be an object.");
        var graphNode = JsonNode.Parse(graph.GetRawText())?.AsObject()
                        ?? throw new JsonException("Probed graph must be an object.");
        graphNode["id"] = fixture.Specification.GraphId;
        input["graph"] = graphNode;
        var request = new WorkerRequestFrame(
            operation.Request.RunId,
            operation.Request.SampleId,
            operation.Request.OperationKey,
            operation.Request.AlgorithmSemanticIdentity,
            operation.Request.CanonicalParameters,
            JsonSerializer.SerializeToElement(input, JsonOptions));
        return operation with { Request = request };
    }

    private static PostgreSqlCorpusIdentity LoadPostgreSqlCorpusIdentity()
    {
        var path = Path.Combine(
            AppContext.BaseDirectory,
            "data",
            "seed",
            "insights_stress_corpus.json");
        if (!File.Exists(path))
        {
            throw new FileNotFoundException(
                "The canonical PostgreSQL stress corpus was not found for dataset identity.",
                path);
        }

        using var document = JsonDocument.Parse(File.ReadAllBytes(path));
        var corpusId = RequiredString(document.RootElement, "corpusId");
        return new PostgreSqlCorpusIdentity(
            corpusId,
            CanonicalJson.ComputeSha256(document.RootElement));
    }

    public async Task<BenchmarkOperationExecutionResult> ExecuteAsync(
        PreparedBenchmarkOperation operation,
        BenchmarkScenarioDefinition scenario,
        DeterministicStressGraphFixture fixture,
        BenchmarkProfileDefinition profile,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(scenario);
        ArgumentNullException.ThrowIfNull(fixture);
        ArgumentNullException.ThrowIfNull(profile);

        if (scenario.ExecutionTarget == BenchmarkScenarioExecutionTarget.InMemory)
        {
            throw new ArgumentException(
                $"Scenario '{scenario.Key}' is not registered as a REST journey.",
                nameof(scenario));
        }

        if (scenario.OperationKey is not (
                OperationKeys.GraphCatalog or
                OperationKeys.GraphFetch or
                OperationKeys.EvidenceImpactRanking or
                OperationKeys.NodeRobustness))
        {
            throw new ArgumentException(
                $"REST execution does not support operation '{scenario.OperationKey}'.",
                nameof(scenario));
        }

        var started = Stopwatch.GetTimestamp();
        decimal? timeToFirstByte = null;
        decimal? fullTransfer = null;
        long responseBytes = 0;
        HttpStatusCode? statusCode = null;
        string? contentType = null;
        long? contentLength = null;
        long requestBytes = 0;
        var headerTimings = new List<RestServerTiming>();
        var trailerTimings = new List<RestServerTiming>();
        var timingIssues = new List<ValidationFailure>();
        byte[]? body = null;
        ExecutionOutcome terminal;
        CompactRunOutput? output = null;
        long requestedNodeCount = fixture.NodeCount;
        long requestedEdgeCount = fixture.EdgeCount;
        long? actualNodeCount = null;
        long? actualEdgeCount = null;

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(timeout);

        try
        {
            var preparedRequest = CreateRequest(operation, scenario);
            using var request = preparedRequest.Request;
            requestBytes = preparedRequest.RequestBytes;
            using var response = await _httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                deadline.Token);
            timeToFirstByte = ElapsedMilliseconds(started);
            statusCode = response.StatusCode;
            contentType = response.Content.Headers.ContentType?.ToString();
            contentLength = response.Content.Headers.ContentLength;

            ParseServerTiming(response.Headers, headerTimings, timingIssues);
            var correlationFailure = ValidateCorrelation(response.Headers, operation);

            await using (var responseStream = await response.Content.ReadAsStreamAsync(deadline.Token))
            using (var buffer = new MemoryStream())
            {
                var chunk = new byte[64 * 1024];
                while (true)
                {
                    var read = await responseStream.ReadAsync(chunk, deadline.Token);
                    if (read == 0)
                    {
                        break;
                    }

                    responseBytes += read;
                    await buffer.WriteAsync(chunk.AsMemory(0, read), deadline.Token);
                }

                body = buffer.ToArray();
            }

            fullTransfer = ElapsedMilliseconds(started);
            ParseServerTiming(response.TrailingHeaders, trailerTimings, timingIssues);

            if (correlationFailure is not null)
            {
                terminal = correlationFailure;
            }
            else if (!response.IsSuccessStatusCode)
            {
                terminal = BenchmarkOperationExecutor.Failure(
                    ExecutionStatus.Failed,
                    FailureKind.Execution,
                    "rest-api-http-status",
                    $"The REST API returned HTTP {(int)response.StatusCode} ({response.StatusCode}).");
            }
            else if (headerTimings.Count + trailerTimings.Count == 0)
            {
                terminal = BenchmarkOperationExecutor.Failure(
                    ExecutionStatus.Failed,
                    FailureKind.Execution,
                    "rest-api-server-timing-missing",
                    "The correlated REST response did not contain server phase timing evidence.");
            }
            else if (timingIssues.Count > 0)
            {
                terminal = ExecutionOutcome.ValidationFailed(timingIssues);
            }
            else
            {
                var parsed = CreateOutput(
                    operation,
                    scenario,
                    fixture,
                    body,
                    response.StatusCode,
                    contentType,
                    contentLength,
                    requestBytes,
                    responseBytes,
                    timeToFirstByte.Value,
                    fullTransfer.Value,
                    headerTimings.Count,
                    trailerTimings.Count,
                    deadline.Token);
                terminal = parsed.Execution;
                output = parsed.Output;
                requestedNodeCount = parsed.RequestedNodeCount;
                requestedEdgeCount = parsed.RequestedEdgeCount;
                actualNodeCount = parsed.ActualNodeCount;
                actualEdgeCount = parsed.ActualEdgeCount;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            terminal = BenchmarkOperationExecutor.Failure(
                ExecutionStatus.Cancelled,
                FailureKind.Cancellation,
                "rest-api-journey-cancelled",
                "The REST API journey was cancelled by the caller.");
        }
        catch (OperationCanceledException) when (deadline.IsCancellationRequested)
        {
            terminal = BenchmarkOperationExecutor.Failure(
                ExecutionStatus.TimedOut,
                FailureKind.Timeout,
                "rest-api-journey-timeout",
                "The REST API journey exceeded its hard timeout.");
        }
        catch (JsonException exception)
        {
            terminal = BenchmarkOperationExecutor.Failure(
                ExecutionStatus.Failed,
                FailureKind.Execution,
                "rest-api-response-json-invalid",
                "The REST API response was not valid graph journey JSON.",
                exception.GetType().FullName);
        }
        catch (Exception exception) when (
            exception is HttpRequestException or IOException or InvalidOperationException or ArgumentException)
        {
            terminal = BenchmarkOperationExecutor.Failure(
                ExecutionStatus.Failed,
                FailureKind.Execution,
                "rest-api-journey-failed",
                "The REST API journey failed after preserving all completed phase evidence.",
                exception.GetType().FullName);
        }

        var transfer = new SampleTransportMeasurements(
            RequestBytes: requestBytes,
            ResponseBytes: responseBytes,
            TimeToFirstByte: timeToFirstByte,
            FullTransferDuration: fullTransfer);
        var completedPhase = new ExecutionOutcome(ExecutionStatus.Succeeded);
        var timings = headerTimings.Concat(trailerTimings).ToArray();
        var samples = new List<RunSample>(timings.Length + 4);
        samples.AddRange(timings.Select(timing => CreateSample(
            operation,
            scenario,
            fixture,
            profile,
            completedPhase,
            timing.Layer,
            timing.Phase,
            timing.Duration,
            TimingBoundaryProvenance.DirectlyInstrumented,
            requestedNodeCount,
            requestedEdgeCount,
            actualNodeCount,
            actualEdgeCount,
            transfer)));

        var clientProvenance = _options.Boundary == RestApiJourneyBoundary.RealProcessNetwork
            ? TimingBoundaryProvenance.ExternallyObserved
            : TimingBoundaryProvenance.Estimated;
        if (timeToFirstByte.HasValue)
        {
            samples.Add(CreateSample(
                operation, scenario, fixture, profile, completedPhase,
                InsightMeasurementLayers.Transport,
                InsightMeasurementPhases.TimeToFirstByte,
                timeToFirstByte.Value,
                clientProvenance,
                requestedNodeCount,
                requestedEdgeCount,
                actualNodeCount,
                actualEdgeCount,
                transfer));
        }

        samples.Add(CreateSample(
            operation, scenario, fixture, profile,
            fullTransfer.HasValue ? completedPhase : terminal,
            InsightMeasurementLayers.Transport,
            InsightMeasurementPhases.ResponseBytes,
            0,
            clientProvenance,
            requestedNodeCount,
            requestedEdgeCount,
            actualNodeCount,
            actualEdgeCount,
            transfer));
        if (fullTransfer.HasValue)
        {
            samples.Add(CreateSample(
                operation, scenario, fixture, profile, completedPhase,
                InsightMeasurementLayers.Transport,
                InsightMeasurementPhases.FullTransfer,
                fullTransfer.Value,
                clientProvenance,
                requestedNodeCount,
                requestedEdgeCount,
                actualNodeCount,
                actualEdgeCount,
                transfer));
        }

        samples.Add(CreateSample(
            operation, scenario, fixture, profile, terminal,
            InsightMeasurementLayers.BenchmarkOrchestration,
            InsightMeasurementPhases.OperationExecution,
            ElapsedMilliseconds(started),
            TimingBoundaryProvenance.DirectlyInstrumented,
            requestedNodeCount,
            requestedEdgeCount,
            actualNodeCount,
            actualEdgeCount,
            transfer));

        return new BenchmarkOperationExecutionResult(
            terminal,
            samples.AsReadOnly(),
            output is null ? [] : [output]);
    }

    private RestHttpRequest CreateRequest(
        PreparedBenchmarkOperation operation,
        BenchmarkScenarioDefinition scenario)
    {
        var slug = Uri.EscapeDataString(scenario.DatasetId);
        var targetNodeId = operation.TargetNodeId is null
            ? null
            : Uri.EscapeDataString(operation.TargetNodeId);
        var relativePath = scenario.OperationKey switch
        {
            OperationKeys.GraphCatalog => "api/graphs",
            OperationKeys.GraphFetch => $"api/graphs/{slug}",
            OperationKeys.EvidenceImpactRanking =>
                $"api/graphs/{slug}/nodes/{targetNodeId}/evidence-impact-ranking",
            OperationKeys.NodeRobustness => $"api/graphs/{slug}/node-robustness-ranking",
            _ => throw new InvalidOperationException(
                $"Operation '{scenario.OperationKey}' has no REST journey route.")
        };
        var method = scenario.OperationKey is OperationKeys.GraphCatalog or OperationKeys.GraphFetch
            ? HttpMethod.Get
            : HttpMethod.Post;
        var request = new HttpRequestMessage(
            method,
            new Uri(NormalizeBaseAddress(_options.BaseAddress), relativePath))
        {
            Version = HttpVersion.Version20,
            VersionPolicy = HttpVersionPolicy.RequestVersionExact
        };
        request.Headers.TryAddWithoutValidation(
            InsightCorrelationHeaders.RunId,
            operation.Request.RunId.ToString("D"));
        request.Headers.TryAddWithoutValidation(
            InsightCorrelationHeaders.SampleId,
            operation.Request.SampleId.ToString("D"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        long requestBytes = 0;
        if (scenario.ExecutionTarget == BenchmarkScenarioExecutionTarget.RestSuppliedGraph)
        {
            if (!operation.Request.Input.TryGetProperty("graph", out var graph) ||
                graph.ValueKind != JsonValueKind.Object)
            {
                request.Dispose();
                throw new InvalidOperationException(
                    "A supplied-graph REST scenario requires a prepared graph object.");
            }

            var bytes = Encoding.UTF8.GetBytes(graph.GetRawText());
            requestBytes = bytes.LongLength;
            request.Content = new ByteArrayContent(bytes);
            request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json")
            {
                CharSet = "utf-8"
            };
        }

        return new RestHttpRequest(request, requestBytes);
    }

    private static ExecutionOutcome? ValidateCorrelation(
        HttpResponseHeaders headers,
        PreparedBenchmarkOperation operation)
    {
        var expectedRunId = operation.Request.RunId.ToString("D");
        var expectedSampleId = operation.Request.SampleId.ToString("D");
        var runId = SingleHeader(headers, InsightCorrelationHeaders.RunId);
        var sampleId = SingleHeader(headers, InsightCorrelationHeaders.SampleId);
        return string.Equals(runId, expectedRunId, StringComparison.Ordinal) &&
               string.Equals(sampleId, expectedSampleId, StringComparison.Ordinal)
            ? null
            : BenchmarkOperationExecutor.Failure(
                ExecutionStatus.Failed,
                FailureKind.Execution,
                "rest-api-correlation-mismatch",
                "The REST API did not echo the exact benchmark run and sample correlation IDs.");
    }

    private static string? SingleHeader(HttpResponseHeaders headers, string name)
    {
        if (!headers.TryGetValues(name, out var values))
        {
            return null;
        }

        var materialized = values.ToArray();
        return materialized.Length == 1 ? materialized[0] : null;
    }

    private static void ParseServerTiming(
        HttpHeaders headers,
        ICollection<RestServerTiming> timings,
        ICollection<ValidationFailure> issues)
    {
        if (!headers.TryGetValues(ServerTimingHeader, out var values))
        {
            return;
        }

        foreach (var rawHeader in values)
        {
            foreach (var rawEntry in rawHeader.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            {
                var segments = rawEntry.Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
                var separator = segments[0].IndexOf('.', StringComparison.Ordinal);
                if (separator <= 0 || separator == segments[0].Length - 1)
                {
                    continue;
                }

                var layer = segments[0][..separator];
                var phase = segments[0][(separator + 1)..];
                if (!InsightPhaseRegistry.TryGetDefinition(layer, phase, out var definition) ||
                    !definition!.ServerSideMeasurable)
                {
                    continue;
                }

                var durationText = segments
                    .Skip(1)
                    .Select(segment => segment.Split('=', 2, StringSplitOptions.TrimEntries))
                    .Where(parts => parts.Length == 2 && string.Equals(parts[0], "dur", StringComparison.OrdinalIgnoreCase))
                    .Select(parts => parts[1])
                    .SingleOrDefault();
                if (durationText is null ||
                    !decimal.TryParse(
                        durationText,
                        NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint | NumberStyles.AllowExponent,
                        CultureInfo.InvariantCulture,
                        out var duration) ||
                    duration < 0)
                {
                    issues.Add(new ValidationFailure(
                        ServerTimingHeader,
                        "server-timing-duration-invalid",
                        $"Server timing '{segments[0]}' must contain one nonnegative dur value in milliseconds."));
                    continue;
                }

                timings.Add(new RestServerTiming(layer, phase, duration));
            }
        }
    }

    private static ParsedRestOutput CreateOutput(
        PreparedBenchmarkOperation operation,
        BenchmarkScenarioDefinition scenario,
        DeterministicStressGraphFixture fixture,
        byte[] body,
        HttpStatusCode statusCode,
        string? contentType,
        long? contentLength,
        long requestBytes,
        long responseBytes,
        decimal timeToFirstByte,
        decimal fullTransfer,
        int headerTimingCount,
        int trailerTimingCount,
        CancellationToken cancellationToken)
    {
        using var document = JsonDocument.Parse(body);
        return scenario.OperationKey switch
        {
            OperationKeys.GraphCatalog => CreateCatalogOutput(
                operation, scenario, fixture, document.RootElement,
                statusCode, contentType, contentLength, responseBytes,
                timeToFirstByte, fullTransfer, headerTimingCount, trailerTimingCount),
            OperationKeys.GraphFetch => CreateGraphFetchOutput(
                operation, scenario, fixture, document.RootElement,
                statusCode, contentType, contentLength, responseBytes,
                timeToFirstByte, fullTransfer, headerTimingCount, trailerTimingCount),
            OperationKeys.EvidenceImpactRanking => CreateEvidenceImpactOutput(
                operation, scenario, fixture, document.RootElement,
                statusCode, contentType, contentLength, requestBytes, responseBytes,
                timeToFirstByte, fullTransfer, headerTimingCount, trailerTimingCount,
                cancellationToken),
            OperationKeys.NodeRobustness => CreateRobustnessOutput(
                operation, scenario, fixture, document.RootElement,
                statusCode, contentType, contentLength, requestBytes, responseBytes,
                timeToFirstByte, fullTransfer, headerTimingCount, trailerTimingCount,
                cancellationToken),
            _ => throw new InvalidOperationException(
                $"Operation '{scenario.OperationKey}' has no REST response projection.")
        };
    }

    private static ParsedRestOutput CreateCatalogOutput(
        PreparedBenchmarkOperation operation,
        BenchmarkScenarioDefinition scenario,
        DeterministicStressGraphFixture fixture,
        JsonElement root,
        HttpStatusCode statusCode,
        string? contentType,
        long? contentLength,
        long responseBytes,
        decimal timeToFirstByte,
        decimal fullTransfer,
        int headerTimingCount,
        int trailerTimingCount)
    {
        if (root.ValueKind != JsonValueKind.Array)
        {
            throw new JsonException("The graph catalog response must be a JSON array.");
        }

        var catalog = root.EnumerateArray()
            .Select(item => new CatalogProjection(
                RequiredString(item, "slug"),
                RequiredString(item, "title"),
                RequiredLong(item, "nodeCount"),
                RequiredLong(item, "edgeCount")))
            .OrderBy(item => item.Slug, StringComparer.Ordinal)
            .ToArray();
        var bySlug = catalog.ToDictionary(item => item.Slug, StringComparer.Ordinal);
        var failures = new List<ValidationFailure>();
        foreach (var expected in StressGraphSeedCatalog.All)
        {
            if (!bySlug.TryGetValue(expected.Slug, out var actual))
            {
                failures.Add(new ValidationFailure(
                    "catalog",
                    "canonical-stress-graph-missing",
                    $"Catalog response is missing canonical stress graph '{expected.Slug}'."));
            }
            else if (actual.NodeCount != expected.NodeCount || actual.EdgeCount != expected.EdgeCount)
            {
                failures.Add(new ValidationFailure(
                    "catalog",
                    "canonical-stress-graph-count-mismatch",
                    $"Catalog graph '{expected.Slug}' returned {actual.NodeCount} nodes/{actual.EdgeCount} edges; expected {expected.NodeCount}/{expected.EdgeCount}."));
            }
        }

        var expectedNodes = StressGraphSeedCatalog.All.Sum(spec => (long)spec.NodeCount);
        var expectedEdges = StressGraphSeedCatalog.All.Sum(spec => (long)spec.EdgeCount);
        var actualStress = catalog.Where(item => StressGraphSeedCatalog.All.Any(
            spec => string.Equals(spec.Slug, item.Slug, StringComparison.Ordinal))).ToArray();
        var actualNodes = actualStress.Sum(item => item.NodeCount);
        var actualEdges = actualStress.Sum(item => item.EdgeCount);
        if (failures.Count > 0)
        {
            return new ParsedRestOutput(
                ExecutionOutcome.ValidationFailed(failures),
                null,
                expectedNodes,
                expectedEdges,
                actualNodes,
                actualEdges);
        }

        var items = catalog.Select(item => JsonSerializer.SerializeToElement(item, JsonOptions)).ToArray();
        var observedFingerprint = CanonicalJson.ComputeSha256(items);
        var output = new CompactRunOutput(
            operation.Request.RunId,
            operation.Request.SampleId,
            scenario.Key,
            scenario.OperationKey,
            operation.Request.AlgorithmSemanticIdentity,
            operation.Strategy,
            new GraphTargetIdentifiers(
                "canonical-stress-catalog",
                null,
                null,
                []),
            operation.Request.CanonicalParameters,
            new ExecutionOutcome(ExecutionStatus.Succeeded),
            JsonSerializer.SerializeToElement(new
            {
                graphCount = catalog.LongLength,
                canonicalStressGraphCount = StressGraphSeedCatalog.All.Count,
                actualNodeCount = actualNodes,
                actualEdgeCount = actualEdges,
                observedDatasetFingerprint = observedFingerprint,
                responseBytes,
                statusCode = (int)statusCode,
                contentType,
                contentLength,
                correlationEchoed = true
            }, JsonOptions),
            JsonSerializer.SerializeToElement(new
            {
                timeToFirstByte,
                fullTransfer,
                headerTimingCount,
                trailerTimingCount
            }, JsonOptions),
            catalog.LongLength,
            items,
            observedFingerprint,
            null,
            []);
        return new ParsedRestOutput(
            new ExecutionOutcome(ExecutionStatus.Succeeded),
            output,
            expectedNodes,
            expectedEdges,
            actualNodes,
            actualEdges);
    }

    private static ParsedRestOutput CreateGraphFetchOutput(
        PreparedBenchmarkOperation operation,
        BenchmarkScenarioDefinition scenario,
        DeterministicStressGraphFixture fixture,
        JsonElement root,
        HttpStatusCode statusCode,
        string? contentType,
        long? contentLength,
        long responseBytes,
        decimal timeToFirstByte,
        decimal fullTransfer,
        int headerTimingCount,
        int trailerTimingCount)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new JsonException("The graph fetch response must be a JSON object.");
        }

        var slug = RequiredString(root, "slug");
        var nodes = RequiredArrayLength(root, "nodes");
        var edges = RequiredArrayLength(root, "edges");
        var observedFingerprint = CanonicalJson.ComputeSha256(root);
        if (!string.Equals(slug, scenario.DatasetId, StringComparison.Ordinal) ||
            nodes != fixture.NodeCount ||
            edges != fixture.EdgeCount)
        {
            var failures = new List<ValidationFailure>();
            if (!string.Equals(slug, scenario.DatasetId, StringComparison.Ordinal))
            {
                failures.Add(new ValidationFailure(
                    "slug",
                    "graph-fetch-slug-mismatch",
                    $"Graph fetch returned slug '{slug}' instead of '{scenario.DatasetId}'."));
            }

            if (nodes != fixture.NodeCount)
            {
                failures.Add(new ValidationFailure(
                    "nodes",
                    "graph-fetch-node-count-mismatch",
                    $"Graph fetch returned {nodes} nodes; expected {fixture.NodeCount}."));
            }

            if (edges != fixture.EdgeCount)
            {
                failures.Add(new ValidationFailure(
                    "edges",
                    "graph-fetch-edge-count-mismatch",
                    $"Graph fetch returned {edges} edges; expected {fixture.EdgeCount}."));
            }

            return new ParsedRestOutput(
                ExecutionOutcome.ValidationFailed(failures),
                null,
                fixture.NodeCount,
                fixture.EdgeCount,
                nodes,
                edges);
        }

        var item = JsonSerializer.SerializeToElement(new
        {
            slug,
            actualNodeCount = nodes,
            actualEdgeCount = edges,
            observedDatasetFingerprint = observedFingerprint
        }, JsonOptions);
        var items = new[] { item };
        var resultDigest = CanonicalJson.ComputeSha256(items);
        var output = new CompactRunOutput(
            operation.Request.RunId,
            operation.Request.SampleId,
            scenario.Key,
            scenario.OperationKey,
            operation.Request.AlgorithmSemanticIdentity,
            operation.Strategy,
            new GraphTargetIdentifiers(
                fixture.Specification.Slug,
                fixture.Specification.GraphId.ToString(CultureInfo.InvariantCulture),
                null,
                []),
            operation.Request.CanonicalParameters,
            new ExecutionOutcome(ExecutionStatus.Succeeded),
            JsonSerializer.SerializeToElement(new
            {
                slug,
                actualNodeCount = nodes,
                actualEdgeCount = edges,
                observedDatasetFingerprint = observedFingerprint,
                responseBytes,
                statusCode = (int)statusCode,
                contentType,
                contentLength,
                correlationEchoed = true
            }, JsonOptions),
            JsonSerializer.SerializeToElement(new
            {
                timeToFirstByte,
                fullTransfer,
                headerTimingCount,
                trailerTimingCount
            }, JsonOptions),
            1,
            items,
            resultDigest,
            null,
            []);
        return new ParsedRestOutput(
            new ExecutionOutcome(ExecutionStatus.Succeeded),
            output,
            fixture.NodeCount,
            fixture.EdgeCount,
            nodes,
            edges);
    }

    private static ParsedRestOutput CreateEvidenceImpactOutput(
        PreparedBenchmarkOperation operation,
        BenchmarkScenarioDefinition scenario,
        DeterministicStressGraphFixture fixture,
        JsonElement root,
        HttpStatusCode statusCode,
        string? contentType,
        long? contentLength,
        long requestBytes,
        long responseBytes,
        decimal timeToFirstByte,
        decimal fullTransfer,
        int headerTimingCount,
        int trailerTimingCount,
        CancellationToken cancellationToken)
    {
        if (root.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty("supportingEvidence", out var supporting) ||
            supporting.ValueKind != JsonValueKind.Array ||
            !root.TryGetProperty("counterEvidence", out var counter) ||
            counter.ValueKind != JsonValueKind.Array)
        {
            throw new JsonException(
                "The evidence-impact response must contain supportingEvidence and counterEvidence arrays.");
        }

        ValidateEvidenceItems(supporting);
        ValidateEvidenceItems(counter);
        var observedFingerprint = CanonicalJson.ComputeSha256(root);
        var canonical = new AnalysisWorkerDispatcher().Dispatch(
            operation.Request,
            cancellationToken);
        var validationFailures = ValidateEvidenceParity(
            supporting,
            counter,
            canonical);
        if (validationFailures.Count > 0)
        {
            return new ParsedRestOutput(
                ExecutionOutcome.ValidationFailed(validationFailures),
                null,
                fixture.NodeCount,
                fixture.EdgeCount,
                fixture.NodeCount,
                fixture.EdgeCount);
        }

        var output = CreateAnalysisOutput(
            scenario,
            canonical,
            statusCode,
            contentType,
            contentLength,
            requestBytes,
            responseBytes,
            timeToFirstByte,
            fullTransfer,
            headerTimingCount,
            trailerTimingCount,
            observedFingerprint,
            supporting.GetArrayLength() + counter.GetArrayLength());
        return new ParsedRestOutput(
            new ExecutionOutcome(ExecutionStatus.Succeeded),
            output,
            fixture.NodeCount,
            fixture.EdgeCount,
            fixture.NodeCount,
            fixture.EdgeCount);
    }

    private static ParsedRestOutput CreateRobustnessOutput(
        PreparedBenchmarkOperation operation,
        BenchmarkScenarioDefinition scenario,
        DeterministicStressGraphFixture fixture,
        JsonElement root,
        HttpStatusCode statusCode,
        string? contentType,
        long? contentLength,
        long requestBytes,
        long responseBytes,
        decimal timeToFirstByte,
        decimal fullTransfer,
        int headerTimingCount,
        int trailerTimingCount,
        CancellationToken cancellationToken)
    {
        if (root.ValueKind != JsonValueKind.Array)
        {
            throw new JsonException("The robustness response must be a JSON array.");
        }

        foreach (var item in root.EnumerateArray())
        {
            _ = RequiredString(item, "nodeId");
            _ = RequiredString(item, "nodeTitle");
            _ = RequiredDecimal(item, "robustness");
        }

        var observedFingerprint = CanonicalJson.ComputeSha256(root);
        var canonical = new AnalysisWorkerDispatcher().Dispatch(
            operation.Request,
            cancellationToken);
        var validationFailures = ValidateRobustnessParity(root, canonical);
        if (validationFailures.Count > 0)
        {
            return new ParsedRestOutput(
                ExecutionOutcome.ValidationFailed(validationFailures),
                null,
                fixture.NodeCount,
                fixture.EdgeCount,
                fixture.NodeCount,
                fixture.EdgeCount);
        }

        var output = CreateAnalysisOutput(
            scenario,
            canonical,
            statusCode,
            contentType,
            contentLength,
            requestBytes,
            responseBytes,
            timeToFirstByte,
            fullTransfer,
            headerTimingCount,
            trailerTimingCount,
            observedFingerprint,
            root.GetArrayLength());
        return new ParsedRestOutput(
            new ExecutionOutcome(ExecutionStatus.Succeeded),
            output,
            fixture.NodeCount,
            fixture.EdgeCount,
            fixture.NodeCount,
            fixture.EdgeCount);
    }

    private static CompactRunOutput CreateAnalysisOutput(
        BenchmarkScenarioDefinition scenario,
        CompactRunOutput canonical,
        HttpStatusCode statusCode,
        string? contentType,
        long? contentLength,
        long requestBytes,
        long responseBytes,
        decimal timeToFirstByte,
        decimal fullTransfer,
        int headerTimingCount,
        int trailerTimingCount,
        string observedResponseFingerprint,
        long observedResponseCardinality)
    {
        var summary = JsonSerializer.SerializeToElement(new
        {
            canonical = canonical.Summary,
            observedApi = new
            {
                executionTarget = ExecutionTargetToken(scenario.ExecutionTarget),
                responseFingerprint = observedResponseFingerprint,
                responseCardinality = observedResponseCardinality,
                statusCode = (int)statusCode,
                contentType,
                contentLength,
                requestBytes,
                responseBytes,
                timeToFirstByte,
                fullTransfer,
                headerTimingCount,
                trailerTimingCount,
                correlationEchoed = true
            }
        }, JsonOptions);
        var distribution = JsonSerializer.SerializeToElement(new
        {
            canonical = canonical.Distribution,
            transport = new
            {
                timeToFirstByte,
                fullTransfer,
                headerTimingCount,
                trailerTimingCount,
                requestBytes,
                responseBytes
            }
        }, JsonOptions);
        return new CompactRunOutput(
            canonical.RunId,
            canonical.SampleId,
            scenario.Key,
            canonical.OperationKey,
            canonical.AlgorithmSemanticIdentity,
            canonical.Strategy,
            canonical.Identifiers,
            canonical.CanonicalParameters,
            canonical.Execution,
            summary,
            distribution,
            canonical.TotalResultCardinality,
            canonical.Items,
            canonical.ResultDigest,
            canonical.FullResultArtifactReference,
            canonical.OrderedPaths);
    }

    private static void ValidateEvidenceItems(JsonElement source)
    {
        foreach (var item in source.EnumerateArray())
        {
            _ = RequiredString(item, "nodeId");
            _ = RequiredDecimal(item, "logLr");
            _ = RequiredDecimal(item, "probabilityDifference");
        }
    }

    private static IReadOnlyList<ValidationFailure> ValidateEvidenceParity(
        JsonElement supporting,
        JsonElement counter,
        CompactRunOutput canonical)
    {
        var failures = new List<ValidationFailure>();
        var observedCardinality = (long)supporting.GetArrayLength() + counter.GetArrayLength();
        if (observedCardinality != canonical.TotalResultCardinality)
        {
            failures.Add(new ValidationFailure(
                "response",
                "rest-evidence-cardinality-mismatch",
                $"The legacy REST response returned {observedCardinality} evidence items; " +
                $"the canonical evidence-impact result contains {canonical.TotalResultCardinality}."));
        }

        var observedPartitions = new Dictionary<string, JsonElement[]>(StringComparer.Ordinal)
        {
            ["supporting"] = supporting.EnumerateArray().Select(item => item.Clone()).ToArray(),
            ["counter"] = counter.EnumerateArray().Select(item => item.Clone()).ToArray()
        };
        foreach (var canonicalItem in canonical.Items)
        {
            var partition = RequiredString(canonicalItem, "partition");
            var rank = checked((int)RequiredLong(canonicalItem, "rank"));
            if (!observedPartitions.TryGetValue(partition, out var observed) ||
                rank < 1 || rank > observed.Length)
            {
                failures.Add(new ValidationFailure(
                    "response",
                    "rest-evidence-ranked-item-missing",
                    $"The legacy REST response omitted canonical {partition} evidence rank {rank}."));
                continue;
            }

            var actual = observed[rank - 1];
            var canonicalNodeId = RequiredString(canonicalItem, "nodeId");
            var observedNodeId = RequiredString(actual, "nodeId");
            if (!string.Equals(canonicalNodeId, observedNodeId, StringComparison.Ordinal))
            {
                failures.Add(new ValidationFailure(
                    $"response.{partition}[{rank - 1}].nodeId",
                    "rest-evidence-node-id-mismatch",
                    $"The legacy REST response returned node '{observedNodeId}' at {partition} " +
                    $"rank {rank}; canonical evidence returned '{canonicalNodeId}'."));
            }

            var canonicalLogLr = RequiredDecimal(
                canonicalItem,
                "accumulatedPathLogLikelihoodRatio");
            var observedLogLr = RequiredDecimal(actual, "logLr");
            if (CanonicalResultNumber.Normalize(observedLogLr) != canonicalLogLr)
            {
                failures.Add(new ValidationFailure(
                    $"response.{partition}[{rank - 1}].logLr",
                    "rest-evidence-log-lr-mismatch",
                    $"The legacy REST logLr {observedLogLr} does not match canonical value " +
                    $"{canonicalLogLr} for node '{canonicalNodeId}'."));
            }

            var canonicalDifference = RequiredDecimal(
                canonicalItem,
                "rawProbabilityDelta");
            var observedDifference = actual.GetProperty("probabilityDifference").GetDouble();
            if (CanonicalResultNumber.Normalize(observedDifference) != canonicalDifference)
            {
                failures.Add(new ValidationFailure(
                    $"response.{partition}[{rank - 1}].probabilityDifference",
                    "rest-evidence-probability-difference-mismatch",
                    $"The legacy REST probability difference {observedDifference:R} does not " +
                    $"match canonical value {canonicalDifference} for node '{canonicalNodeId}'."));
            }
        }

        return failures.AsReadOnly();
    }

    private static IReadOnlyList<ValidationFailure> ValidateRobustnessParity(
        JsonElement root,
        CompactRunOutput canonical)
    {
        var failures = new List<ValidationFailure>();
        if (root.GetArrayLength() != canonical.TotalResultCardinality)
        {
            failures.Add(new ValidationFailure(
                "response",
                "rest-robustness-cardinality-mismatch",
                $"The legacy REST response returned {root.GetArrayLength()} robustness items; " +
                $"the canonical robustness result contains {canonical.TotalResultCardinality}."));
        }

        var observed = root.EnumerateArray().Select(item => item.Clone()).ToArray();
        for (var index = 0; index < canonical.Items.Count; index++)
        {
            if (index >= observed.Length)
            {
                failures.Add(new ValidationFailure(
                    "response",
                    "rest-robustness-ranked-item-missing",
                    $"The legacy REST response omitted canonical robustness rank {index + 1}."));
                continue;
            }

            var canonicalItem = canonical.Items[index];
            var actual = observed[index];
            var canonicalNodeId = RequiredString(canonicalItem, "nodeId");
            var observedNodeId = RequiredString(actual, "nodeId");
            if (!string.Equals(canonicalNodeId, observedNodeId, StringComparison.Ordinal))
            {
                failures.Add(new ValidationFailure(
                    $"response[{index}].nodeId",
                    "rest-robustness-node-id-mismatch",
                    $"The legacy REST response returned node '{observedNodeId}' at rank " +
                    $"{index + 1}; canonical robustness returned '{canonicalNodeId}'."));
            }

            var canonicalTitle = RequiredString(canonicalItem, "title");
            var observedTitle = RequiredString(actual, "nodeTitle");
            if (!string.Equals(canonicalTitle, observedTitle, StringComparison.Ordinal))
            {
                failures.Add(new ValidationFailure(
                    $"response[{index}].nodeTitle",
                    "rest-robustness-node-title-mismatch",
                    $"The legacy REST title '{observedTitle}' does not match canonical title " +
                    $"'{canonicalTitle}' for node '{canonicalNodeId}'."));
            }

            var canonicalScore = RequiredDecimal(canonicalItem, "robustnessScore");
            var observedScore = RequiredDecimal(actual, "robustness");
            if (CanonicalResultNumber.Normalize(observedScore) != canonicalScore)
            {
                failures.Add(new ValidationFailure(
                    $"response[{index}].robustness",
                    "rest-robustness-score-mismatch",
                    $"The legacy REST robustness score {observedScore} does not match canonical " +
                    $"value {canonicalScore} for node '{canonicalNodeId}'."));
            }
        }

        return failures.AsReadOnly();
    }

    private string CreateEnvironmentProfile(Version httpVersion)
    {
        var postgreSqlMajor = Version.TryParse(_options.PostgreSqlVersion, out var version)
            ? version.Major.ToString(CultureInfo.InvariantCulture)
            : "unreported";
        var boundary = _options.Boundary == RestApiJourneyBoundary.RealProcessNetwork
            ? "real-process-network"
            : "in-memory-test-double";
        var hostClass = _options.BaseAddress.IsLoopback ? "loopback" : "remote";
        return $"rest-postgresql-major-{postgreSqlMajor}-dotnet-major-{Environment.Version.Major}-" +
               $"http-{httpVersion.Major}-{boundary}-{hostClass}";
    }

    private static string ExecutionTargetToken(BenchmarkScenarioExecutionTarget target) => target switch
    {
        BenchmarkScenarioExecutionTarget.RestDatabaseLoaded => "database-loaded",
        BenchmarkScenarioExecutionTarget.RestSuppliedGraph => "supplied-graph",
        _ => "in-memory"
    };

    private static RunSample CreateSample(
        PreparedBenchmarkOperation operation,
        BenchmarkScenarioDefinition scenario,
        DeterministicStressGraphFixture fixture,
        BenchmarkProfileDefinition profile,
        ExecutionOutcome execution,
        string layer,
        string phase,
        decimal duration,
        TimingBoundaryProvenance provenance,
        long requestedNodeCount,
        long requestedEdgeCount,
        long? actualNodeCount,
        long? actualEdgeCount,
        SampleTransportMeasurements transport)
    {
        var temperature = string.Equals(profile.Key, "cold", StringComparison.Ordinal)
            ? IterationClassificationTokens.Cold
            : IterationClassificationTokens.Warm;
        var cacheState = temperature == IterationClassificationTokens.Cold
            ? IterationClassificationTokens.ColdCache
            : IterationClassificationTokens.WarmCache;
        var countForDensity = actualNodeCount ?? requestedNodeCount;
        var edgeCountForDensity = actualEdgeCount ?? requestedEdgeCount;
        return new RunSample(
            operation.Request.RunId,
            operation.Request.SampleId,
            scenario.Key,
            scenario.OperationKey,
            layer,
            phase,
            duration,
            0,
            new IterationClassification(
                IterationClassificationTokens.Measured,
                temperature,
                IterationClassificationTokens.PostJit,
                cacheState),
            new SampleNodeCounts(
                requestedNodeCount,
                actualNodeCount,
                actualNodeCount,
                null),
            new SampleEdgeCounts(
                requestedEdgeCount,
                null,
                countForDensity == 0 ? null : (decimal)edgeCountForDensity / countForDensity),
            new SampleSearchCounts(null, null),
            null,
            transport,
            new RuntimeResourceMeasurements(null, null, null, null, null, "ms", null),
            execution,
            BenchmarkOperationExecutor.StandardUnits,
            provenance,
            null);
    }

    private static string RequiredString(JsonElement value, string property)
    {
        if (!value.TryGetProperty(property, out var item) || item.ValueKind != JsonValueKind.String)
        {
            throw new JsonException($"Response property '{property}' must be a string.");
        }

        return item.GetString()!;
    }

    private static long RequiredLong(JsonElement value, string property)
    {
        if (!value.TryGetProperty(property, out var item) || !item.TryGetInt64(out var result) || result < 0)
        {
            throw new JsonException($"Response property '{property}' must be a nonnegative integer.");
        }

        return result;
    }

    private static decimal RequiredDecimal(JsonElement value, string property)
    {
        if (!value.TryGetProperty(property, out var item) || !item.TryGetDecimal(out var result))
        {
            throw new JsonException($"Response property '{property}' must be a decimal number.");
        }

        return result;
    }

    private static long RequiredArrayLength(JsonElement value, string property)
    {
        if (!value.TryGetProperty(property, out var item) || item.ValueKind != JsonValueKind.Array)
        {
            throw new JsonException($"Response property '{property}' must be an array.");
        }

        return item.GetArrayLength();
    }

    private static Uri NormalizeBaseAddress(Uri baseAddress)
    {
        var text = baseAddress.AbsoluteUri;
        return text.EndsWith("/", StringComparison.Ordinal)
            ? baseAddress
            : new Uri(text + "/", UriKind.Absolute);
    }

    private static decimal ElapsedMilliseconds(long started) =>
        (Stopwatch.GetTimestamp() - started) * 1000m / Stopwatch.Frequency;

    private sealed record CatalogProjection(
        string Slug,
        string Title,
        long NodeCount,
        long EdgeCount);

    private sealed record RestHttpRequest(
        HttpRequestMessage Request,
        long RequestBytes);

    private sealed record PostgreSqlCorpusIdentity(
        string CorpusId,
        string CorpusFingerprint);

    private sealed record ParsedRestOutput(
        ExecutionOutcome Execution,
        CompactRunOutput? Output,
        long RequestedNodeCount,
        long RequestedEdgeCount,
        long? ActualNodeCount,
        long? ActualEdgeCount);
}

public sealed class BenchmarkOperationExecutorRouter :
    IBenchmarkOperationExecutor,
    IBenchmarkScenarioPreparer
{
    private readonly IBenchmarkOperationExecutor _inMemoryExecutor;
    private readonly IBenchmarkOperationExecutor? _restExecutor;
    private readonly IBenchmarkOperationExecutor? _browserExecutor;
    private readonly RestBenchmarkDatasetInstaller? _datasetInstaller;
    private readonly TimeSpan _datasetSetupTimeout;

    public BenchmarkOperationExecutorRouter(
        IBenchmarkOperationExecutor inMemoryExecutor,
        IBenchmarkOperationExecutor? restExecutor = null,
        RestBenchmarkDatasetInstaller? datasetInstaller = null,
        TimeSpan? datasetSetupTimeout = null,
        IBenchmarkOperationExecutor? browserExecutor = null)
    {
        _inMemoryExecutor = inMemoryExecutor ?? throw new ArgumentNullException(nameof(inMemoryExecutor));
        _restExecutor = restExecutor;
        _browserExecutor = browserExecutor;
        _datasetInstaller = datasetInstaller;
        _datasetSetupTimeout = datasetSetupTimeout ?? TimeSpan.FromMinutes(10);
        if (_datasetSetupTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(datasetSetupTimeout),
                "Dataset setup timeout must be positive.");
        }
    }

    public async Task<BenchmarkOperationExecutionResult> ExecuteAsync(
        PreparedBenchmarkOperation operation,
        BenchmarkScenarioDefinition scenario,
        DeterministicStressGraphFixture fixture,
        BenchmarkProfileDefinition profile,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        if (scenario.ExecutionTarget == BenchmarkScenarioExecutionTarget.InMemory)
        {
            return await _inMemoryExecutor.ExecuteAsync(
                operation, scenario, fixture, profile, timeout, cancellationToken);
        }

        if (scenario.ExecutionTarget == BenchmarkScenarioExecutionTarget.Browser)
        {
            if (_browserExecutor is null)
            {
                var outcome = BenchmarkOperationExecutor.Failure(
                    ExecutionStatus.Failed,
                    FailureKind.Execution,
                    "browser-harness-url-required",
                    "This browser scenario requires --browser-harness-url and a Playwright driver.");
                return new BenchmarkOperationExecutionResult(
                    outcome,
                    [CreateUnavailableSample(operation, scenario, fixture, profile, outcome)],
                    []);
            }

            return await _browserExecutor.ExecuteAsync(
                operation, scenario, fixture, profile, timeout, cancellationToken);
        }

        if (_restExecutor is null)
        {
            var outcome = BenchmarkOperationExecutor.Failure(
                ExecutionStatus.Failed,
                FailureKind.Execution,
                "rest-api-base-url-required",
                "This REST scenario requires --api-base-url pointing to a separately running API process.");
            return new BenchmarkOperationExecutionResult(
                outcome,
                [CreateUnavailableSample(operation, scenario, fixture, profile, outcome)],
                []);
        }

        return await _restExecutor.ExecuteAsync(
            operation, scenario, fixture, profile, timeout, cancellationToken);
    }

    public async Task<BenchmarkScenarioPreparationResult> PrepareAsync(
        PreparedBenchmarkOperation operation,
        BenchmarkScenarioDefinition scenario,
        DeterministicStressGraphFixture fixture,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        if (scenario.ExecutionTarget == BenchmarkScenarioExecutionTarget.InMemory)
        {
            return new BenchmarkScenarioPreparationResult(
                operation,
                new ExecutionOutcome(ExecutionStatus.Succeeded),
                []);
        }

        var isBrowser = scenario.ExecutionTarget == BenchmarkScenarioExecutionTarget.Browser;
        var requiresDatabase = !isBrowser ||
            scenario.BrowserJourney?.Action != BrowserJourneyActions.ResultRender;
        var selectedPreparer = isBrowser
            ? _browserExecutor as IBenchmarkScenarioPreparer
            : _restExecutor as IBenchmarkScenarioPreparer;
        if (selectedPreparer is null)
        {
            var outcome = BenchmarkOperationExecutor.Failure(
                ExecutionStatus.Failed,
                FailureKind.Execution,
                isBrowser ? "browser-harness-url-required" : "rest-api-base-url-required",
                isBrowser
                    ? "This browser scenario requires --browser-harness-url and a Playwright driver."
                    : "This REST scenario requires --api-base-url pointing to a separately running API process.");
            return new BenchmarkScenarioPreparationResult(operation, outcome, []);
        }

        if (isBrowser && requiresDatabase && _restExecutor is not IBenchmarkScenarioPreparer)
        {
            var outcome = BenchmarkOperationExecutor.Failure(
                ExecutionStatus.Failed,
                FailureKind.Execution,
                "browser-api-base-url-required",
                "Graph browser scenarios require --api-base-url so PostgreSQL identity is captured before the manifest is created.");
            return new BenchmarkScenarioPreparationResult(operation, outcome, []);
        }

        var setupSamples = new List<RunSample>();
        if (requiresDatabase && _datasetInstaller is not null)
        {
            var datasetIds = scenario.OperationKey == OperationKeys.GraphCatalog
                ? StressGraphSeedCatalog.All.Select(spec => spec.Id).ToArray()
                : [scenario.DatasetId];
            var setup = await _datasetInstaller.InstallAsync(
                operation.Request.RunId,
                operation.Request.SampleId,
                datasetIds,
                _datasetSetupTimeout,
                cancellationToken);
            setupSamples.Add(CreateSetupSample(
                operation, scenario, fixture, setup, datasetIds));
            if (setup.Execution.Status != ExecutionStatus.Succeeded)
            {
                return new BenchmarkScenarioPreparationResult(
                    operation,
                    setup.Execution,
                    setupSamples.AsReadOnly());
            }
        }

        BenchmarkScenarioPreparationResult? databaseProbe = null;
        if (isBrowser && requiresDatabase && _restExecutor is IBenchmarkScenarioPreparer restPreparer)
        {
            databaseProbe = await restPreparer.PrepareAsync(
                operation,
                scenario,
                fixture,
                _datasetSetupTimeout,
                cancellationToken);
            setupSamples.AddRange(databaseProbe.SetupSamples);
            if (databaseProbe.Execution.Status != ExecutionStatus.Succeeded)
            {
                return databaseProbe with
                {
                    SetupSamples = setupSamples.AsReadOnly()
                };
            }

            operation = databaseProbe.Operation;
        }

        var prepared = await selectedPreparer.PrepareAsync(
            operation,
            scenario,
            fixture,
            _datasetSetupTimeout,
            cancellationToken);
        var dependencies = isBrowser
            ? MergeDependencies(databaseProbe?.Dependencies, prepared.Dependencies)
            : prepared.Dependencies;
        return prepared with
        {
            SetupSamples = setupSamples.Concat(prepared.SetupSamples).ToArray(),
            GraphIdentity = databaseProbe?.GraphIdentity ?? prepared.GraphIdentity,
            DatasetIdentity = databaseProbe?.DatasetIdentity ?? prepared.DatasetIdentity,
            Dependencies = dependencies,
            EnvironmentProfile = prepared.EnvironmentProfile ?? databaseProbe?.EnvironmentProfile,
            RunnerType = isBrowser ? RunnerType.ApiBrowserJourney : prepared.RunnerType
        };
    }

    private static DependencyVersions? MergeDependencies(
        DependencyVersions? database,
        DependencyVersions? browser)
    {
        if (database is null) return browser;
        if (browser is null) return database;
        var relevant = database.RelevantDependencies
            .Concat(browser.RelevantDependencies)
            .GroupBy(pair => pair.Key, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Last().Value, StringComparer.Ordinal);
        return new DependencyVersions(
            browser.DotNet,
            browser.Node,
            browser.Browser,
            browser.GraphMap,
            database.PostgreSql,
            relevant);
    }

    private static RunSample CreateSetupSample(
        PreparedBenchmarkOperation operation,
        BenchmarkScenarioDefinition scenario,
        DeterministicStressGraphFixture fixture,
        DatasetInstallationResult setup,
        IReadOnlyList<string> datasetIds)
    {
        var specifications = StressGraphSeedCatalog.Resolve(datasetIds);
        var requestedNodes = specifications.Sum(specification => (long)specification.NodeCount);
        var requestedEdges = specifications.Sum(specification => (long)specification.EdgeCount);
        return new RunSample(
            operation.Request.RunId,
            operation.Request.SampleId,
            scenario.Key,
            scenario.OperationKey,
            InsightMeasurementLayers.BenchmarkOrchestration,
            InsightMeasurementPhases.FixtureConstruction,
            setup.WallClockDuration,
            0,
            new IterationClassification(
                IterationClassificationTokens.Setup,
                IterationClassificationTokens.Cold,
                IterationClassificationTokens.PreJit,
                IterationClassificationTokens.ColdCache),
            new SampleNodeCounts(requestedNodes, null, null, null),
            new SampleEdgeCounts(
                requestedEdges,
                null,
                requestedNodes == 0 ? null : (decimal)requestedEdges / requestedNodes),
            new SampleSearchCounts(null, null),
            null,
            new SampleTransportMeasurements(
                setup.RequestBytes,
                setup.ResponseBytes,
                setup.TimeToFirstByte,
                setup.FullTransferDuration),
            new RuntimeResourceMeasurements(null, null, null, null, null, "ms", null),
            setup.Execution,
            BenchmarkOperationExecutor.StandardUnits,
            TimingBoundaryProvenance.DirectlyInstrumented,
            null);
    }

    private static RunSample CreateUnavailableSample(
        PreparedBenchmarkOperation operation,
        BenchmarkScenarioDefinition scenario,
        DeterministicStressGraphFixture fixture,
        BenchmarkProfileDefinition profile,
        ExecutionOutcome outcome) => new(
            operation.Request.RunId,
            operation.Request.SampleId,
            scenario.Key,
            scenario.OperationKey,
            InsightMeasurementLayers.BenchmarkOrchestration,
            InsightMeasurementPhases.OperationExecution,
            0,
            0,
            new IterationClassification(
                IterationClassificationTokens.Measured,
                string.Equals(profile.Key, "cold", StringComparison.Ordinal)
                    ? IterationClassificationTokens.Cold
                    : IterationClassificationTokens.Warm,
                IterationClassificationTokens.PostJit,
                IterationClassificationTokens.WarmCache),
            new SampleNodeCounts(fixture.NodeCount, null, null, null),
            new SampleEdgeCounts(
                fixture.EdgeCount,
                null,
                fixture.NodeCount == 0 ? null : (decimal)fixture.EdgeCount / fixture.NodeCount),
            new SampleSearchCounts(null, null),
            null,
            new SampleTransportMeasurements(null, null, null, null),
            new RuntimeResourceMeasurements(null, null, null, null, null, "ms", null),
            outcome,
            BenchmarkOperationExecutor.StandardUnits,
            TimingBoundaryProvenance.DirectlyInstrumented,
            null);
}
