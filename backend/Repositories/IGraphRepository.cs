using Backend.Models.Domain;

namespace Backend.Repositories;

public interface IGraphRepository
{
    Task<Graph?> GetBySlugAsync(string slug, CancellationToken cancellationToken = default);
    Task<bool> DeleteNodeAsync(string slug, string nodeId, CancellationToken cancellationToken = default);
    Task<bool> AddNodeAsync(string slug, string kind, string title, string bodyText,
        string? parentID = null, CancellationToken cancellationToken = default);
}
