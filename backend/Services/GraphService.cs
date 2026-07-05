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
                    Tags = node.Tags.ToList(),
                    LogOdds = node.LogOdds,
                    Evidence = node.Evidence == null ? null : new GraphEvidenceDto
                    {
                        Type = node.Evidence.Type,
                        Score = node.Evidence.Score,
                        Rationale = node.Evidence.Rationale
                    }
                })
                .ToList(),
            Edges = graph.Edges
                .Select(edge => new GraphEdgeDto
                {
                    Id = edge.Id,
                    From = edge.From,
                    To = edge.To,
                    Kind = edge.Kind,
                    ImportanceToParent = edge.ImportanceToParent
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
        string edgeKind = "support",
        int importanceToParent = 1,
        CancellationToken cancellationToken = default)
    {
        return await _graphRepository.AddNodeAsync(slug, node, parentID, edgeKind, importanceToParent, cancellationToken);
    }

    public async Task<bool> UpdateNodeAsync(
        string slug,
        string nodeId,
        GraphNodeUpdateDto node,
        CancellationToken cancellationToken = default)
    {
        return await _graphRepository.UpdateNodeAsync(slug, nodeId, node, cancellationToken);
    }

    public async Task<bool> AddEdgeAsync(
        string slug,
        GraphEdgeDto edge,
        CancellationToken cancellationToken = default)
    {
        return await _graphRepository.AddEdgeAsync(slug, edge, cancellationToken);
    }

    public async Task<bool> UpdateEdgeAsync(
        string slug,
        string edgeId,
        GraphEdgeUpdateDto edge,
        CancellationToken cancellationToken = default)
    {
        return await _graphRepository.UpdateEdgeAsync(slug, edgeId, edge, cancellationToken);
    }

    public async Task ResetDatabaseAsync(CancellationToken cancellationToken = default)
    {
        await _graphRepository.ResetDatabaseAsync(cancellationToken);
    }
}
