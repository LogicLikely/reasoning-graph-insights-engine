using Backend.Models.Dto;

namespace Backend.Services;

public interface IGraphService
{
    Task<GraphDto?> GetBySlugAsync(string slug, CancellationToken cancellationToken = default);
    Task<bool> DeleteNodeAsync(string slug, string nodeId, CancellationToken cancellationToken = default);
    Task<bool> AddNodeAsync(string slug, string kind, string title, string bodyText,
        string? parentID = null, CancellationToken cancellationToken = default);
}
