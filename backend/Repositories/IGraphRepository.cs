using Backend.Models.Domain;
using Backend.Models.Dto;
using Backend.Seeding;

namespace Backend.Repositories;

public interface IGraphRepository
{
    Task<IReadOnlyList<GraphSummary>> GetSummariesAsync(CancellationToken cancellationToken = default);
    Task<Graph?> GetBySlugAsync(string slug, CancellationToken cancellationToken = default);
    Task<bool> DeleteNodeAsync(string slug, string nodeId, CancellationToken cancellationToken = default);
    Task<bool> AddNodeAsync(string slug, GraphNodeDto node, string? parentID = null,
        string edgeKind = "support",
        decimal probabilityGivenParent = 0.5m, decimal probabilityGivenNotParent = 0.5m,
        CancellationToken cancellationToken = default);
    Task<bool> UpdateNodeAsync(string slug, string nodeId, GraphNodeUpdateDto node,
        CancellationToken cancellationToken = default);
    Task<bool> AddEdgeAsync(string slug, GraphEdgeDto edge, CancellationToken cancellationToken = default);
    Task<bool> UpdateEdgeAsync(string slug, string edgeId, GraphEdgeUpdateDto edge,
        CancellationToken cancellationToken = default);
    Task UpdateNodePosteriorOddsBatchAsync(
        int graphId,
        IReadOnlyDictionary<string, decimal> posteriorOddsByNodeId,
        CancellationToken cancellationToken = default);
    Task ResetDatabaseAsync(
        IReadOnlyList<StressGraphSeedSpec> stressGraphs,
        CancellationToken cancellationToken = default);
}
