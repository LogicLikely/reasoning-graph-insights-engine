namespace Backend.Models.Dto;

public class EvidenceImpactRankingDto
{
    public List<string> SupportingEvidenceNodeIds { get; set; } = [];
    public List<string> CounterEvidenceNodeIds { get; set; } = [];
}
