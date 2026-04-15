using Backend.Models.Domain;

namespace Backend.Repositories;

public interface IGraphRepository
{
    Task<Graph?> GetGraphAsync(CancellationToken cancellationToken = default);
}
