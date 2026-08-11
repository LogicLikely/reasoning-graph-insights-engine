namespace Backend.Models.Dto;

public class GraphEdgeDto
{
    public string Id { get; set; } = string.Empty;

    public string From { get; set; } = string.Empty;

    public string To { get; set; } = string.Empty;

    public string Kind { get; set; } = string.Empty;

    public decimal ImportanceToParent { get; set; } = 1m;
}
