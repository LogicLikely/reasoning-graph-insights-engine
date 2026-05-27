namespace Backend.Models.Dto;

public class GraphNodeDto
{
    public string Id { get; set; } = string.Empty;

    public string Kind { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public string BodyText { get; set; } = string.Empty;

    public string? Category { get; set; }

    public List<string> Tags { get; set; } = new();

    public decimal? Prior { get; set; }

    public decimal? Weight { get; set; }

    public decimal? Confidence { get; set; }

    public decimal? Importance { get; set; }

    public GraphEvidenceDto? Evidence { get; set; }
}
public class GraphEvidenceDto
{
    public string Type { get; set; } = string.Empty;

    public decimal Score { get; set; }

    public string Rationale { get; set; } = string.Empty;
}
