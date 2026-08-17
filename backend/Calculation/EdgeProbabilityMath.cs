using Backend.Models.Domain;

namespace Backend.Calculation;

/// <summary>
/// Derives likelihood-ratio quantities from an edge's two conditional probabilities.
/// </summary>
internal static class EdgeProbabilityMath
{
    /// <summary>Returns P(child|parent) / P(child|not parent).</summary>
    public static decimal GetLikelihoodRatio(GraphEdge edge)
    {
        Validate(edge.Id, edge.ProbabilityGivenParent, edge.ProbabilityGivenNotParent);
        return edge.ProbabilityGivenParent / edge.ProbabilityGivenNotParent;
    }

    /// <summary>Returns P(child|parent) / P(child|not parent) for calculation state.</summary>
    public static decimal GetLikelihoodRatio(GraphEdgeCalcState edge)
    {
        Validate(edge.Id, edge.ProbabilityGivenParent, edge.ProbabilityGivenNotParent);
        return edge.ProbabilityGivenParent / edge.ProbabilityGivenNotParent;
    }

    /// <summary>Returns log P(child|parent) - log P(child|not parent).</summary>
    public static decimal GetLogLikelihoodRatio(GraphEdgeCalcState edge)
    {
        Validate(edge.Id, edge.ProbabilityGivenParent, edge.ProbabilityGivenNotParent);
        return (decimal)(
            Math.Log((double)edge.ProbabilityGivenParent) -
            Math.Log((double)edge.ProbabilityGivenNotParent));
    }

    /// <summary>Rejects probabilities that cannot define a finite positive likelihood ratio.</summary>
    private static void Validate(string edgeId, decimal givenParent, decimal givenNotParent)
    {
        if (givenParent <= 0m || givenParent > 1m ||
            givenNotParent <= 0m || givenNotParent > 1m)
        {
            throw new InvalidOperationException(
                $"Edge '{edgeId}' must have both conditional probabilities in the range (0, 1].");
        }
    }
}
