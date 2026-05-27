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
}
