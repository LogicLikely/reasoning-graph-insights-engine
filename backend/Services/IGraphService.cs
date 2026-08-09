using Backend.Models.Dto;

namespace Backend.Services;

public interface IGraphService
{
    Task<GraphDto?> GetBySlugAsync(string slug, CancellationToken cancellationToken = default);
    Task<bool> DeleteNodeAsync(string slug, string nodeId, CancellationToken cancellationToken = default);
    Task<bool> AddNodeAsync(string slug, GraphNodeDto node, string? parentID = null,
        string edgeKind = "support", decimal importanceToParent = 1m, CancellationToken cancellationToken = default);
    Task<bool> UpdateNodeAsync(string slug, string nodeId, GraphNodeUpdateDto node,
        CancellationToken cancellationToken = default);
    Task<bool> AddEdgeAsync(string slug, GraphEdgeDto edge, CancellationToken cancellationToken = default);
    Task<bool> UpdateEdgeAsync(string slug, string edgeId, GraphEdgeUpdateDto edge,
        CancellationToken cancellationToken = default);
    Task<List<string>?> GetMinimalCounterSetAsync(string slug, string targetNodeId,
        CancellationToken cancellationToken = default);
    Task<List<string>?> GetMinimalCounterSetAsync(string slug, string targetNodeId,
        GraphDto graphContext, CancellationToken cancellationToken = default);
    Task<List<string>?> GetEvidenceImpactRankingAsync(string slug, string targetNodeId,
        CancellationToken cancellationToken = default);
    Task<List<string>?> GetEvidenceImpactRankingAsync(string slug, string targetNodeId,
        GraphDto graphContext, CancellationToken cancellationToken = default);
    Task ResetDatabaseAsync(CancellationToken cancellationToken = default);
}
