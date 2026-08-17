using Backend.Reporting;
using Microsoft.AspNetCore.Mvc;

namespace Backend.Controllers;

[ApiController]
[Route("api/performance-runs")]
public sealed class PerformanceRunsController : ControllerBase
{
    private readonly IPerformanceRunStore _performanceRunStore;

    public PerformanceRunsController(IPerformanceRunStore performanceRunStore)
    {
        _performanceRunStore = performanceRunStore;
    }

    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken cancellationToken)
    {
        var report = await _performanceRunStore.ReadAsync(cancellationToken);
        return Ok(report);
    }
}
