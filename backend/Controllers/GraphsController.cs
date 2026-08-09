using Backend.Models.Dto;
using Backend.Services;
using Microsoft.AspNetCore.Mvc;

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
    public async Task<IActionResult> ResetDatabase(CancellationToken cancellationToken)
    {
        await _graphService.ResetDatabaseAsync(cancellationToken);

        return NoContent();
    }

    [HttpPost("{slug}/nodes/{targetNodeId}/minimal-counter-set")]
    public async Task<IActionResult> GetMinimalCounterSet(
        string slug,
        string targetNodeId,
        [FromBody] GraphDto? graphContext,
        CancellationToken cancellationToken)
    {
        var counterNodeIds = graphContext is null
            ? await _graphService.GetMinimalCounterSetAsync(slug, targetNodeId, cancellationToken)
            : await _graphService.GetMinimalCounterSetAsync(slug, targetNodeId, graphContext, cancellationToken);

        Console.WriteLine(
            $"Minimal counter set for node '{targetNodeId}': " +
            (counterNodeIds is null ? "null" : $"[{string.Join(", ", counterNodeIds)}]"));

        return Ok(new { counterNodeIds });
    }
    
    [HttpPost("{slug}/nodes/{targetNodeId}/evidence-impact-ranking")]
    public async Task<IActionResult> GetEvidenceImpactRanking(
        string slug,
        string targetNodeId,
        [FromBody] GraphDto? graphContext,
        CancellationToken cancellationToken)
    {
        var evidenceNodeIds = graphContext is null
            ? await _graphService.GetEvidenceImpactRankingAsync(slug, targetNodeId, cancellationToken)
            : await _graphService.GetEvidenceImpactRankingAsync(slug, targetNodeId, graphContext, cancellationToken);

        if (evidenceNodeIds is null)
        {
            return NotFound();
        }

        return Ok(new { evidenceNodeIds });
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
        [FromQuery] decimal importanceToParent = 1m)
    {
        Console.WriteLine($"Adding node to graph {slug}");
        var success = await _graphService.AddNodeAsync(slug, nodeDto, parentID, edgeKind, importanceToParent, cancellationToken);

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
        CancellationToken cancellationToken)
    {
        Console.WriteLine($"Updating node {nodeId} in graph {slug}");
        var success = await _graphService.UpdateNodeAsync(slug, nodeId, nodeDto, cancellationToken);

        if (!success)
        {
            Console.WriteLine($"Failed to update node {nodeId} in graph {slug}");
            return NotFound();
        }

        return NoContent();
    }
}
