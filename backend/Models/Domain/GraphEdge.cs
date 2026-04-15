namespace Backend.Models.Domain;

public class GraphEdge
{
    public Guid Id { get; set; }

    public Guid SourceNodeId { get; set; }

    public Guid TargetNodeId { get; set; }
}
