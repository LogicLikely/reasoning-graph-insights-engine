namespace Backend.Models.Dto;

public class GraphDto
{
    public string Slug { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public string? Description { get; set; }

    public List<GraphNodeDto> Nodes { get; set; } = new();

    public List<GraphEdgeDto> Edges { get; set; } = new();
}
