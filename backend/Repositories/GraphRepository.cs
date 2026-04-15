using Backend.Models.Domain;

namespace Backend.Repositories;

public class GraphRepository : IGraphRepository
{
    public Task<Graph?> GetGraphAsync(CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }
}
