using System.ComponentModel.DataAnnotations;
using Backend.Models.Dto;
using Backend.Reporting;
using Backend.Services;
using Backend.Seeding;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Backend.Controllers;

[ApiController]
[Route("api/graphs")]
public class GraphsController : ControllerBase
{
    private readonly IGraphService _graphService;

    public GraphsController(IGraphService graphService)
    {
        _graphService = graphService;
    }

    [HttpGet]
    public async Task<IActionResult> GetSummaries(CancellationToken cancellationToken)
    {
        var summaries = await _graphService.GetSummariesAsync(cancellationToken);

        return Ok(summaries);
    }

    [HttpGet("{slug}")]
    public async Task<IActionResult> GetBySlug(
        string slug,
        CancellationToken cancellationToken)
    {
        var graph = await _graphService.GetBySlugAsync(slug, cancellationToken);

        if (graph is null)
        {
            return NotFound();
        }

        return Ok(graph);
    }

    [HttpPost("reset")]
    public async Task<IActionResult> ResetDatabase(
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] ResetDatabaseRequestDto? request,
        CancellationToken cancellationToken)
    {
        try
        {
            await _graphService.ResetDatabaseAsync(
                request?.StressGraphIds ?? [],
                cancellationToken);
        }
        catch (InvalidStressGraphSeedSelectionException exception)
        {
            return BadRequest(new
            {
                message = exception.Message,
                unknownStressGraphIds = exception.UnknownIds
            });
        }

        return NoContent();
    }

    [HttpPost("{slug}/nodes/{targetNodeId}/minimal-counter-set")]
    public async Task<IActionResult> GetMinimalCounterSet(
        string slug,
        string targetNodeId,
        [FromBody] GraphDto? graphContext,
        CancellationToken cancellationToken,
        [FromHeader(Name = PerformanceReportingHeaders.BenchmarkSetId)]
        string? benchmarkSetId = null)
    {
        var counterNodeIds = graphContext is null
            ? await _graphService.GetMinimalCounterSetAsync(
                slug,
                targetNodeId,
                benchmarkSetId,
                cancellationToken)
            : await _graphService.GetMinimalCounterSetAsync(
                slug,
                targetNodeId,
                graphContext,
                benchmarkSetId,
                cancellationToken);

        return Ok(new { counterNodeIds });
    }

    [HttpPost("{slug}/nodes/{targetNodeId}/bounded-minimal-counter-set")]
    public async Task<IActionResult> GetBoundedMinimalCounterSet(
        string slug,
        string targetNodeId,
        [FromBody] GraphDto? graphContext,
        CancellationToken cancellationToken,
        [FromHeader(Name = PerformanceReportingHeaders.BenchmarkSetId)]
        string? benchmarkSetId = null)
    {
        var result = graphContext is null
            ? await _graphService.GetBoundedMinimalCounterSetAsync(
                slug,
                targetNodeId,
                benchmarkSetId,
                cancellationToken)
            : await _graphService.GetBoundedMinimalCounterSetAsync(
                slug,
                targetNodeId,
                graphContext,
                benchmarkSetId,
                cancellationToken);

        return result is null ? NotFound() : Ok(result);
    }

    [HttpPost("{slug}/nodes/{targetNodeId}/evidence-impact-ranking")]
    public async Task<IActionResult> GetEvidenceImpactRanking(
        string slug,
        string targetNodeId,
        [FromBody] GraphDto? graphContext,
        CancellationToken cancellationToken,
        [FromHeader(Name = PerformanceReportingHeaders.BenchmarkSetId)]
        string? benchmarkSetId = null)
    {
        var ranking = graphContext is null
            ? await _graphService.GetEvidenceImpactRankingAsync(
                slug,
                targetNodeId,
                benchmarkSetId,
                cancellationToken)
            : await _graphService.GetEvidenceImpactRankingAsync(
                slug,
                targetNodeId,
                graphContext,
                benchmarkSetId,
                cancellationToken);

        if (ranking is null)
        {
            return NotFound();
        }

        return Ok(ranking);
    }

    [HttpPost("{slug}/least-robust-node")]
    public async Task<IActionResult> GetLeastRobustNode(
        string slug,
        [FromBody] GraphDto? graphContext,
        CancellationToken cancellationToken,
        [FromHeader(Name = PerformanceReportingHeaders.BenchmarkSetId)]
        string? benchmarkSetId = null)
    {
        var result = graphContext is null
            ? await _graphService.GetLeastRobustNodeAsync(
                slug,
                benchmarkSetId,
                cancellationToken)
            : await _graphService.GetLeastRobustNodeAsync(
                slug,
                graphContext,
                benchmarkSetId,
                cancellationToken);

        return result is null ? NotFound() : Ok(result);
    }

    [HttpPost("{slug}/node-robustness-ranking")]
    public async Task<IActionResult> GetNodeRobustnessRanking(
        string slug,
        [FromBody] GraphDto? graphContext,
        CancellationToken cancellationToken,
        [FromHeader(Name = PerformanceReportingHeaders.BenchmarkSetId)]
        string? benchmarkSetId = null)
    {
        var result = graphContext is null
            ? await _graphService.GetNodeRobustnessRankingAsync(
                slug,
                benchmarkSetId,
                cancellationToken)
            : await _graphService.GetNodeRobustnessRankingAsync(
                slug,
                graphContext,
                benchmarkSetId,
                cancellationToken);

        return result is null ? NotFound() : Ok(result);
    }

    [HttpDelete("{slug}/nodes/{nodeId}")]
    public async Task<IActionResult> DeleteNode(
        string slug,
        string nodeId,
        CancellationToken cancellationToken)
    {
        Console.WriteLine($"Deleting node {nodeId} from graph {slug}");
        var deleted = await _graphService.DeleteNodeAsync(slug, nodeId, cancellationToken);

        if (!deleted)
        {
            Console.WriteLine($"Failed to delete node {nodeId} from graph {slug}");
            return NotFound(); // Return 404 if the node or graph was not found
        }
        return NoContent(); // Return 204 No Content for a successful deletion
    }
    [HttpPost("{slug}/nodes")]
    public async Task<IActionResult> AddNode(
        string slug,
        [FromBody] GraphNodeDto nodeDto,
        CancellationToken cancellationToken,
        [FromQuery] string? parentID = null,
        [FromQuery] string edgeKind = "support",
        [FromQuery, Range(typeof(decimal), "0.000000001", "1")] decimal probabilityGivenParent = 0.5m,
        [FromQuery, Range(typeof(decimal), "0.000000001", "1")] decimal probabilityGivenNotParent = 0.5m)
    {
        Console.WriteLine($"Adding node to graph {slug}");
        var success = await _graphService.AddNodeAsync(
            slug,
            nodeDto,
            parentID,
            edgeKind,
            probabilityGivenParent,
            probabilityGivenNotParent,
            cancellationToken);

        if (!success)
        {
            Console.WriteLine($"Failed to add node to graph {slug}");
            return NotFound();
        }

        return CreatedAtAction(nameof(GetBySlug), new { slug }, nodeDto);
    }

    [HttpPost("{slug}/edges")]
    public async Task<IActionResult> AddEdge(
        string slug,
        [FromBody] GraphEdgeDto edgeDto,
        CancellationToken cancellationToken)
    {
        Console.WriteLine($"Adding edge to graph {slug}");
        var success = await _graphService.AddEdgeAsync(slug, edgeDto, cancellationToken);

        if (!success)
        {
            Console.WriteLine($"Failed to add edge to graph {slug}");
            return NotFound();
        }

        return CreatedAtAction(nameof(GetBySlug), new { slug }, edgeDto);
    }

    [HttpPatch("{slug}/edges/{edgeId}")]
    public async Task<IActionResult> UpdateEdge(
        string slug,
        string edgeId,
        [FromBody] GraphEdgeUpdateDto edgeDto,
        CancellationToken cancellationToken)
    {
        Console.WriteLine($"Updating edge {edgeId} in graph {slug}");
        var success = await _graphService.UpdateEdgeAsync(slug, edgeId, edgeDto, cancellationToken);

        if (!success)
        {
            Console.WriteLine($"Failed to update edge {edgeId} in graph {slug}");
            return NotFound();
        }

        return NoContent();
    }

    [HttpPatch("{slug}/nodes/{nodeId}")]
    public async Task<IActionResult> UpdateNode(
        string slug,
        string nodeId,
        [FromBody] GraphNodeUpdateDto nodeDto,
        CancellationToken cancellationToken,
        [FromHeader(Name = PerformanceReportingHeaders.BenchmarkSetId)]
        string? benchmarkSetId = null)
    {
        Console.WriteLine($"Updating node {nodeId} in graph {slug}");
        var success = await _graphService.UpdateNodeAsync(
            slug,
            nodeId,
            nodeDto,
            benchmarkSetId,
            cancellationToken);

        if (!success)
        {
            Console.WriteLine($"Failed to update node {nodeId} in graph {slug}");
            return NotFound();
        }

        return NoContent();
    }
}
