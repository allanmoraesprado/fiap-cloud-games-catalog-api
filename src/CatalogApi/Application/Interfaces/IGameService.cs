using CatalogApi.Application.Dtos.Games;

namespace CatalogApi.Application.Interfaces;

public interface IGameService
{
    Task<GameResponse> CreateAsync(GameRequest request, CancellationToken ct = default);
    Task<GameResponse> UpdateAsync(Guid id, GameRequest request, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);
    Task<GameResponse> GetAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<GameResponse>> ListAsync(CancellationToken ct = default);
}
