using System.ComponentModel.DataAnnotations;

namespace Backend.Models.Dto;

public class GraphEdgeUpdateDto
{
    public decimal? ImportanceToParent { get; set; }

    [Range(typeof(decimal), "0", "1")]
    public decimal? ProbabilityGivenParent { get; set; }

    [Range(typeof(decimal), "0", "1")]
    public decimal? ProbabilityGivenNotParent { get; set; }
}
