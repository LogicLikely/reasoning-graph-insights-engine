using System.ComponentModel.DataAnnotations;

namespace Backend.Models.Dto;

public class GraphEdgeUpdateDto
{
    [Range(typeof(decimal), "0.000000001", "1")]
    public decimal? ProbabilityGivenParent { get; set; }

    [Range(typeof(decimal), "0.000000001", "1")]
    public decimal? ProbabilityGivenNotParent { get; set; }
}
