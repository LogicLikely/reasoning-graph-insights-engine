namespace Backend.Models.Domain;

public class GraphNode
{
    public string Id { get; set; } = string.Empty;

    public string Kind { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public string BodyText { get; set; } = string.Empty;
    public string? Category { get; set; }
    public List<string> Tags { get; set; } = new();
    public decimal LogOdds { get; set; }
    public GraphEvidenceDetails? Evidence { get; set; }
}
