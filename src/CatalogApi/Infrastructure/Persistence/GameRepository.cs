using CatalogApi.Application.Interfaces;
using CatalogApi.Domain;
using Microsoft.EntityFrameworkCore;

namespace CatalogApi.Infrastructure.Persistence;

public class GameRepository : IGameRepository
{
    private readonly CatalogDbContext _db;
    public GameRepository(CatalogDbContext db) => _db = db;

    public Task<Game?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => _db.Games.FirstOrDefaultAsync(x => x.Id == id, ct);

    public async Task<IReadOnlyList<Game>> ListAsync(CancellationToken ct = default)
        => await _db.Games.AsNoTracking().OrderBy(g => g.Title).ToListAsync(ct);

    public async Task AddAsync(Game game, CancellationToken ct = default)
        => await _db.Games.AddAsync(game, ct);

    public Task UpdateAsync(Game game, CancellationToken ct = default)
    {
        _db.Games.Update(game);
        return Task.CompletedTask;
    }
}
