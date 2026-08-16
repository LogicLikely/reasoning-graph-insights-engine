using Backend.Insights.Contracts;
using Backend.Seeding;
using static Backend.Insights.Benchmarking.DeterministicStressGraphFixture;

namespace Backend.Insights.Benchmarking;

/// <summary>
/// Generates deterministic in-memory fixtures from the canonical stress seed
/// catalog and the topology/value rules in insights_stress_seed.sql. Textual
/// payloads use a small benchmark-only corpus so fixture construction does not
/// depend on PostgreSQL, HTTP, or a machine-local source path.
/// </summary>
public static class DeterministicStressGraphFixtureFactory
{
    public const string GeneratorVersion = "stress-v1-in-memory";
    public const string CorpusId = "in-memory-stress-corpus-v1";

    private const decimal MinimumLogOdds = -100m;
    private const decimal MaximumLogOdds = 100m;

    private static readonly string StableCorpusFingerprint = CanonicalJson.ComputeSha256(new
    {
        corpusId = CorpusId,
        version = 1,
        titlePattern = "{kind} {node-id}",
        bodyPattern = "Deterministic in-memory benchmark node {node-id}.",
        category = "benchmark",
        tags = new[] { "benchmark", "{shape}" },
        evidenceTypes = new[]
        {
            "experimental",
            "media-analysis",
            "observational",
            "textual",
            "video"
        }
    });

    public static DeterministicStressGraphFixture Create(
        string datasetId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(datasetId);
        var specification = StressGraphSeedCatalog.Resolve([datasetId]).Single();
        return Create(specification, cancellationToken);
    }

    public static DeterministicStressGraphFixture Create(
        StressGraphSeedSpec specification,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(specification);
        ValidateCatalogSpecification(specification);
        cancellationToken.ThrowIfCancellationRequested();

        var posteriorContributions = CalculatePosteriorContributions(
            specification,
            cancellationToken);
        var nodes = CreateNodes(
            specification,
            posteriorContributions,
            cancellationToken);
        var edges = CreateEdges(specification, cancellationToken);

        var nodeIdentityDigest = CanonicalJson.ComputeSha256Sequence(
            nodes.Select(node => new { node.Id }),
            cancellationToken);
        var edgeTopologyDigest = CanonicalJson.ComputeSha256Sequence(
            edges.Select(edge => new
            {
                edge.Id,
                edge.From,
                edge.To,
                edge.Kind,
                edge.ImportanceToParent
            }),
            cancellationToken);
        var topologyFingerprint = CanonicalJson.ComputeSha256(new
        {
            generatorVersion = GeneratorVersion,
            specification.Shape,
            specification.NodeCount,
            specification.EdgeCount,
            nodeIdentityDigest,
            edgeTopologyDigest
        });
        var nodeInputDigest = CanonicalJson.ComputeSha256Sequence(nodes, cancellationToken);
        var inputFingerprint = CanonicalJson.ComputeSha256(new
        {
            specification.Slug,
            specification.Title,
            specification.Description,
            nodeInputDigest,
            edgeTopologyDigest
        });
        var datasetInputFingerprint = CanonicalJson.ComputeSha256(new
        {
            generatorVersion = GeneratorVersion,
            corpusId = CorpusId,
            corpusFingerprint = StableCorpusFingerprint,
            topologyFingerprint,
            inputFingerprint
        });
        var identity = new DeterministicStressGraphIdentity(
            GeneratorVersion,
            CorpusId,
            StableCorpusFingerprint,
            topologyFingerprint,
            inputFingerprint,
            datasetInputFingerprint);

        return new DeterministicStressGraphFixture(
            specification,
            identity,
            nodes,
            edges);
    }

    public static string NodeId(int nodeIndex) => $"n-{nodeIndex:D5}";

