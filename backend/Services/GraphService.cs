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
                    Prior = node.Prior,
                    Confidence = node.Confidence,
                    Weight = node.Weight,
                    Importance = node.Importance,
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
                    Kind = edge.Kind
                })
                .ToList()
        };
    }
}
