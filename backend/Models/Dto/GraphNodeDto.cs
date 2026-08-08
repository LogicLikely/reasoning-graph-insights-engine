namespace Backend.Models.Dto;

public class GraphNodeDto
{
    public string Id { get; set; } = string.Empty;

    public string Kind { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public string BodyText { get; set; } = string.Empty;
    public string? Category { get; set; }
    public List<string> Tags { get; set; } = new();
    public decimal PriorOdds { get; set; }
    public decimal PosteriorOdds { get; set; }
    public GraphEvidenceDto? Evidence { get; set; }
}
