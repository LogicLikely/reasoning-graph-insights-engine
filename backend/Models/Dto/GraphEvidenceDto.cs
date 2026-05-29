namespace Backend.Models.Dto;

public class GraphEvidenceDto
{
    public string Type { get; set; } = string.Empty;
    public decimal? Score { get; set; }
    public string? Rationale { get; set; }
}