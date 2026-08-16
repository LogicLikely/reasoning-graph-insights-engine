using System.Collections.ObjectModel;
using Backend.Insights.Contracts;
using Backend.Models.Domain;
using Backend.Seeding;

namespace Backend.Insights.Benchmarking;

public sealed record DeterministicStressGraphIdentity(
    string GeneratorVersion,
    string CorpusId,
    string CorpusFingerprint,
    string TopologyFingerprint,
    string InputFingerprint,
    string DatasetInputFingerprint);

/// <summary>
/// Immutable blueprint for an in-memory stress graph. Domain graph instances
/// are intentionally created on demand because the existing domain types are
/// mutable and likelihood recalculation mutates calculation contexts.
/// </summary>
public sealed class DeterministicStressGraphFixture
{
    private readonly ReadOnlyCollection<NodeBlueprint> _nodes;
    private readonly ReadOnlyCollection<EdgeBlueprint> _edges;

    internal DeterministicStressGraphFixture(
        StressGraphSeedSpec specification,
        DeterministicStressGraphIdentity identity,
        IEnumerable<NodeBlueprint> nodes,
        IEnumerable<EdgeBlueprint> edges)
    {
        ArgumentNullException.ThrowIfNull(specification);
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(nodes);
        ArgumentNullException.ThrowIfNull(edges);

        Specification = specification;
        Identity = identity;
        _nodes = Array.AsReadOnly(nodes.ToArray());
        _edges = Array.AsReadOnly(edges.ToArray());
    }

    public StressGraphSeedSpec Specification { get; }

    public DeterministicStressGraphIdentity Identity { get; }

    public string RootNodeId => DeterministicStressGraphFixtureFactory.NodeId(0);

    public string DeepestNodeId =>
        DeterministicStressGraphFixtureFactory.NodeId(Specification.NodeCount - 1);

    public int NodeCount => _nodes.Count;

    public int EdgeCount => _edges.Count;

    public Graph CreateGraph()
    {
        return new Graph
        {
            Id = Specification.GraphId,
            Slug = Specification.Slug,
            Title = Specification.Title,
            Description = Specification.Description,
            Nodes = _nodes.Select(node => new GraphNode
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
            }).ToList(),
            Edges = _edges.Select(edge => new GraphEdge
            {
                Id = edge.Id,
                From = edge.From,
                To = edge.To,
                Kind = edge.Kind,
                ImportanceToParent = edge.ImportanceToParent
            }).ToList()
        };
    }

    internal sealed record NodeBlueprint(
        string Id,
        string Kind,
        string Title,
        string BodyText,
        string Category,
        IReadOnlyList<string> Tags,
        decimal PriorOdds,
        decimal PosteriorOdds,
        EvidenceBlueprint? Evidence);

    internal sealed record EvidenceBlueprint(
        string Type,
        decimal Score,
        string Rationale);

    internal sealed record EdgeBlueprint(
        string Id,
        string From,
        string To,
        string Kind,
        decimal ImportanceToParent);
}
