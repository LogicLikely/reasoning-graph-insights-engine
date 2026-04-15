namespace Backend.Models.Dto;

public class GraphEdgeDto
{
    public Guid Id { get; set; }

    public Guid SourceNodeId { get; set; }

    public Guid TargetNodeId { get; set; }
}
