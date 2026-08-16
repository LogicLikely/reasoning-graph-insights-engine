using Backend.Models.Domain;

namespace Backend.Calculation;

/// <summary>
/// Builds a posterior-independent subgraph containing compatible strongest
/// likelihood-ratio paths from evidence nodes to a hypothesis.
/// </summary>
/// <remarks>
/// Edge importance is interpreted as a multiplicative likelihood ratio. Path
/// strength is the absolute log likelihood ratio (distance from neutral BF=1),
/// and ties in the computed counter/support log scores select the maximum path.
/// At a merge, all evidence prefixes are retained and the merge node's locally
/// strongest suffix is shared. This class neither uses nor recalculates
/// posterior odds. Validation and pruning are limited to the portion of the
/// graph that can reach the requested hypothesis.
/// </remarks>
public sealed class GraphBayesFactorPruner
{
    /// <summary>
    /// Returns the compatible strongest-path subgraph for the requested hypothesis.
    /// </summary>
    public Graph Prune(
        Graph graph,
        string hypothesisNodeId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(hypothesisNodeId);
        cancellationToken.ThrowIfCancellationRequested();

        ValidateUniqueIds(graph);

        var context = GraphCalculationContext.From(graph.Nodes, graph.Edges);
        if (!context.NodesById.ContainsKey(hypothesisNodeId))
        {
            throw new InvalidOperationException(
                $"Hypothesis node '{hypothesisNodeId}' does not exist in the graph.");
        }

        var relevantNodeIds = CollectNodesThatReachHypothesis(
            context,
            hypothesisNodeId,
            cancellationToken);
        var pathMetricsByNodeId = CalculatePathMetrics(
            context,
            hypothesisNodeId,
            relevantNodeIds,
            cancellationToken);

        var evidenceNodeIds = relevantNodeIds
            .Where(nodeId =>
                nodeId != hypothesisNodeId &&
                IsEvidenceKind(context.NodesById[nodeId].Kind))
            .ToList();

        var selectedSubgraph = SelectCompatiblePaths(
            evidenceNodeIds,
            hypothesisNodeId,
            pathMetricsByNodeId,
            cancellationToken);

        return CreatePrunedGraph(
            graph,
            selectedSubgraph.NodeIds,
            selectedSubgraph.EdgeIds,
            cancellationToken);
    }

    /// <summary>Finds every node with a directed path to the hypothesis.</summary>
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

    /// <summary>Builds the min/max path table in reverse topological order.</summary>
    private static Dictionary<string, PathMetrics> CalculatePathMetrics(
        GraphCalculationContext context,
        string hypothesisNodeId,
        HashSet<string> relevantNodeIds,
        CancellationToken cancellationToken)
    {
        var unresolvedParentEdgeCount = relevantNodeIds.ToDictionary(
            nodeId => nodeId,
            nodeId => context.ParentEdgesByChildId.TryGetValue(nodeId, out var parentEdges)
                ? parentEdges.Count(edge => relevantNodeIds.Contains(edge.ToNodeId))
                : 0,
            StringComparer.Ordinal);

        var readyNodes = new Queue<string>(
            unresolvedParentEdgeCount
                .Where(entry => entry.Value == 0)
                .Select(entry => entry.Key));
        var pathMetricsByNodeId = new Dictionary<string, PathMetrics>(
            relevantNodeIds.Count,
            StringComparer.Ordinal);
        int processedNodeCount = 0;

        while (readyNodes.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string nodeId = readyNodes.Dequeue();

            pathMetricsByNodeId[nodeId] = nodeId == hypothesisNodeId
                ? new PathMetrics(0m, 0m, null, null, 0)
                : CalculateNodePathMetrics(
                    context,
                    nodeId,
                    relevantNodeIds,
                    pathMetricsByNodeId);
            processedNodeCount++;

            if (!context.ChildEdgesByParentId.TryGetValue(nodeId, out var childEdges))
            {
                continue;
            }

            foreach (var edge in childEdges)
            {
                if (!relevantNodeIds.Contains(edge.FromNodeId))
                {
                    continue;
                }

                int remainingCount = --unresolvedParentEdgeCount[edge.FromNodeId];
                if (remainingCount == 0)
                {
                    readyNodes.Enqueue(edge.FromNodeId);
                }
            }
        }

        if (processedNodeCount != relevantNodeIds.Count)
        {
            string cycleNodeId = unresolvedParentEdgeCount
                .Where(entry => entry.Value > 0)
                .Select(entry => entry.Key)
                .OrderBy(nodeId => nodeId, StringComparer.Ordinal)
                .First();
            throw new InvalidOperationException(
                $"Cycle detected while pruning Bayes-factor paths at node '{cycleNodeId}'.");
        }

        return pathMetricsByNodeId;
    }

