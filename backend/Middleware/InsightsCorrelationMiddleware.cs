using System.Globalization;
using Backend.Insights.Contracts;
using Backend.Insights.Measurement;

namespace Backend.Middleware;

public sealed class InsightsCorrelationMiddleware
{
    private const string ServerTimingHeader = "Server-Timing";
    private readonly RequestDelegate _next;

    public InsightsCorrelationMiddleware(RequestDelegate next)
    {
        ArgumentNullException.ThrowIfNull(next);
        _next = next;
    }

    public async Task InvokeAsync(
        HttpContext httpContext,
        IInsightCorrelationAccessor correlationAccessor,
        IInsightPhaseTimingCollector timingCollector)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        ArgumentNullException.ThrowIfNull(correlationAccessor);
        ArgumentNullException.ThrowIfNull(timingCollector);

        var validation = InsightCorrelationContext.FromHeaders(httpContext.Request.Headers);
        if (!validation.IsValid)
        {
            await WriteValidationFailureAsync(httpContext, validation.Failures);
            return;
        }

        var previous = correlationAccessor.Current;
        correlationAccessor.Current = validation.Context;

        try
        {
            if (validation.Context is not null)
            {
                httpContext.Response.Headers[InsightCorrelationHeaders.RunId] =
                    validation.Context.RunId.ToString("D");
                httpContext.Response.Headers[InsightCorrelationHeaders.SampleId] =
                    validation.Context.SampleId.ToString("D");

                httpContext.Response.OnStarting(static state =>
                {
                    var (response, collector) =
                        ((HttpResponse Response, IInsightPhaseTimingCollector Collector))state;
                    WriteServerTimingHeader(response, collector);
                    return Task.CompletedTask;
                }, (httpContext.Response, timingCollector));
            }

            await _next(httpContext);

            // In-memory hosts and endpoints without a body may not start the
            // response before returning. Publish the same snapshot while the
            // headers remain mutable; OnStarting covers ordinary body writes.
            if (validation.Context is not null && !httpContext.Response.HasStarted)
            {
                WriteServerTimingHeader(httpContext.Response, timingCollector);
            }
        }
        finally
        {
            correlationAccessor.Current = previous;
        }
    }

    private static bool IsServerTimingEligible(InsightPhaseTimingRecord timing)
    {
        return InsightPhaseRegistry.TryGetDefinition(timing.Layer, timing.Phase, out var definition) &&
               definition!.ServerSideMeasurable;
    }

    private static string ToServerTimingValue(InsightPhaseTimingRecord timing)
    {
        var duration = timing.Duration.ToString(
            "0.############################",
            CultureInfo.InvariantCulture);
        return $"{timing.Layer}.{timing.Phase};dur={duration}";
    }

    private static void WriteServerTimingHeader(
        HttpResponse response,
        IInsightPhaseTimingCollector collector)
    {
        var values = collector.Snapshot()
            .Where(IsServerTimingEligible)
            .Select(ToServerTimingValue)
            .ToArray();

        if (values.Length > 0)
        {
            response.Headers[ServerTimingHeader] = string.Join(", ", values);
        }
    }

    private static async Task WriteValidationFailureAsync(
        HttpContext httpContext,
        IReadOnlyList<ValidationFailure> failures)
    {
        httpContext.Response.StatusCode = StatusCodes.Status400BadRequest;
        httpContext.Response.ContentType = "application/problem+json; charset=utf-8";
        var body = CanonicalJson.Canonicalize(new
        {
            type = "https://logiclikely.dev/problems/invalid-insights-correlation",
            title = "Invalid Insights correlation headers.",
            status = StatusCodes.Status400BadRequest,
            errors = failures
        });
        await httpContext.Response.WriteAsync(body, httpContext.RequestAborted);
    }
}
