namespace Backend.Models.Domain;

public class GraphNode
{
    public string Id { get; set; } = string.Empty;

    public string Kind { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public string BodyText { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string Tags { get; set; } = string.Empty;
    public int Prior { get; set; } = 0;
    public int Confidence { get; set; } = 0;
    public int Weight { get; set; } = 0;
    public int Importance { get; set; } = 0;
    public string Evidence { get; set; } = string.Empty;
}
