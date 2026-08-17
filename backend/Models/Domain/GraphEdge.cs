namespace Backend.Models.Domain;

public class GraphEdge
{
    public string Id { get; set; } = string.Empty;

    public string From { get; set; } = string.Empty;

    public string To { get; set; } = string.Empty;

    public string Kind { get; set; } = string.Empty;

    public decimal ProbabilityGivenParent { get; set; } = 0.5m;

    public decimal ProbabilityGivenNotParent { get; set; } = 0.5m;
}
