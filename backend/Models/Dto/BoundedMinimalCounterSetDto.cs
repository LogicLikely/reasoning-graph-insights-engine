namespace Backend.Models.Dto;

public sealed class BoundedMinimalCounterSetDto
{
    public List<string>? CounterNodeIds { get; set; }

    public string ProofStatus { get; set; } = string.Empty;

    public long RunNumber { get; set; }
}
