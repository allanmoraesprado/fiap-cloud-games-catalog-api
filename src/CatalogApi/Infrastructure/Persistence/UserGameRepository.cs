using CatalogApi.Application.Interfaces;
using CatalogApi.Domain;
using Microsoft.EntityFrameworkCore;

namespace CatalogApi.Infrastructure.Persistence;

public class UserGameRepository : IUserGameRepository
{
    private readonly CatalogDbContext _db;
    public UserGameRepository(CatalogDbContext db) => _db = db;

    public Task<bool> ExistsAsync(Guid userId, Guid gameId, CancellationToken ct = default)
        => _db.UserGames.AnyAsync(x => x.UserId == userId && x.GameId == gameId, ct);

    public async Task AddAsync(UserGame userGame, CancellationToken ct = default)
        => await _db.UserGames.AddAsync(userGame, ct);
}
