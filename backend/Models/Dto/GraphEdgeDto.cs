using System.ComponentModel.DataAnnotations;

namespace Backend.Models.Dto;

public class GraphEdgeDto
{
    public string Id { get; set; } = string.Empty;

    public string From { get; set; } = string.Empty;

    public string To { get; set; } = string.Empty;

    public string Kind { get; set; } = string.Empty;

    [Range(typeof(decimal), "0.000000001", "1")]
    public decimal ProbabilityGivenParent { get; set; } = 0.5m;

    [Range(typeof(decimal), "0.000000001", "1")]
    public decimal ProbabilityGivenNotParent { get; set; } = 0.5m;
}
