using Backend.Models.Domain;

namespace Backend.Insights.Contracts;

public sealed record RobustnessV0Vector(
    decimal StoredPosteriorLogOdds,
    double OriginalProbability,
    double HypotheticalProbability,
    double AbsoluteProbabilityDelta,
    decimal AccumulatedPathLogLikelihoodRatio,
    decimal? AccumulatedPathLikelihoodRatio,
    decimal RobustnessScore);

public sealed record RobustnessV0RankedNode(string NodeId, decimal RobustnessScore);

public sealed record RobustnessV0PathProjection(
    IReadOnlyList<string> NodeIds,
    IReadOnlyList<string> EdgeIds,
    decimal AccumulatedPathLogLikelihoodRatio);

public sealed class RobustnessV0RankingComparer : IComparer<RobustnessV0RankedNode>
{
    public static RobustnessV0RankingComparer Instance { get; } = new();

    private RobustnessV0RankingComparer()
    {
    }

    public int Compare(RobustnessV0RankedNode? left, RobustnessV0RankedNode? right)
    {
        if (ReferenceEquals(left, right))
        {
            return 0;
        }

        if (left is null)
        {
            return 1;
        }

        if (right is null)
        {
            return -1;
        }

        var scoreComparison = left.RobustnessScore.CompareTo(right.RobustnessScore);
        return scoreComparison != 0
            ? scoreComparison
            : StringComparer.Ordinal.Compare(left.NodeId, right.NodeId);
    }
}

public static class RobustnessV0Contract
{
    public const string SemanticVersion = AlgorithmSemanticIdentities.RobustnessV0;
    public const bool RanksAllNodeKinds = true;
    public const bool AllowsAllStructuralLeafKinds = true;
    public const bool IncludesAllEdgeKinds = true;
    public const bool IncludesLeafEvidenceContribution = false;
    public const bool UsesStoredPosteriorLogOdds = true;
    public const bool RequiresDirectedAcyclicGraph = true;
    public const bool CurrentImplementationUsesRecursion = true;
    public const bool CurrentImplementationHasDeepGraphStackRisk = true;
    public const decimal TheoreticalMaximumScore = 1m;

    public static decimal TheoreticalMinimumScore => (decimal)Math.Exp(-1d);

    public static AlgorithmGraphContractValidationResult ValidateGraph(Graph graph) =>
        AlgorithmGraphContractValidation.ValidateDirectedAcyclicGraph(graph);

    public static bool IsRankableNode(GraphNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        return true;
    }

    public static bool IsStructuralLeaf(Graph graph, string nodeId)
    {
        EnsureValidGraphAndNode(graph, nodeId);

        return !graph.Edges.Any(edge =>
            string.Equals(edge.To, nodeId, StringComparison.Ordinal));
    }

    public static decimal AccumulateEdgeLogLikelihoodRatio(IEnumerable<GraphEdge> orderedPathEdges)
    {
        ArgumentNullException.ThrowIfNull(orderedPathEdges);

        decimal accumulatedLogLikelihoodRatio = 0m;
        foreach (var edge in orderedPathEdges)
        {
            ArgumentNullException.ThrowIfNull(edge);
            if (edge.ImportanceToParent <= 0m)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(orderedPathEdges),
                    $"Edge '{edge.Id}' has non-positive likelihood ratio '{edge.ImportanceToParent}'.");
            }

