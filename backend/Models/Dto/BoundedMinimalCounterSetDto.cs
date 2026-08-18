namespace Backend.Models.Dto;

public sealed class BoundedMinimalCounterSetDto
{
    public List<string>? CounterNodeIds { get; set; }

    public string ProofStatus { get; set; } = string.Empty;

    public string Status { get; set; } = string.Empty;

    public string StopReason { get; set; } = string.Empty;

    public double TimeBudgetMilliseconds { get; set; }

    public long RunNumber { get; set; }
}
