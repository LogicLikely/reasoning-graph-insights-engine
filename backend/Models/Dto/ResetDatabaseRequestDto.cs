namespace Backend.Models.Dto;

public sealed class ResetDatabaseRequestDto
{
    public List<string>? StressGraphIds { get; set; } = [];
}
