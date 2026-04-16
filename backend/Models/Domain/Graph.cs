namespace Backend.Models.Domain;

public class Graph
{
    public int Id { get; set; }

    public string Slug { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public string? Description { get; set; }

    public List<GraphNode> Nodes { get; set; } = new();

    public List<GraphEdge> Edges { get; set; } = new();
}