    /// <summary>Calculates both path extremes and the distance metric for one node.</summary>
    private static PathMetrics CalculateNodePathMetrics(
        GraphCalculationContext context,
        string nodeId,
        HashSet<string> relevantNodeIds,
        Dictionary<string, PathMetrics> pathMetricsByNodeId)
    {
        decimal? minimumLogLr = null;
        decimal? maximumLogLr = null;
        GraphEdgeCalcState? minimumNextEdge = null;
        GraphEdgeCalcState? maximumNextEdge = null;
        int distanceFromHypothesis = 0;

        foreach (var edge in context.ParentEdgesByChildId[nodeId])
        {
            if (!relevantNodeIds.Contains(edge.ToNodeId))
            {
                continue;
            }

            decimal logEdgeWeight = GetLogEdgeWeight(edge);
            var parentMetrics = pathMetricsByNodeId[edge.ToNodeId];
            decimal minimumCandidate = logEdgeWeight + parentMetrics.MinimumLogLr;
            decimal maximumCandidate = logEdgeWeight + parentMetrics.MaximumLogLr;

            if (!minimumLogLr.HasValue ||
                minimumCandidate < minimumLogLr.Value ||
                minimumCandidate == minimumLogLr.Value &&
                IsEarlierEdge(edge, minimumNextEdge!))
            {
                minimumLogLr = minimumCandidate;
                minimumNextEdge = edge;
            }

            if (!maximumLogLr.HasValue ||
                maximumCandidate > maximumLogLr.Value ||
                maximumCandidate == maximumLogLr.Value &&
                IsEarlierEdge(edge, maximumNextEdge!))
            {
                maximumLogLr = maximumCandidate;
                maximumNextEdge = edge;
            }

            distanceFromHypothesis = Math.Max(
                distanceFromHypothesis,
                parentMetrics.DistanceFromHypothesis + 1);
        }

        if (!minimumLogLr.HasValue || !maximumLogLr.HasValue)
        {
            throw new InvalidOperationException(
                $"Node '{nodeId}' was expected to have a path to the hypothesis.");
        }

        return new PathMetrics(
            minimumLogLr.Value,
            maximumLogLr.Value,
            minimumNextEdge,
            maximumNextEdge,
            distanceFromHypothesis);
    }

    /// <summary>Applies the deterministic tie-break between equal-scoring edges.</summary>
    private static bool IsEarlierEdge(
        GraphEdgeCalcState candidate,
        GraphEdgeCalcState current)
    {
        int byId = StringComparer.Ordinal.Compare(candidate.Id, current.Id);
        if (byId != 0)
        {
            return byId < 0;
        }

        int byDestination = StringComparer.Ordinal.Compare(
            candidate.ToNodeId,
            current.ToNodeId);
        return byDestination < 0;
    }

    /// <summary>Validates and converts an edge likelihood ratio to log space.</summary>
    private static decimal GetLogEdgeWeight(GraphEdgeCalcState edge)
    {
        if (edge.ImportanceToParent <= 0m)
        {
            throw new InvalidOperationException(
                $"Edge '{edge.Id}' has invalid likelihood ratio " +
                $"'{edge.ImportanceToParent}'. Likelihood ratios must be greater than zero.");
        }

        return (decimal)Math.Log((double)edge.ImportanceToParent);
    }

