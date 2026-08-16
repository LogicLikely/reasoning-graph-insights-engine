using System.Text.Json;
using Backend.Insights.Measurement;
using Backend.Middleware;
using Microsoft.AspNetCore.Http;

namespace backend.Tests.Insights.Measurement;

[TestClass]
public sealed class InsightsCorrelationMiddlewareTests
{
    private static readonly Guid RunId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid SampleId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    [TestMethod]
    public void HeaderParser_TreatsBothAbsentAsAmbientAndRejectsPartialInvalidOrMultipleValues()
    {
        var ambient = InsightCorrelationContext.FromHeaders(new HeaderDictionary());
        Assert.IsTrue(ambient.IsAmbient);
        Assert.IsTrue(ambient.IsValid);

        var partialHeaders = new HeaderDictionary
        {
            [InsightCorrelationHeaders.RunId] = RunId.ToString("D")
        };
        var partial = InsightCorrelationContext.FromHeaders(partialHeaders);
        Assert.IsFalse(partial.IsValid);
        Assert.AreEqual("missing-correlation-header", partial.Failures.Single().Code);

        var invalidHeaders = ValidHeaders();
        invalidHeaders[InsightCorrelationHeaders.SampleId] = Guid.Empty.ToString("D");
        var invalid = InsightCorrelationContext.FromHeaders(invalidHeaders);
        Assert.IsFalse(invalid.IsValid);
        Assert.AreEqual("invalid-correlation-header", invalid.Failures.Single().Code);

        var multipleHeaders = ValidHeaders();
        multipleHeaders[InsightCorrelationHeaders.RunId] = new[] { RunId.ToString("D"), RunId.ToString("D") };
        Assert.IsFalse(InsightCorrelationContext.FromHeaders(multipleHeaders).IsValid);
    }

    [TestMethod]
    public async Task Middleware_AmbientRequestFlowsWithoutContextEchoOrTimingHeader()
    {
        var context = Context();
        var accessor = new InsightCorrelationAccessor();
        var collector = new InsightPhaseTimingCollector();
        var nextCalled = false;
        var middleware = new InsightsCorrelationMiddleware(httpContext =>
        {
            nextCalled = true;
            Assert.IsNull(accessor.Current);
            collector.Record(
                InsightMeasurementLayers.PostgreSqlRepository,
                InsightMeasurementPhases.GraphLookup,
                1m);
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(context, accessor, collector);
        await context.Response.StartAsync();

        Assert.IsTrue(nextCalled);
        Assert.IsFalse(context.Response.Headers.ContainsKey(InsightCorrelationHeaders.RunId));
        Assert.IsFalse(context.Response.Headers.ContainsKey("Server-Timing"));
    }

    [TestMethod]
    public async Task Middleware_ValidPairFlowsEchoesAndPublishesOnlyCompletedServerTimings()
    {
        var context = Context();
        foreach (var header in ValidHeaders()) context.Request.Headers[header.Key] = header.Value;
        var accessor = new InsightCorrelationAccessor();
        var collector = new InsightPhaseTimingCollector();
        var middleware = new InsightsCorrelationMiddleware(httpContext =>
        {
            Assert.AreEqual(RunId, accessor.Current?.RunId);
            Assert.AreEqual(SampleId, accessor.Current?.SampleId);
            collector.Record(
                InsightMeasurementLayers.PostgreSqlRepository,
                InsightMeasurementPhases.GraphLookup,
                2.5m);
            collector.Record(
                InsightMeasurementLayers.Transport,
                InsightMeasurementPhases.TimeToFirstByte,
                99m);
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(context, accessor, collector);
        await context.Response.StartAsync();

        Assert.IsNull(accessor.Current);
        Assert.AreEqual(RunId.ToString("D"), context.Response.Headers[InsightCorrelationHeaders.RunId].ToString());
        Assert.AreEqual(SampleId.ToString("D"), context.Response.Headers[InsightCorrelationHeaders.SampleId].ToString());
        var serverTiming = context.Response.Headers["Server-Timing"].ToString();
        StringAssert.Contains(serverTiming, "postgresql-repository.graph-lookup;dur=2.5");
        Assert.IsFalse(serverTiming.Contains("time-to-first-byte", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task Middleware_InvalidPairReturnsStructuredValidationErrorWithoutCallingNext()
    {
        var context = Context();
        context.Request.Headers[InsightCorrelationHeaders.RunId] = RunId.ToString("D");
        var accessor = new InsightCorrelationAccessor();
        var collector = new InsightPhaseTimingCollector();
        var middleware = new InsightsCorrelationMiddleware(_ =>
            throw new AssertFailedException("Next middleware must not run."));

        await middleware.InvokeAsync(context, accessor, collector);

        Assert.AreEqual(StatusCodes.Status400BadRequest, context.Response.StatusCode);
        context.Response.Body.Position = 0;
        using var document = await JsonDocument.ParseAsync(context.Response.Body);
        Assert.AreEqual(400, document.RootElement.GetProperty("status").GetInt32());
        Assert.AreEqual(
            "missing-correlation-header",
            document.RootElement.GetProperty("errors")[0].GetProperty("code").GetString());
        Assert.IsNull(accessor.Current);
    }

    private static HeaderDictionary ValidHeaders() => new()
    {
        [InsightCorrelationHeaders.RunId] = RunId.ToString("D"),
        [InsightCorrelationHeaders.SampleId] = SampleId.ToString("D")
    };

    private static DefaultHttpContext Context()
    {
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        return context;
    }
}
