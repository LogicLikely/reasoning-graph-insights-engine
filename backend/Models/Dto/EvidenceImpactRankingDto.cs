namespace Backend.Models.Dto;

public class EvidenceImpactRankingDto
{
    public List<EvidenceImpactDto> SupportingEvidence { get; set; } = [];
    public List<EvidenceImpactDto> CounterEvidence { get; set; } = [];
}

public class EvidenceImpactDto
{
    public string NodeId { get; set; } = string.Empty;
    public decimal LogLr { get; set; }
    public double ProbabilityDifference { get; set; }
}
