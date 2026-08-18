namespace Backend.Models.Dto;

public class EvidenceImpactRankingDto
{
    public List<EvidenceImpactDto> SupportingEvidence { get; set; } = [];
    public List<EvidenceImpactDto> CounterEvidence { get; set; } = [];
}

public class EvidenceImpactDto
{
    public string NodeId { get; set; } = string.Empty;

    // Kept for API compatibility. This is the evidence node's marginal target
    // log-odds impact after a leave-one-out Bayes-factor recalculation.
    public decimal LogLr { get; set; }
    public double ProbabilityDifference { get; set; }
}
