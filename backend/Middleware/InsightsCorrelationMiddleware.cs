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

                var publication = new ServerTimingPublication(
                    httpContext.Response,
                    timingCollector);

                if (httpContext.Response.SupportsTrailers())
                {
                    httpContext.Response.DeclareTrailer(ServerTimingHeader);
                }

                httpContext.Response.OnStarting(static state =>
                {
                    var publication = (ServerTimingPublication)state;
                    publication.WriteHeader();
                    return Task.CompletedTask;
                }, publication);

                await _next(httpContext);

                // In-memory hosts and endpoints without a body may not start
                // the response before returning. Ordinary Kestrel responses
                // publish late phases (notably MVC serialization) as a trailer
                // so measuring them does not require response buffering. The
                // controlled REST harness uses HTTP/2 because local HTTP/1.1
                // Kestrel responses do not advertise trailer support; on an
                // unsupported protocol the scoped collector still retains the
                // measurement, but it cannot be added after headers are sent.
                if (!httpContext.Response.HasStarted)
                {
                    publication.WriteHeader();
                }
                else
                {
                    publication.WriteTrailer();
                }

                return;
            }

            await _next(httpContext);
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

    private sealed class ServerTimingPublication
    {
        private readonly HttpResponse _response;
        private readonly IInsightPhaseTimingCollector _collector;
        private readonly HashSet<long> _publishedSequences = [];

        public ServerTimingPublication(
            HttpResponse response,
            IInsightPhaseTimingCollector collector)
        {
            _response = response;
            _collector = collector;
        }

        public void WriteHeader()
        {
            var timings = UnpublishedTimings();
            if (timings.Length == 0)
            {
                return;
            }

            _response.Headers[ServerTimingHeader] = string.Join(
                ", ",
                timings.Select(ToServerTimingValue));
            MarkPublished(timings);
        }

        public void WriteTrailer()
        {
            if (!_response.SupportsTrailers())
            {
                return;
            }

            var timings = UnpublishedTimings();
            if (timings.Length == 0)
            {
                return;
            }

            _response.AppendTrailer(
                ServerTimingHeader,
                string.Join(", ", timings.Select(ToServerTimingValue)));
            MarkPublished(timings);
        }

        private InsightPhaseTimingRecord[] UnpublishedTimings() =>
            _collector.Snapshot()
                .Where(IsServerTimingEligible)
                .Where(timing => !_publishedSequences.Contains(timing.Sequence))
                .ToArray();

        private void MarkPublished(IEnumerable<InsightPhaseTimingRecord> timings)
        {
            foreach (var timing in timings)
            {
                _publishedSequences.Add(timing.Sequence);
            }
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
