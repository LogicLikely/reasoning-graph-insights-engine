using Backend.Models.Domain;

namespace Backend.Services;

public interface IGraphService
{
    Task<Graph?> GetGraphAsync(CancellationToken cancellationToken = default);
}
