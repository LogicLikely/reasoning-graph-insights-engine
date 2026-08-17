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

    [HttpPost("benchmark-sets")]
    public async Task<IActionResult> CreateBenchmarkSet(
        [FromBody] CreatePerformanceBenchmarkSetRequest? request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request?.Name))
        {
            return BadRequest(new { message = "A benchmark set name is required." });
        }

        var benchmarkSet = await _performanceRunStore.CreateBenchmarkSetAsync(
            request.Name,
            cancellationToken);
        return StatusCode(StatusCodes.Status201Created, benchmarkSet);
    }
}

public sealed record CreatePerformanceBenchmarkSetRequest
{
    public string? Name { get; init; }
}
