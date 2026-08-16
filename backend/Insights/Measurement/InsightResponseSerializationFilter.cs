using Microsoft.AspNetCore.Mvc.Filters;

namespace Backend.Insights.Measurement;

/// <summary>
/// Measures MVC result execution, including the configured output formatter,
/// without replacing or pre-serializing the ordinary API response.
/// </summary>
public sealed class InsightResponseSerializationFilter : IAsyncResultFilter
{
    private readonly IInsightPhaseTimingCollector _phaseTimings;

    public InsightResponseSerializationFilter(IInsightPhaseTimingCollector phaseTimings)
    {
        ArgumentNullException.ThrowIfNull(phaseTimings);
        _phaseTimings = phaseTimings;
    }

    public async Task OnResultExecutionAsync(
        ResultExecutingContext context,
        ResultExecutionDelegate next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);

        using (_phaseTimings.Measure(
                   InsightMeasurementLayers.BackendServiceApi,
                   InsightMeasurementPhases.Serialization))
        {
            await next();
        }
    }
}
