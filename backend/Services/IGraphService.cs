using Backend.Models.Dto;

namespace Backend.Services;

public interface IGraphService
{
    Task<GraphDto?> GetBySlugAsync(string slug, CancellationToken cancellationToken = default);
    Task<bool> DeleteNodeAsync(string slug, string nodeId, CancellationToken cancellationToken = default);
    Task<bool> AddNodeAsync(string slug, GraphNodeDto node, string? parentID = null,
        string edgeKind = "support", int importanceToParent = 1, CancellationToken cancellationToken = default);
    Task<bool> UpdateNodeAsync(string slug, string nodeId, GraphNodeUpdateDto node,
        CancellationToken cancellationToken = default);
    Task<bool> AddEdgeAsync(string slug, GraphEdgeDto edge, CancellationToken cancellationToken = default);
    Task<bool> UpdateEdgeAsync(string slug, string edgeId, GraphEdgeUpdateDto edge,
        CancellationToken cancellationToken = default);
    Task ResetDatabaseAsync(CancellationToken cancellationToken = default);
}
