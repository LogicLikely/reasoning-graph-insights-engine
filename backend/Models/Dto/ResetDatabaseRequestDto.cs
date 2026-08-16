namespace Backend.Models.Dto;

public sealed class ResetDatabaseRequestDto
{
    public List<string>? StressGraphIds { get; set; } = [];

    public string? ExpectedDatabaseName { get; set; }

    public string? ExpectedDatabaseFingerprint { get; set; }
}
