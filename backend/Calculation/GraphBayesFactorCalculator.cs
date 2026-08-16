using Backend.Models.Domain;

namespace Backend.Calculation;

/// <summary>
/// Calculates a Bayes factor over an already-pruned, dependency-compatible DAG.
/// Leaf Bayes factors are explicit inputs; node prior/posterior odds are not used.
/// </summary>
/// <remarks>
/// Edges are traversed from child to parent. Direct child contributions are
/// transformed through their edge probabilities and combined at each parent.
/// Structural leaves use their supplied BF as the recurrence base case;
/// internal nodes multiply all transformed direct-child contributions.
/// The ordinary API preserves checked decimal arithmetic; the log API uses a
/// stable log-sum-exp transform for posterior-odds integration. Both APIs are
/// read-only and visit each target-relevant node and edge once.
/// </remarks>
public sealed class GraphBayesFactorCalculator
{
    /// <summary>
    /// Calculates an ordinary Bayes factor using positive ordinary leaf BFs.
    /// </summary>
    public decimal Calculate(
        Graph prunedGraph,
        string hypothesisNodeId,
        IReadOnlyDictionary<string, decimal> leafBayesFactorsByNodeId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(prunedGraph);
        ArgumentNullException.ThrowIfNull(hypothesisNodeId);
        ArgumentNullException.ThrowIfNull(leafBayesFactorsByNodeId);
        cancellationToken.ThrowIfCancellationRequested();

        return CalculateBayesFactorCore(
            prunedGraph,
            hypothesisNodeId,
            leafBayesFactorsByNodeId,
            cancellationToken);
    }

    /// <summary>
    /// Calculates the natural logarithm of the Bayes factor over an
    /// already-pruned graph. Leaf inputs are natural-log Bayes factors.
    /// </summary>
    public decimal CalculateLogBayesFactor(
        Graph prunedGraph,
        string hypothesisNodeId,
        IReadOnlyDictionary<string, decimal> leafLogBayesFactorsByNodeId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(prunedGraph);
        ArgumentNullException.ThrowIfNull(hypothesisNodeId);
        ArgumentNullException.ThrowIfNull(leafLogBayesFactorsByNodeId);
        cancellationToken.ThrowIfCancellationRequested();

        return CalculateLogCore(
            prunedGraph,
            hypothesisNodeId,
            nodeId => GetSuppliedLeafLogBayesFactor(
                nodeId,
                leafLogBayesFactorsByNodeId),
            cancellationToken);
    }

