namespace Backend.Models.Dto;

public class GraphDto
{
    public Guid Id { get; set; }

    public List<GraphNodeDto> Nodes { get; set; } = new();

    public List<GraphEdgeDto> Edges { get; set; } = new();
}
