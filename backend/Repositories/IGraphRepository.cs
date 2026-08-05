using Backend.Models.Domain;
using Backend.Models.Dto;

namespace Backend.Repositories;

public interface IGraphRepository
{
    Task<Graph?> GetBySlugAsync(string slug, CancellationToken cancellationToken = default);
    Task<bool> DeleteNodeAsync(string slug, string nodeId, CancellationToken cancellationToken = default);
    Task<bool> AddNodeAsync(string slug, GraphNodeDto node, string? parentID = null,
        string edgeKind = "support", decimal importanceToParent = 1m, CancellationToken cancellationToken = default);
    Task<bool> UpdateNodeAsync(string slug, string nodeId, GraphNodeUpdateDto node,
        CancellationToken cancellationToken = default);
    Task<bool> AddEdgeAsync(string slug, GraphEdgeDto edge, CancellationToken cancellationToken = default);
    Task<bool> UpdateEdgeAsync(string slug, string edgeId, GraphEdgeUpdateDto edge,
        CancellationToken cancellationToken = default);
    Task UpdateNodeLogOddsBatchAsync(
        int graphId,
        IReadOnlyDictionary<string, decimal> logOddsByNodeId,
        CancellationToken cancellationToken = default);
    Task ResetDatabaseAsync(CancellationToken cancellationToken = default);
}
