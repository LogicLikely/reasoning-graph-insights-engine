namespace Backend.Calculation;

public sealed record GraphEdgeCalcState(
    string Id,
    string FromNodeId,
    string ToNodeId,
    string Kind,
    int ImportanceToParent);
