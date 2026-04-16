using Backend.Models.Dto;

namespace Backend.Services;

public interface IGraphService
{
    Task<GraphDto?> GetBySlugAsync(string slug, CancellationToken cancellationToken = default);
}
