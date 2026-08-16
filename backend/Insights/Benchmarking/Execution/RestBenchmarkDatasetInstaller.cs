using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Backend.Insights.Contracts;
using Backend.Insights.Measurement;
using Backend.Seeding;

namespace Backend.Insights.Benchmarking;

public sealed record DatasetInstallationResult(
    ExecutionOutcome Execution,
    decimal WallClockDuration,
    long RequestBytes,
    long ResponseBytes,
    decimal? TimeToFirstByte,
    decimal? FullTransferDuration);

/// <summary>
/// Installs canonical stress graphs through the ordinary reset endpoint. The
/// routing executor records this exchange as setup before starting a measured
/// graph catalog/fetch exchange.
/// </summary>
public sealed class RestBenchmarkDatasetInstaller
{
    private readonly HttpClient _httpClient;
    private readonly Uri _baseAddress;
    private readonly DatabaseResetTargetExpectation? _targetExpectation;

    public RestBenchmarkDatasetInstaller(
        HttpClient httpClient,
        Uri baseAddress,
        DatabaseResetTargetExpectation? targetExpectation = null)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        ArgumentNullException.ThrowIfNull(baseAddress);
        if (!baseAddress.IsAbsoluteUri ||
            (baseAddress.Scheme != Uri.UriSchemeHttp &&
             baseAddress.Scheme != Uri.UriSchemeHttps))
        {
            throw new ArgumentException(
                "The dataset installer base address must be an absolute HTTP or HTTPS URI.",
                nameof(baseAddress));
        }

        _baseAddress = baseAddress;
        _targetExpectation = targetExpectation;
    }

    public async Task<DatasetInstallationResult> InstallAsync(
        Guid runId,
        Guid sampleId,
        IReadOnlyCollection<string> datasetIds,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        if (runId == Guid.Empty) throw new ArgumentException("Run ID must not be empty.", nameof(runId));
        if (sampleId == Guid.Empty) throw new ArgumentException("Sample ID must not be empty.", nameof(sampleId));
        ArgumentNullException.ThrowIfNull(datasetIds);
        if (timeout <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(timeout));

        var canonicalIds = StressGraphSeedCatalog.Resolve(datasetIds)
            .Select(specification => specification.Id)
            .ToArray();
        var requestBody = Encoding.UTF8.GetBytes(CanonicalJson.Canonicalize(new
        {
            stressGraphIds = canonicalIds,
            expectedDatabaseName = _targetExpectation?.DatabaseName,
            expectedDatabaseFingerprint = _targetExpectation?.Fingerprint
        }));
        var started = Stopwatch.GetTimestamp();
        decimal? timeToFirstByte = null;
        decimal? fullTransfer = null;
        long responseBytes = 0;
        ExecutionOutcome outcome;
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(timeout);

        try
        {
            using var request = new HttpRequestMessage(
                HttpMethod.Post,
                new Uri(NormalizeBaseAddress(_baseAddress), "api/graphs/reset"))
            {
                Version = HttpVersion.Version20,
                VersionPolicy = HttpVersionPolicy.RequestVersionExact,
                Content = new ByteArrayContent(requestBody)
            };
            request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json")
            {
                CharSet = "utf-8"
            };
            request.Headers.TryAddWithoutValidation(InsightCorrelationHeaders.RunId, runId.ToString("D"));
            request.Headers.TryAddWithoutValidation(InsightCorrelationHeaders.SampleId, sampleId.ToString("D"));

            using var response = await _httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                deadline.Token);
            timeToFirstByte = ElapsedMilliseconds(started);
            await using var responseStream = await response.Content.ReadAsStreamAsync(deadline.Token);
            var chunk = new byte[16 * 1024];
            while (true)
            {
                var read = await responseStream.ReadAsync(chunk, deadline.Token);
                if (read == 0) break;
                responseBytes += read;
            }

            fullTransfer = ElapsedMilliseconds(started);
            var echoedRunId = SingleHeader(response.Headers, InsightCorrelationHeaders.RunId);
            var echoedSampleId = SingleHeader(response.Headers, InsightCorrelationHeaders.SampleId);
            if (!string.Equals(echoedRunId, runId.ToString("D"), StringComparison.Ordinal) ||
                !string.Equals(echoedSampleId, sampleId.ToString("D"), StringComparison.Ordinal))
            {
                outcome = BenchmarkOperationExecutor.Failure(
                    ExecutionStatus.Failed,
                    FailureKind.Execution,
                    "dataset-install-correlation-mismatch",
                    "The dataset reset response did not echo the setup correlation IDs.");
            }
            else if (response.StatusCode == HttpStatusCode.Conflict)
            {
                outcome = BenchmarkOperationExecutor.Failure(
                    ExecutionStatus.Failed,
                    FailureKind.Execution,
                    "dataset-install-database-identity-mismatch",
                    "Dataset installation was refused because the API database target did not match the runner expectation.");
            }
            else if (response.StatusCode != HttpStatusCode.NoContent)
            {
                outcome = BenchmarkOperationExecutor.Failure(
                    ExecutionStatus.Failed,
                    FailureKind.Execution,
                    "dataset-install-http-status",
                    $"Dataset installation returned HTTP {(int)response.StatusCode} ({response.StatusCode}).");
            }
            else
            {
                outcome = new ExecutionOutcome(ExecutionStatus.Succeeded);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            outcome = BenchmarkOperationExecutor.Failure(
                ExecutionStatus.Cancelled,
                FailureKind.Cancellation,
                "dataset-install-cancelled",
                "Dataset installation was cancelled by the caller.");
        }
        catch (OperationCanceledException) when (deadline.IsCancellationRequested)
        {
            outcome = BenchmarkOperationExecutor.Failure(
                ExecutionStatus.TimedOut,
                FailureKind.Timeout,
                "dataset-install-timeout",
                "Dataset installation exceeded its setup timeout.");
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException)
        {
            outcome = BenchmarkOperationExecutor.Failure(
                ExecutionStatus.Failed,
                FailureKind.Execution,
                "dataset-install-failed",
                "Dataset installation failed before the measured REST operation began.",
                exception.GetType().FullName);
        }

        return new DatasetInstallationResult(
            outcome,
            ElapsedMilliseconds(started),
            requestBody.LongLength,
            responseBytes,
            timeToFirstByte,
            fullTransfer);
    }

    private static string? SingleHeader(HttpResponseHeaders headers, string name)
    {
        if (!headers.TryGetValues(name, out var values)) return null;
        var materialized = values.ToArray();
        return materialized.Length == 1 ? materialized[0] : null;
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
}
