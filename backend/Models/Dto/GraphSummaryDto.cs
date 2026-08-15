namespace Backend.Models.Dto;

public class GraphSummaryDto
{
    public string Slug { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public string? Description { get; set; }

    public int NodeCount { get; set; }

    public int EdgeCount { get; set; }
}