    /// <summary>Evaluates the BF recurrence with checked decimal arithmetic.</summary>
    private static decimal CalculateBayesFactorCore(
        Graph prunedGraph,
        string hypothesisNodeId,
        IReadOnlyDictionary<string, decimal> leafBayesFactorsByNodeId,
        CancellationToken cancellationToken)
    {
        var context = GraphCalculationContext.From(prunedGraph.Nodes, prunedGraph.Edges);
        if (!context.NodesById.ContainsKey(hypothesisNodeId))
        {
            throw new InvalidOperationException(
                $"Hypothesis node '{hypothesisNodeId}' does not exist in the graph.");
        }

        var relevantNodeIds = CollectNodesThatReachHypothesis(
            context,
            hypothesisNodeId,
            cancellationToken);
        var unresolvedChildEdgeCount = relevantNodeIds.ToDictionary(
            nodeId => nodeId,
            nodeId => context.ChildEdgesByParentId.TryGetValue(nodeId, out var childEdges)
                ? childEdges.Count(edge => relevantNodeIds.Contains(edge.FromNodeId))
                : 0,
            StringComparer.Ordinal);
        var accumulatedChildContributions = relevantNodeIds.ToDictionary(
            nodeId => nodeId,
            _ => 1m,
            StringComparer.Ordinal);
        var bayesFactorsByNodeId = new Dictionary<string, decimal>(
            relevantNodeIds.Count,
            StringComparer.Ordinal);
        var readyNodes = new Queue<string>(
            unresolvedChildEdgeCount
                .Where(entry => entry.Value == 0)
                .Select(entry => entry.Key));

        int processedNodeCount = 0;
        while (readyNodes.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string nodeId = readyNodes.Dequeue();

            decimal nodeBayesFactor;
            if (!context.ChildEdgesByParentId.TryGetValue(nodeId, out var childEdges) ||
                !childEdges.Any(edge => relevantNodeIds.Contains(edge.FromNodeId)))
            {
                nodeBayesFactor = GetLeafBayesFactor(
                    nodeId,
                    leafBayesFactorsByNodeId);
            }
            else
            {
                nodeBayesFactor = accumulatedChildContributions[nodeId];
            }

            bayesFactorsByNodeId[nodeId] = nodeBayesFactor;
            processedNodeCount++;

            if (!context.ParentEdgesByChildId.TryGetValue(nodeId, out var parentEdges))
            {
                continue;
            }

            foreach (var edge in parentEdges)
            {
                if (!relevantNodeIds.Contains(edge.ToNodeId))
                {
                    continue;
                }

                decimal contribution = TransformThroughEdge(nodeBayesFactor, edge);
                accumulatedChildContributions[edge.ToNodeId] = checked(
                    accumulatedChildContributions[edge.ToNodeId] * contribution);

                int remainingChildren = --unresolvedChildEdgeCount[edge.ToNodeId];
                if (remainingChildren == 0)
                {
                    readyNodes.Enqueue(edge.ToNodeId);
                }
            }
        }

        ValidateCompletedCalculation(
            context,
            hypothesisNodeId,
            relevantNodeIds,
            unresolvedChildEdgeCount,
            processedNodeCount);

        return bayesFactorsByNodeId[hypothesisNodeId];
    }

    /// <summary>Evaluates the equivalent recurrence additively in log space.</summary>
    private static decimal CalculateLogCore(
        Graph prunedGraph,
        string hypothesisNodeId,
        Func<string, decimal> getLeafLogBayesFactor,
        CancellationToken cancellationToken)
    {
        var context = GraphCalculationContext.From(prunedGraph.Nodes, prunedGraph.Edges);
        if (!context.NodesById.ContainsKey(hypothesisNodeId))
        {
            throw new InvalidOperationException(
                $"Hypothesis node '{hypothesisNodeId}' does not exist in the graph.");
        }

        var relevantNodeIds = CollectNodesThatReachHypothesis(
            context,
            hypothesisNodeId,
            cancellationToken);

        var unresolvedChildEdgeCount = relevantNodeIds.ToDictionary(
            nodeId => nodeId,
            nodeId => context.ChildEdgesByParentId.TryGetValue(nodeId, out var childEdges)
                ? childEdges.Count(edge => relevantNodeIds.Contains(edge.FromNodeId))
                : 0,
            StringComparer.Ordinal);
        var accumulatedChildLogContributions = relevantNodeIds.ToDictionary(
            nodeId => nodeId,
            _ => 0m,
            StringComparer.Ordinal);
        var logBayesFactorsByNodeId = new Dictionary<string, decimal>(
            relevantNodeIds.Count,
            StringComparer.Ordinal);
        var readyNodes = new Queue<string>(
            unresolvedChildEdgeCount
                .Where(entry => entry.Value == 0)
                .Select(entry => entry.Key));

        int processedNodeCount = 0;
        while (readyNodes.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string nodeId = readyNodes.Dequeue();

            decimal nodeLogBayesFactor;
            if (!context.ChildEdgesByParentId.TryGetValue(nodeId, out var childEdges) ||
                !childEdges.Any(edge => relevantNodeIds.Contains(edge.FromNodeId)))
            {
                nodeLogBayesFactor = getLeafLogBayesFactor(nodeId);
            }
            else
            {
                nodeLogBayesFactor = accumulatedChildLogContributions[nodeId];
            }

            logBayesFactorsByNodeId[nodeId] = nodeLogBayesFactor;
            processedNodeCount++;

            if (!context.ParentEdgesByChildId.TryGetValue(nodeId, out var parentEdges))
            {
                continue;
            }

            foreach (var edge in parentEdges)
            {
                if (!relevantNodeIds.Contains(edge.ToNodeId))
                {
                    continue;
                }

                decimal logContribution = TransformLogThroughEdge(
                    nodeLogBayesFactor,
                    edge);
                accumulatedChildLogContributions[edge.ToNodeId] = checked(
                    accumulatedChildLogContributions[edge.ToNodeId] + logContribution);

                int remainingChildren = --unresolvedChildEdgeCount[edge.ToNodeId];
                if (remainingChildren == 0)
                {
                    readyNodes.Enqueue(edge.ToNodeId);
                }
            }
        }

        ValidateCompletedCalculation(
            context,
            hypothesisNodeId,
            relevantNodeIds,
            unresolvedChildEdgeCount,
            processedNodeCount);

        return logBayesFactorsByNodeId[hypothesisNodeId];
    }

