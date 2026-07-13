using CatalogApi.Application.Dtos.Games;
using CatalogApi.Application.Interfaces;
using CatalogApi.Domain;
using CatalogApi.Domain.Exceptions;
using Microsoft.Extensions.Logging;

namespace CatalogApi.Application.Services;

public class GameService : IGameService
{
    private readonly IGameRepository _games;
    private readonly IUnitOfWork _uow;
    private readonly ICurrentUser _currentUser;
    private readonly ILogger<GameService> _logger;

    public GameService(
        IGameRepository games,
        IUnitOfWork uow,
        ICurrentUser currentUser,
        ILogger<GameService> logger)
    {
        _games = games;
        _uow = uow;
        _currentUser = currentUser;
        _logger = logger;
    }

    public async Task<GameResponse> CreateAsync(GameRequest request, CancellationToken ct = default)
    {
        EnsureAdmin();
        var game = new Game(request.Title, request.Description, request.Genre, request.Price, request.ReleaseDate);
        await _games.AddAsync(game, ct);
        await _uow.SaveChangesAsync(ct);
        _logger.LogInformation("Game created: {Title}", game.Title);
        return Map(game);
    }

    public async Task<GameResponse> UpdateAsync(Guid id, GameRequest request, CancellationToken ct = default)
    {
        EnsureAdmin();
        var game = await _games.GetByIdAsync(id, ct) ?? throw new NotFoundException("Game not found.");
        game.Update(request.Title, request.Description, request.Genre, request.Price, request.ReleaseDate);
        await _games.UpdateAsync(game, ct);
        await _uow.SaveChangesAsync(ct);
        return Map(game);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        EnsureAdmin();
        var game = await _games.GetByIdAsync(id, ct) ?? throw new NotFoundException("Game not found.");
        game.Deactivate();
        await _games.UpdateAsync(game, ct);
        await _uow.SaveChangesAsync(ct);
    }

    public async Task<GameResponse> GetAsync(Guid id, CancellationToken ct = default)
    {
        var game = await _games.GetByIdAsync(id, ct) ?? throw new NotFoundException("Game not found.");
        return Map(game);
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
