namespace Backend.Models.Domain;

public class Graph
{
    public Guid Id { get; set; }

    public List<GraphNode> Nodes { get; set; } = new();

    public List<GraphEdge> Edges { get; set; } = new();
}
