using Backend.Models.Domain;

namespace Backend.Calculation;

/// <summary>
/// Calculates a Bayes factor over an already-pruned, dependency-compatible DAG.
/// Leaf Bayes factors are explicit inputs; node prior/posterior odds are not used.
/// </summary>
public sealed class GraphBayesFactorCalculator
{
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

        return bayesFactorsByNodeId[hypothesisNodeId];
    }

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