            accumulatedLogLikelihoodRatio += (decimal)Math.Log((double)edge.ImportanceToParent);
        }

        return accumulatedLogLikelihoodRatio;
    }

    public static decimal SelectMaximumAccumulatedPathLogLikelihoodRatio(
        IEnumerable<decimal> accumulatedPathLogLikelihoodRatios)
    {
        ArgumentNullException.ThrowIfNull(accumulatedPathLogLikelihoodRatios);

        using var enumerator = accumulatedPathLogLikelihoodRatios.GetEnumerator();
        if (!enumerator.MoveNext())
        {
            throw new ArgumentException(
                "At least one structural leaf-to-node path is required.",
                nameof(accumulatedPathLogLikelihoodRatios));
        }

        var maximum = enumerator.Current;
        while (enumerator.MoveNext())
        {
            maximum = Math.Max(maximum, enumerator.Current);
        }

        return maximum;
    }

    public static RobustnessV0Vector Evaluate(
        GraphNode node,
        decimal maximumAccumulatedPathLogLikelihoodRatio)
    {
        ArgumentNullException.ThrowIfNull(node);

        return Evaluate(node.PosteriorOdds, maximumAccumulatedPathLogLikelihoodRatio);
    }

    public static RobustnessV0Vector Evaluate(
        decimal storedPosteriorLogOdds,
        decimal maximumAccumulatedPathLogLikelihoodRatio)
    {
        var originalProbability = LogOddsToProbability(storedPosteriorLogOdds);
        var hypotheticalProbability = LogOddsToProbability(
            storedPosteriorLogOdds - maximumAccumulatedPathLogLikelihoodRatio);
        var absoluteProbabilityDelta = Math.Abs(originalProbability - hypotheticalProbability);
        var robustnessScore = (decimal)Math.Exp(-absoluteProbabilityDelta);
        var accumulatedPathLikelihoodRatio = TryConvertLogLikelihoodRatioToDecimal(
            maximumAccumulatedPathLogLikelihoodRatio);

        return new RobustnessV0Vector(
            storedPosteriorLogOdds,
            originalProbability,
            hypotheticalProbability,
            absoluteProbabilityDelta,
            maximumAccumulatedPathLogLikelihoodRatio,
            accumulatedPathLikelihoodRatio,
            robustnessScore);
    }

    public static RobustnessV0PathProjection SelectReportedPath(
        IEnumerable<RobustnessV0PathProjection> maximumScorePaths)
    {
        ArgumentNullException.ThrowIfNull(maximumScorePaths);

        var paths = maximumScorePaths.ToArray();
        if (paths.Length == 0)
        {
            throw new ArgumentException("At least one path is required.", nameof(maximumScorePaths));
        }

        foreach (var path in paths)
        {
            ArgumentNullException.ThrowIfNull(path);
            if (path.NodeIds is null || path.EdgeIds is null)
            {
                throw new ArgumentException("Path node and edge ID sequences cannot be null.", nameof(maximumScorePaths));
            }

            if (path.NodeIds.Count != path.EdgeIds.Count + 1)
            {
                throw new ArgumentException(
                    "A path must contain exactly one more node ID than edge IDs.",
                    nameof(maximumScorePaths));
            }
        }

        var maximumScore = paths.Max(path => path.AccumulatedPathLogLikelihoodRatio);
        return paths
            .Where(path => path.AccumulatedPathLogLikelihoodRatio == maximumScore)
            .OrderBy(path => path.NodeIds, OrdinalSequenceComparer.Instance)
            .ThenBy(path => path.EdgeIds, OrdinalSequenceComparer.Instance)
            .First();
    }

    public static IReadOnlyList<RobustnessV0RankedNode> Rank(
        IEnumerable<RobustnessV0RankedNode> results)
    {
        ArgumentNullException.ThrowIfNull(results);

        return Array.AsReadOnly(
            results
                .OrderBy(result => result, RobustnessV0RankingComparer.Instance)
                .ToArray());
    }

    public static RobustnessV0RankedNode? LeastRobust(
        IEnumerable<RobustnessV0RankedNode> results) =>
        Rank(results).FirstOrDefault();

    private static double LogOddsToProbability(decimal logOdds)
    {
        var value = (double)logOdds;
        if (value >= 0d)
        {
            var inverseOdds = Math.Exp(-value);
            return 1d / (1d + inverseOdds);
        }

        var odds = Math.Exp(value);
        return odds / (1d + odds);
    }

    private static decimal? TryConvertLogLikelihoodRatioToDecimal(decimal logLikelihoodRatio)
    {
        var likelihoodRatio = Math.Exp((double)logLikelihoodRatio);
        if (!double.IsFinite(likelihoodRatio) || likelihoodRatio == 0d)
        {
            return null;
        }

        try
        {
            var decimalLikelihoodRatio = (decimal)likelihoodRatio;
            return decimalLikelihoodRatio == 0m ? null : decimalLikelihoodRatio;
        }
        catch (OverflowException)
        {
            return null;
        }
    }

    private static void EnsureValidGraphAndNode(Graph graph, string nodeId)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentException.ThrowIfNullOrWhiteSpace(nodeId);

        var validation = ValidateGraph(graph);
        if (!validation.IsValid)
        {
            throw new ArgumentException(
                $"Graph violates the robustness-v0 input contract: {string.Join("; ", validation.Issues.Select(issue => issue.Message))}",
                nameof(graph));
        }

        if (!graph.Nodes.Any(node => string.Equals(node.Id, nodeId, StringComparison.Ordinal)))
        {
            throw new ArgumentException($"Node '{nodeId}' is not present in the graph.", nameof(nodeId));
        }
    }

    private sealed class OrdinalSequenceComparer : IComparer<IReadOnlyList<string>>
    {
        public static OrdinalSequenceComparer Instance { get; } = new();

        public int Compare(IReadOnlyList<string>? left, IReadOnlyList<string>? right)
        {
            if (ReferenceEquals(left, right))
            {
                return 0;
            }

            if (left is null)
            {
                return 1;
            }

            if (right is null)
            {
                return -1;
            }

            var sharedLength = Math.Min(left.Count, right.Count);
            for (var index = 0; index < sharedLength; index++)
            {
                var comparison = StringComparer.Ordinal.Compare(left[index], right[index]);
                if (comparison != 0)
                {
                    return comparison;
                }
            }

            return left.Count.CompareTo(right.Count);
        }
    }
}
