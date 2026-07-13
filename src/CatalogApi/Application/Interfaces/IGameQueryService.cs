using CatalogApi.Application.Dtos.Games;
using CatalogApi.Application.Dtos.Library;

namespace CatalogApi.Application.Interfaces;

public interface IGameQueryService
{
    Task<IReadOnlyList<GameResponse>> ListActiveGamesAsync(CancellationToken ct = default);
    Task<IReadOnlyList<UserGameResponse>> ListUserLibraryAsync(Guid userId, CancellationToken ct = default);
}