    private static NodeBlueprint[] CreateNodes(
        StressGraphSeedSpec specification,
        IReadOnlyList<decimal> posteriorContributions,
        CancellationToken cancellationToken)
    {
        var nodes = new NodeBlueprint[specification.NodeCount];
        for (var nodeIndex = 0; nodeIndex < nodes.Length; nodeIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var id = NodeId(nodeIndex);
            var kind = NodeKind(nodeIndex);
            var evidenceScore = EvidenceScore(nodeIndex);
            var priorOdds = kind == "evidence"
                ? (decimal)Math.Log((double)(evidenceScore / (100m - evidenceScore)))
                : 0m;
            var posteriorOdds = Math.Clamp(
                priorOdds + posteriorContributions[nodeIndex],
                MinimumLogOdds,
                MaximumLogOdds);
            var evidence = kind == "evidence"
                ? new EvidenceBlueprint(
                    EvidenceType(nodeIndex),
                    evidenceScore,
                    $"Deterministic benchmark evidence for {id}.")
                : null;

            nodes[nodeIndex] = new NodeBlueprint(
                id,
                kind,
                $"{kind} {id}",
                $"Deterministic in-memory benchmark node {id}.",
                "benchmark",
                Array.AsReadOnly(new[] { "benchmark", specification.Shape }),
                priorOdds,
                posteriorOdds,
                evidence);
        }

        return nodes;
    }

    private static EdgeBlueprint[] CreateEdges(
        StressGraphSeedSpec specification,
        CancellationToken cancellationToken)
    {
        var edges = new List<EdgeBlueprint>(specification.EdgeCount);
        for (var nodeIndex = 1; nodeIndex < specification.NodeCount; nodeIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            edges.Add(CreateEdge("e-p", nodeIndex, PrimaryParentIndex(
                specification.Shape,
                nodeIndex)));

            if (specification.Shape == "shared-diamond" && nodeIndex >= 5)
            {
                edges.Add(CreateEdge(
                    "e-a",
                    nodeIndex,
                    AlternateParentIndex((nodeIndex - 1) / 4)));
            }
        }

        if (edges.Count != specification.EdgeCount)
        {
            throw new InvalidOperationException(
                $"Fixture '{specification.Id}' generated {edges.Count} edges; expected {specification.EdgeCount}.");
        }

        return edges.ToArray();
    }

    private static EdgeBlueprint CreateEdge(
        string prefix,
        int nodeIndex,
        int parentIndex)
    {
        return new EdgeBlueprint(
            $"{prefix}-{nodeIndex:D5}",
            NodeId(nodeIndex),
            NodeId(parentIndex),
            nodeIndex % 2 == 1 ? "support" : "rebut",
            EdgeImportance(nodeIndex));
    }

    private static decimal[] CalculatePosteriorContributions(
        StressGraphSeedSpec specification,
        CancellationToken cancellationToken)
    {
        return specification.Shape == "deep"
            ? CalculateDeepPosteriorContributions(specification.NodeCount, cancellationToken)
            : CalculateShallowPosteriorContributions(specification, cancellationToken);
    }

    private static decimal[] CalculateDeepPosteriorContributions(
        int nodeCount,
        CancellationToken cancellationToken)
    {
        var pathToRoot = new decimal[nodeCount];
        var totalActivePath = 0m;
        var totalActiveCount = 0;
        var currentPath = 0m;
        for (var nodeIndex = 0; nodeIndex < nodeCount; nodeIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (nodeIndex > 0)
            {
                currentPath += EdgeLogWeight(nodeIndex);
            }

            pathToRoot[nodeIndex] = currentPath;
            if (IsActiveEvidenceNode(nodeIndex))
            {
                totalActivePath += currentPath;
                totalActiveCount++;
            }
        }

        var contributions = new decimal[nodeCount];
        var activePathThroughNode = 0m;
        var activeCountThroughNode = 0;
        for (var nodeIndex = 0; nodeIndex < nodeCount; nodeIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (IsActiveEvidenceNode(nodeIndex))
            {
                activePathThroughNode += pathToRoot[nodeIndex];
                activeCountThroughNode++;
            }

            contributions[nodeIndex] =
                (totalActivePath - activePathThroughNode) -
                ((totalActiveCount - activeCountThroughNode) * pathToRoot[nodeIndex]);
        }

        return contributions;
    }

    private static decimal[] CalculateShallowPosteriorContributions(
        StressGraphSeedSpec specification,
        CancellationToken cancellationToken)
    {
        var contributions = new decimal[specification.NodeCount];
        for (var sourceIndex = 1; sourceIndex < specification.NodeCount; sourceIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!IsActiveEvidenceNode(sourceIndex))
            {
                continue;
            }

            if (specification.Shape == "shared-diamond")
            {
                AddSharedDiamondContributions(sourceIndex, contributions, cancellationToken);
            }
            else
            {
                AddSingleParentContributions(
                    specification.Shape,
                    sourceIndex,
                    contributions,
                    cancellationToken);
            }
        }

