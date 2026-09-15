using CatalogApi.Application.Dtos.Games;
using CatalogApi.Application.Interfaces;
using CatalogApi.Domain;
using CatalogApi.Domain.Exceptions;
using CatalogApi.Infrastructure.Caching;
using Microsoft.Extensions.Logging;

namespace CatalogApi.Application.Services;

public class GameService : IGameService
{
    private readonly IGameRepository _games;
    private readonly IUnitOfWork _uow;
    private readonly ICurrentUser _currentUser;
    private readonly ICatalogCache _cache;
    private readonly ILogger<GameService> _logger;

    public GameService(
        IGameRepository games,
        IUnitOfWork uow,
        ICurrentUser currentUser,
        ICatalogCache cache,
        ILogger<GameService> logger)
    {
        _games = games;
        _uow = uow;
        _currentUser = currentUser;
        _cache = cache;
        _logger = logger;
    }

    public async Task<GameResponse> CreateAsync(GameRequest request, CancellationToken ct = default)
    {
        EnsureAdmin();
        var game = new Game(request.Title, request.Description, request.Genre, request.Price, request.ReleaseDate);
        await _games.AddAsync(game, ct);
        await _uow.SaveChangesAsync(ct);
        _logger.LogInformation("Game created: {Title}", game.Title);

        // A new active game changes the public list.
        await _cache.RemoveAsync(CacheKeys.ActiveGames, ct);
        return Map(game);
    }

    public async Task<GameResponse> UpdateAsync(Guid id, GameRequest request, CancellationToken ct = default)
    {
        EnsureAdmin();
        var game = await _games.GetByIdAsync(id, ct) ?? throw new NotFoundException("Game not found.");
        game.Update(request.Title, request.Description, request.Genre, request.Price, request.ReleaseDate);
        await _games.UpdateAsync(game, ct);
        await _uow.SaveChangesAsync(ct);

        await InvalidateGameAsync(id, ct);
        return Map(game);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        EnsureAdmin();
        var game = await _games.GetByIdAsync(id, ct) ?? throw new NotFoundException("Game not found.");
        game.Deactivate();
        await _games.UpdateAsync(game, ct);
        await _uow.SaveChangesAsync(ct);

        await InvalidateGameAsync(id, ct);
    }

    // Cache-aside: served from Redis while valid; a NotFoundException thrown by the loader is
    // never cached (no negative caching).
    public Task<GameResponse> GetAsync(Guid id, CancellationToken ct = default)
        => _cache.GetOrCreateAsync(CacheKeys.Game(id), async c =>
        {
            var game = await _games.GetByIdAsync(id, c) ?? throw new NotFoundException("Game not found.");
            return Map(game);
        }, ct);

    // Explicit invalidation for admin writes: the public list and the game entry itself.
    private async Task InvalidateGameAsync(Guid id, CancellationToken ct)
    {
        await _cache.RemoveAsync(CacheKeys.ActiveGames, ct);
        await _cache.RemoveAsync(CacheKeys.Game(id), ct);
    }

    public async Task<IReadOnlyList<GameResponse>> ListAsync(CancellationToken ct = default)
    {
        var games = await _games.ListAsync(ct);
        return games.Select(Map).ToList();
    }

    private void EnsureAdmin()
    {
        if (!_currentUser.IsAdmin)
            throw new ForbiddenException("Only Admin can perform this operation.");
    }

    private static GameResponse Map(Game g) =>
        new(g.Id, g.Title, g.Description, g.Genre, g.Price, g.ReleaseDate, g.IsActive);
}
