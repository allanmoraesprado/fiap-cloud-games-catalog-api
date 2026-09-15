using CatalogApi.Application.Dtos.Games;
using CatalogApi.Application.Dtos.Library;
using CatalogApi.Application.Interfaces;

namespace CatalogApi.Infrastructure.Caching;

// Cache-aside decorator over the Dapper read model: GET /api/games and the user library
// listings are served from Redis while the entry is valid, otherwise loaded from PostgreSQL.
// Controllers and LibraryService keep depending on IGameQueryService unchanged.
public sealed class CachedGameQueryService : IGameQueryService
{
    private readonly IGameQueryService _inner;
    private readonly ICatalogCache _cache;

    public CachedGameQueryService(IGameQueryService inner, ICatalogCache cache)
    {
        _inner = inner;
        _cache = cache;
    }

    public Task<IReadOnlyList<GameResponse>> ListActiveGamesAsync(CancellationToken ct = default)
        => _cache.GetOrCreateAsync(CacheKeys.ActiveGames, c => _inner.ListActiveGamesAsync(c), ct);

    public Task<IReadOnlyList<UserGameResponse>> ListUserLibraryAsync(Guid userId, CancellationToken ct = default)
        => _cache.GetOrCreateAsync(CacheKeys.Library(userId), c => _inner.ListUserLibraryAsync(userId, c), ct);
}
