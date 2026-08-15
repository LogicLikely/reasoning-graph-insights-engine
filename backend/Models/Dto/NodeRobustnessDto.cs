namespace Backend.Models.Dto;

public class NodeRobustnessDto
{
    public string NodeId { get; set; } = string.Empty;
    public string NodeTitle { get; set; } = string.Empty;
    public decimal Robustness { get; set; }
}
