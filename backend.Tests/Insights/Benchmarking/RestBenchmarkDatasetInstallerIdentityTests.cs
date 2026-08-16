using System.Net;
using System.Text;
using System.Text.Json;
using Backend.Insights.Benchmarking;
using Backend.Insights.Contracts;
using Backend.Insights.Measurement;
using Backend.Seeding;

namespace backend.Tests.Insights.Benchmarking;

[TestClass]
public sealed class RestBenchmarkDatasetInstallerIdentityTests
{
    private static readonly Uri BaseAddress = new("http://127.0.0.1:43127/");

    [TestMethod]
    public async Task InstallAsync_SendsOpaqueTargetExpectationWithCanonicalDatasetSelection()
    {
        JsonElement? observedBody = null;
        using var client = Client(async (request, cancellationToken) =>
        {
            observedBody = JsonDocument.Parse(
                await request.Content!.ReadAsStringAsync(cancellationToken)).RootElement.Clone();
            return CorrelatedResponse(request, HttpStatusCode.NoContent);
        });
        var expectation = new DatabaseResetTargetExpectation(
            "logiclikely_benchmark_test",
            DatabaseResetTargetIdentity.ComputeFingerprint("stable-target-tuple"));
        var installer = new RestBenchmarkDatasetInstaller(client, BaseAddress, expectation);

        var result = await installer.InstallAsync(
            Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
            [StressGraphSeedIds.Deep1K, StressGraphSeedIds.Balanced1K],
            TimeSpan.FromSeconds(2));

        Assert.AreEqual(ExecutionStatus.Succeeded, result.Execution.Status);
        Assert.IsTrue(observedBody.HasValue);
        CollectionAssert.AreEqual(
            new[] { StressGraphSeedIds.Balanced1K, StressGraphSeedIds.Deep1K },
            observedBody.Value.GetProperty("stressGraphIds")
                .EnumerateArray()
                .Select(value => value.GetString())
                .ToArray());
        Assert.AreEqual(
            expectation.DatabaseName,
            observedBody.Value.GetProperty("expectedDatabaseName").GetString());
        Assert.AreEqual(
            expectation.Fingerprint,
            observedBody.Value.GetProperty("expectedDatabaseFingerprint").GetString());
    }

    [TestMethod]
    public async Task InstallAsync_IdentityConflict_IsSpecificStructuredSetupFailure()
    {
        using var client = Client((request, _) => Task.FromResult(
            CorrelatedResponse(request, HttpStatusCode.Conflict)));
        var installer = new RestBenchmarkDatasetInstaller(
            client,
            BaseAddress,
            new DatabaseResetTargetExpectation(
                "logiclikely_benchmark_test",
                DatabaseResetTargetIdentity.ComputeFingerprint("runner-target-tuple")));

        var result = await installer.InstallAsync(
            Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
            [StressGraphSeedIds.Balanced1K],
            TimeSpan.FromSeconds(2));

        Assert.AreEqual(ExecutionStatus.Failed, result.Execution.Status);
        Assert.AreEqual(FailureKind.Execution, result.Execution.Failure?.Kind);
        Assert.AreEqual(
            "dataset-install-database-identity-mismatch",
            result.Execution.Failure?.Code);
    }

    [TestMethod]
    public void TargetFingerprint_IsVersionedDeterministicAndStrictlyValidated()
    {
        var first = DatabaseResetTargetIdentity.ComputeFingerprint("stable-target-tuple");
        var second = DatabaseResetTargetIdentity.ComputeFingerprint("stable-target-tuple");

        Assert.AreEqual(first, second);
        StringAssert.StartsWith(first, "sha256:");
        Assert.AreEqual(71, first.Length);
        Assert.IsTrue(DatabaseResetTargetIdentity.IsValidFingerprint(first));
        Assert.IsFalse(DatabaseResetTargetIdentity.IsValidFingerprint(first.ToUpperInvariant()));
        Assert.IsFalse(DatabaseResetTargetIdentity.IsValidFingerprint("sha256:not-a-digest"));
    }

    private static HttpClient Client(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler) =>
        new(new DelegatingTestHandler(handler))
        {
            Timeout = Timeout.InfiniteTimeSpan
        };

    private static HttpResponseMessage CorrelatedResponse(
        HttpRequestMessage request,
        HttpStatusCode statusCode)
    {
        var response = new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(string.Empty, Encoding.UTF8, "application/json")
        };
        response.Headers.TryAddWithoutValidation(
            InsightCorrelationHeaders.RunId,
            request.Headers.GetValues(InsightCorrelationHeaders.RunId).Single());
        response.Headers.TryAddWithoutValidation(
            InsightCorrelationHeaders.SampleId,
            request.Headers.GetValues(InsightCorrelationHeaders.SampleId).Single());
        return response;
    }

    private sealed class DelegatingTestHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) => send(request, cancellationToken);
    }
}