    /// <summary>Selects the path extreme farthest from the neutral likelihood ratio.</summary>
    private static PathExtreme SelectStrongest(PathMetrics metrics)
    {
        return Math.Abs(metrics.MinimumLogLr) > Math.Abs(metrics.MaximumLogLr)
            ? PathExtreme.Minimum
            : PathExtreme.Maximum;
    }

    /// <summary>Selects paths while collapsing each merge to one shared continuation.</summary>
    private static SelectedSubgraph SelectCompatiblePaths(
        IReadOnlyCollection<string> evidenceNodeIds,
        string hypothesisNodeId,
        Dictionary<string, PathMetrics> pathMetricsByNodeId,
        CancellationToken cancellationToken)
    {
        var selectedNodeIds = new HashSet<string>(StringComparer.Ordinal)
        {
            hypothesisNodeId
        };
        var selectedEdgeIds = new HashSet<string>(StringComparer.Ordinal);
        if (evidenceNodeIds.Count == 0)
        {
            return new SelectedSubgraph(selectedNodeIds, selectedEdgeIds);
        }

        int maximumDistance = evidenceNodeIds.Max(
            nodeId => pathMetricsByNodeId[nodeId].DistanceFromHypothesis);
        var scheduledNodesByDistance = new List<string>?[maximumDistance + 1];
        var arrivalsByNodeId = new Dictionary<string, List<PathExtreme>>(
            StringComparer.Ordinal);

        void AddArrival(string nodeId, PathExtreme selection)
        {
            if (!arrivalsByNodeId.TryGetValue(nodeId, out var arrivals))
            {
                arrivals = [];
                arrivalsByNodeId[nodeId] = arrivals;

                int distance = pathMetricsByNodeId[nodeId].DistanceFromHypothesis;
                scheduledNodesByDistance[distance] ??= [];
                scheduledNodesByDistance[distance]!.Add(nodeId);
            }

            arrivals.Add(selection);
        }

        foreach (string evidenceNodeId in evidenceNodeIds)
        {
            cancellationToken.ThrowIfCancellationRequested();
            AddArrival(
                evidenceNodeId,
                SelectStrongest(pathMetricsByNodeId[evidenceNodeId]));
        }

        // Each arrival represents one evidence component that has not yet
        // merged at this node. A single arrival keeps its source's min/max
        // choice; multiple arrivals select the node's locally strongest suffix
        // and collapse to the one arrival emitted below.
        //
        // Longest-hop distance strictly decreases along every edge toward H.
        // Processing distance buckets from high to low therefore collects all
        // path arrivals at a node before choosing its single continuation.
        for (int distance = maximumDistance; distance >= 0; distance--)
        {
            var scheduledNodeIds = scheduledNodesByDistance[distance];
            if (scheduledNodeIds is null)
            {
                continue;
            }

            foreach (string nodeId in scheduledNodeIds)
            {
                cancellationToken.ThrowIfCancellationRequested();
                selectedNodeIds.Add(nodeId);

                if (nodeId == hypothesisNodeId)
                {
                    continue;
                }

                var arrivals = arrivalsByNodeId[nodeId];
                PathExtreme selection = arrivals.Count == 1
                    ? arrivals[0]
                    : SelectStrongest(pathMetricsByNodeId[nodeId]);
                var metrics = pathMetricsByNodeId[nodeId];
                var nextEdge = selection == PathExtreme.Minimum
                    ? metrics.MinimumNextEdge
                    : metrics.MaximumNextEdge;
                if (nextEdge is null)
                {
                    throw new InvalidOperationException(
                        $"No {selection.ToString().ToLowerInvariant()} path from node " +
                        $"'{nodeId}' reaches hypothesis '{hypothesisNodeId}'.");
                }

                selectedEdgeIds.Add(nextEdge.Id);
                selectedNodeIds.Add(nextEdge.ToNodeId);
                AddArrival(nextEdge.ToNodeId, selection);
            }
        }

        return new SelectedSubgraph(selectedNodeIds, selectedEdgeIds);
    }