        return contributions;
    }

    private static void AddSingleParentContributions(
        string shape,
        int sourceIndex,
        decimal[] contributions,
        CancellationToken cancellationToken)
    {
        var childIndex = sourceIndex;
        var accumulatedPath = 0m;
        while (childIndex > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            accumulatedPath += EdgeLogWeight(childIndex);
            var parentIndex = PrimaryParentIndex(shape, childIndex);
            contributions[parentIndex] += accumulatedPath;
            childIndex = parentIndex;
        }
    }

    private static void AddSharedDiamondContributions(
        int sourceIndex,
        decimal[] contributions,
        CancellationToken cancellationToken)
    {
        var primaryParent = (sourceIndex - 1) / 4;
        int? alternateParent = sourceIndex >= 5
            ? AlternateParentIndex(primaryParent)
            : null;
        var minimumPath = EdgeLogWeight(sourceIndex);
        var maximumPath = minimumPath;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var strongestPath = Math.Abs(minimumPath) > Math.Abs(maximumPath)
                ? minimumPath
                : maximumPath;
            contributions[primaryParent] += strongestPath;
            if (alternateParent.HasValue)
            {
                contributions[alternateParent.Value] += strongestPath;
            }

            if (primaryParent == 0)
            {
                return;
            }

            if (!alternateParent.HasValue)
            {
                throw new InvalidOperationException(
                    "A non-root shared-diamond frontier must contain two sibling parents.");
            }

            var primaryWeight = EdgeLogWeight(primaryParent);
            var alternateWeight = EdgeLogWeight(alternateParent.Value);
            minimumPath += Math.Min(primaryWeight, alternateWeight);
            maximumPath += Math.Max(primaryWeight, alternateWeight);

            var nextPrimaryParent = (primaryParent - 1) / 4;
            alternateParent = primaryParent >= 5
                ? AlternateParentIndex(nextPrimaryParent)
                : null;
            primaryParent = nextPrimaryParent;
        }
    }

    private static int PrimaryParentIndex(string shape, int nodeIndex) => shape switch
    {
        "balanced" or "shared-diamond" => (nodeIndex - 1) / 4,
        "wide" => 0,
        "deep" => nodeIndex - 1,
        _ => throw new ArgumentOutOfRangeException(nameof(shape), shape, "Unknown stress graph shape.")
    };

    private static int AlternateParentIndex(int primaryParentIndex)
    {
        var firstSiblingIndex = (4 * ((primaryParentIndex - 1) / 4)) + 1;
        return firstSiblingIndex +
               ((primaryParentIndex - firstSiblingIndex + 1) % 4);
    }

    private static string NodeKind(int nodeIndex) => nodeIndex switch
    {
        0 => "root",
        _ when nodeIndex % 5 == 0 => "evidence",
        _ when nodeIndex % 10 == 2 => "objection",
        _ => "claim"
    };

    private static bool IsActiveEvidenceNode(int nodeIndex) =>
        nodeIndex > 0 && (nodeIndex % 5 == 0 || nodeIndex % 10 == 2);

    private static decimal EvidenceScore(int nodeIndex) =>
        35m + (5m * ((nodeIndex / 5) % 7));

    private static string EvidenceType(int nodeIndex)
    {
        string[] evidenceTypes =
        [
            "experimental",
            "media-analysis",
            "observational",
            "textual",
            "video"
        ];
        return evidenceTypes[(nodeIndex / 5) % evidenceTypes.Length];
    }

    private static decimal EdgeImportance(int nodeIndex) =>
        nodeIndex % 2 == 1 ? 1.001m : 0.999m;

    private static decimal EdgeLogWeight(int nodeIndex) =>
        (decimal)Math.Log((double)EdgeImportance(nodeIndex));

    private static void ValidateCatalogSpecification(StressGraphSeedSpec specification)
    {
        var catalogMatch = StressGraphSeedCatalog.All.SingleOrDefault(candidate =>
            string.Equals(candidate.Id, specification.Id, StringComparison.Ordinal));
        if (catalogMatch is null || catalogMatch != specification)
        {
            throw new ArgumentException(
                "The deterministic fixture specification must be an unchanged canonical stress catalog entry.",
                nameof(specification));
        }
    }
}
