namespace Backend.Calculation;

public sealed class GraphNodeCalcState
{
    public required string Id { get; init; }

    public required string Kind { get; init; }

    public decimal LogOdds { get; set; }
}