    /// <summary>Clones the selected nodes and edges into a new graph.</summary>
    private static Graph CreatePrunedGraph(
        Graph source,
        HashSet<string> selectedNodeIds,
        HashSet<string> selectedEdgeIds,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var selectedNodes = new List<GraphNode>(selectedNodeIds.Count);
        foreach (var node in source.Nodes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (selectedNodeIds.Contains(node.Id))
            {
                selectedNodes.Add(CloneNode(node));
            }
        }

        var selectedEdges = new List<GraphEdge>(selectedEdgeIds.Count);
        foreach (var edge in source.Edges)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (selectedEdgeIds.Contains(edge.Id))
            {
                selectedEdges.Add(CloneEdge(edge));
            }
        }

        return new Graph
        {
            Id = source.Id,
            Slug = source.Slug,
            Title = source.Title,
            Description = source.Description,
            Nodes = selectedNodes,
            Edges = selectedEdges
        };
    }

    /// <summary>Creates an independent copy of a selected node.</summary>
    private static GraphNode CloneNode(GraphNode node)
    {
        return new GraphNode
        {
            Id = node.Id,
            Kind = node.Kind,
            Title = node.Title,
            BodyText = node.BodyText,
            Category = node.Category,
            Tags = node.Tags.ToList(),
            PriorOdds = node.PriorOdds,
            PosteriorOdds = node.PosteriorOdds,
            Evidence = node.Evidence is null
                ? null
                : new GraphEvidenceDetails
                {
                    Type = node.Evidence.Type,
                    Score = node.Evidence.Score,
                    Rationale = node.Evidence.Rationale
                }
        };
    }

    /// <summary>Creates an independent copy of a selected edge.</summary>
    private static GraphEdge CloneEdge(GraphEdge edge)
    {
        return new GraphEdge
        {
            Id = edge.Id,
            From = edge.From,
            To = edge.To,
            Kind = edge.Kind,
            ImportanceToParent = edge.ImportanceToParent,
            ProbabilityGivenParent = edge.ProbabilityGivenParent,
            ProbabilityGivenNotParent = edge.ProbabilityGivenNotParent
        };
    }

    /// <summary>Identifies node kinds that seed evidence-path traversals.</summary>
    private static bool IsEvidenceKind(string nodeKind)
    {
        return string.Equals(nodeKind, "evidence", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(nodeKind, "objection", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Rejects duplicate identifiers before building graph indexes.</summary>
    private static void ValidateUniqueIds(Graph graph)
    {
        string? duplicateNodeId = graph.Nodes
            .GroupBy(node => node.Id, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .OrderBy(nodeId => nodeId, StringComparer.Ordinal)
            .FirstOrDefault();
        if (duplicateNodeId is not null)
        {
            throw new InvalidOperationException(
                $"Graph contains duplicate node id '{duplicateNodeId}'.");
        }

        string? duplicateEdgeId = graph.Edges
            .GroupBy(edge => edge.Id, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .OrderBy(edgeId => edgeId, StringComparer.Ordinal)
            .FirstOrDefault();
        if (duplicateEdgeId is not null)
        {
            throw new InvalidOperationException(
                $"Graph contains duplicate edge id '{duplicateEdgeId}'.");
        }
    }

    private sealed record PathMetrics(
        decimal MinimumLogLr,
        decimal MaximumLogLr,
        GraphEdgeCalcState? MinimumNextEdge,
        GraphEdgeCalcState? MaximumNextEdge,
        int DistanceFromHypothesis);

    private sealed record SelectedSubgraph(
        HashSet<string> NodeIds,
        HashSet<string> EdgeIds);

    private enum PathExtreme
    {
        Minimum,
        Maximum
    }

}
