using Backend.Models.Domain;

namespace Backend.Repositories;

public interface IGraphRepository
{
    Task<Graph?> GetBySlugAsync(string slug, CancellationToken cancellationToken = default);
    Task<bool> DeleteNodeAsync(string slug, string nodeId, CancellationToken cancellationToken = default);
}
