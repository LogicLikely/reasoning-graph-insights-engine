using Backend.Models.Domain;
using Backend.Repositories;

namespace Backend.Services;

public class GraphService : IGraphService
{
    private readonly IGraphRepository _graphRepository;

    public GraphService(IGraphRepository graphRepository)
    {
        _graphRepository = graphRepository;
    }

    public Task<Graph?> GetGraphAsync(CancellationToken cancellationToken = default)
    {
        return _graphRepository.GetGraphAsync(cancellationToken);
    }
}
