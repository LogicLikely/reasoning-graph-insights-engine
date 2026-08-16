using Backend.Insights.Measurement;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;

namespace backend.Tests.Insights.Measurement;

[TestClass]
public sealed class InsightResponseSerializationFilterTests
{
    [TestMethod]
    public async Task ResultExecution_RecordsSerializationEvenWhenFormatterFails()
    {
        var collector = new InsightPhaseTimingCollector();
        var filter = new InsightResponseSerializationFilter(collector);
        var actionContext = new ActionContext(
            new DefaultHttpContext(),
            new RouteData(),
            new ActionDescriptor());
        var filters = Array.Empty<IFilterMetadata>();
        var result = new OkObjectResult(new { value = 1 });
        var executing = new ResultExecutingContext(
            actionContext,
            filters,
            result,
            new object());

        await Assert.ThrowsExceptionAsync<InvalidOperationException>(() =>
            filter.OnResultExecutionAsync(
                executing,
                () => throw new InvalidOperationException("formatter failed")));

        var timing = collector.Snapshot().Single();
        Assert.AreEqual(InsightMeasurementLayers.BackendServiceApi, timing.Layer);
        Assert.AreEqual(InsightMeasurementPhases.Serialization, timing.Phase);
        Assert.AreEqual(
            Backend.Insights.Contracts.TimingBoundaryProvenance.DirectlyInstrumented,
            timing.TimingBoundaryProvenance);
    }
}