    /// <summary>Rejects reachable cycles and competing continuations.</summary>
    private static void ValidateCompletedCalculation(
        GraphCalculationContext context,
        string hypothesisNodeId,
        HashSet<string> relevantNodeIds,
        Dictionary<string, int> unresolvedChildEdgeCount,
        int processedNodeCount)
    {
        if (processedNodeCount != relevantNodeIds.Count)
        {
            string cycleNodeId = unresolvedChildEdgeCount
                .Where(entry => entry.Value > 0)
                .Select(entry => entry.Key)
                .OrderBy(nodeId => nodeId, StringComparer.Ordinal)
                .First();
            throw new InvalidOperationException(
                $"Cycle detected while calculating Bayes factor at node '{cycleNodeId}'.");
        }

        ValidateCompatibleContinuations(
            context,
            hypothesisNodeId,
            relevantNodeIds);
    }

    /// <summary>Finds every node whose directed path can reach the hypothesis.</summary>
    private static HashSet<string> CollectNodesThatReachHypothesis(
        GraphCalculationContext context,
        string hypothesisNodeId,
        CancellationToken cancellationToken)
    {
        var relevantNodeIds = new HashSet<string>(StringComparer.Ordinal)
        {
            hypothesisNodeId
        };
        var nodesToVisit = new Queue<string>();
        nodesToVisit.Enqueue(hypothesisNodeId);

        while (nodesToVisit.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string parentNodeId = nodesToVisit.Dequeue();

            if (!context.ChildEdgesByParentId.TryGetValue(parentNodeId, out var childEdges))
            {
                continue;
            }

            foreach (var edge in childEdges)
            {
                if (relevantNodeIds.Add(edge.FromNodeId))
                {
                    nodesToVisit.Enqueue(edge.FromNodeId);
                }
            }
        }

        return relevantNodeIds;
    }

    /// <summary>Ensures each relevant node has at most one continuation toward the target.</summary>
    private static void ValidateCompatibleContinuations(
        GraphCalculationContext context,
        string hypothesisNodeId,
        HashSet<string> relevantNodeIds)
    {
        foreach (string nodeId in relevantNodeIds)
        {
            int continuationCount = context.ParentEdgesByChildId.TryGetValue(
                nodeId,
                out var parentEdges)
                ? parentEdges.Count(edge => relevantNodeIds.Contains(edge.ToNodeId))
                : 0;

            int maximumContinuationCount = nodeId == hypothesisNodeId ? 0 : 1;
            if (continuationCount > maximumContinuationCount)
            {
                throw new InvalidOperationException(
                    $"Node '{nodeId}' has {continuationCount} continuations toward hypothesis " +
                    $"'{hypothesisNodeId}'. Prune the graph before calculating its Bayes factor.");
            }
        }
    }

    /// <summary>Retrieves and validates one positive ordinary leaf BF.</summary>
    private static decimal GetLeafBayesFactor(
        string nodeId,
        IReadOnlyDictionary<string, decimal> leafBayesFactorsByNodeId)
    {
        if (!leafBayesFactorsByNodeId.TryGetValue(nodeId, out decimal bayesFactor))
        {
            throw new InvalidOperationException(
                $"No Bayes factor was supplied for leaf node '{nodeId}'.");
        }

        if (bayesFactor <= 0m)
        {
            throw new InvalidOperationException(
                $"Leaf node '{nodeId}' has invalid Bayes factor '{bayesFactor}'. " +
                "Bayes factors must be greater than zero.");
        }

        return bayesFactor;
    }

