using Backend.Models.Dto;
using Backend.Repositories;

namespace Backend.Services;

public class GraphService : IGraphService
{
    private readonly IGraphRepository _graphRepository;

    public GraphService(IGraphRepository graphRepository)
    {
        _graphRepository = graphRepository;
    }

    public async Task<GraphDto?> GetBySlugAsync(
        string slug,
        CancellationToken cancellationToken = default)
    {
        var graph = await _graphRepository.GetBySlugAsync(slug, cancellationToken);

        if (graph is null)
        {
            return null;
        }

        return new GraphDto
        {
            Slug = graph.Slug,
            Title = graph.Title,
            Description = graph.Description,
            Nodes = graph.Nodes
                .Select(node => new GraphNodeDto
                {
                    Id = node.Id,
                    Kind = node.Kind,
                    Title = node.Title,
                    BodyText = node.BodyText,
                    Category = node.Category,
                    Tags = node.Tags,
                    Prior = node.Prior,
                    Weight = node.Weight,
                    Confidence = node.Confidence,
                    Importance = node.Importance,
                    Evidence = node.Evidence != null ? new GraphEvidenceDto
                    {
                        Type = node.Evidence.Type,
                        Score = node.Evidence.Score,
                        Rationale = node.Evidence.Rationale
                    } : null
                })
                .ToList(),
            Edges = graph.Edges
                .Select(edge => new GraphEdgeDto
                {
                    Id = edge.Id,
                    From = edge.From,
                    To = edge.To,
                    Kind = edge.Kind
                })
                .ToList()
        };
    }

    public async Task<bool> DeleteNodeAsync(
        string slug,
        string nodeId,
        CancellationToken cancellationToken = default)
    {
        var graph = await _graphRepository.GetBySlugAsync(slug, cancellationToken);
        if (graph is null) return false;

        // Check if node has incoming edges (IN neighbors)
        if (graph.Edges.Any(e => e.To == nodeId))
        {
            return false; // Business Rule: Cannot delete nodes that have incoming dependencies
        }

        return await _graphRepository.DeleteNodeAsync(slug, nodeId, cancellationToken);
    }

    public async Task<bool> AddNodeAsync(
        string slug,
        GraphNodeDto node,
        string? parentID = null,
        CancellationToken cancellationToken = default)
    {
        return await _graphRepository.AddNodeAsync(slug, node, parentID, cancellationToken);
    }
}