    /// <summary>Retrieves one explicitly supplied natural-log leaf BF.</summary>
    private static decimal GetSuppliedLeafLogBayesFactor(
        string nodeId,
        IReadOnlyDictionary<string, decimal> leafLogBayesFactorsByNodeId)
    {
        if (!leafLogBayesFactorsByNodeId.TryGetValue(
                nodeId,
                out decimal logBayesFactor))
        {
            throw new InvalidOperationException(
                $"No log Bayes factor was supplied for leaf node '{nodeId}'.");
        }

        return logBayesFactor;
    }

    /// <summary>Transforms a child log BF through one edge in stable log space.</summary>
    private static decimal TransformLogThroughEdge(
        decimal childLogBayesFactor,
        GraphEdgeCalcState edge)
    {
        ValidateProbability(
            edge.Id,
            nameof(edge.ProbabilityGivenParent),
            edge.ProbabilityGivenParent);
        ValidateProbability(
            edge.Id,
            nameof(edge.ProbabilityGivenNotParent),
            edge.ProbabilityGivenNotParent);

        double logNumerator = LogAffineBayesFactor(
            (double)childLogBayesFactor,
            (double)edge.ProbabilityGivenParent);
        double logDenominator = LogAffineBayesFactor(
            (double)childLogBayesFactor,
            (double)edge.ProbabilityGivenNotParent);
        double logContribution = logNumerator - logDenominator;
        if (!double.IsFinite(logContribution))
        {
            throw new InvalidOperationException(
                $"Edge '{edge.Id}' produces an undefined Bayes-factor transform.");
        }

        return checked((decimal)logContribution);
    }

    /// <summary>Transforms an ordinary child BF through one edge.</summary>
    private static decimal TransformThroughEdge(
        decimal childBayesFactor,
        GraphEdgeCalcState edge)
    {
        ValidateProbability(
            edge.Id,
            nameof(edge.ProbabilityGivenParent),
            edge.ProbabilityGivenParent);
        ValidateProbability(
            edge.Id,
            nameof(edge.ProbabilityGivenNotParent),
            edge.ProbabilityGivenNotParent);

        decimal numerator = checked(
            childBayesFactor * edge.ProbabilityGivenParent +
            (1m - edge.ProbabilityGivenParent));
        decimal denominator = checked(
            childBayesFactor * edge.ProbabilityGivenNotParent +
            (1m - edge.ProbabilityGivenNotParent));
        if (denominator == 0m)
        {
            throw new InvalidOperationException(
                $"Edge '{edge.Id}' produces an undefined Bayes-factor transform.");
        }

        return numerator / denominator;
    }

    /// <summary>Calculates log(p exp(x) + 1 - p) without directly expanding exp(x).</summary>
    private static double LogAffineBayesFactor(
        double logBayesFactor,
        double probability)
    {
        if (probability == 0d)
        {
            return 0d;
        }

        if (probability == 1d)
        {
            return logBayesFactor;
        }

        double weightedEvidence = logBayesFactor + Math.Log(probability);
        double complement = Math.Log(1d - probability);
        double maximum = Math.Max(weightedEvidence, complement);

        return maximum + Math.Log(
            Math.Exp(weightedEvidence - maximum) +
            Math.Exp(complement - maximum));
    }

    /// <summary>Validates one conditional probability used by an edge transform.</summary>
    private static void ValidateProbability(
        string edgeId,
        string propertyName,
        decimal probability)
    {
        if (probability is < 0m or > 1m)
        {
            throw new InvalidOperationException(
                $"Edge '{edgeId}' has invalid {propertyName} value '{probability}'. " +
                "Conditional probabilities must be between zero and one.");
        }
    }
}
