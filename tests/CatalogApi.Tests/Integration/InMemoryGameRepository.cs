using CatalogApi.Application.Interfaces;
using CatalogApi.Domain;

namespace CatalogApi.Tests.Integration;

// Stand-in for PostgreSQL in search integration tests: keeps games in memory and implements the
// same UpdatedAt-based change feed the reconciliation job relies on.
public sealed class InMemoryGameRepository : IGameRepository, IUnitOfWork
{
    private readonly List<Game> _games = new();

    public IReadOnlyList<Game> All => _games;

    public Task<Game?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => Task.FromResult(_games.FirstOrDefault(g => g.Id == id));

    public Task<IReadOnlyList<Game>> ListAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<Game>>(_games.OrderBy(g => g.Title).ToList());

    public Task<IReadOnlyList<Game>> ListUpdatedSinceAsync(DateTime? since, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<Game>>(_games
            .Where(g => since is null || g.UpdatedAt >= since)
            .OrderBy(g => g.UpdatedAt)
            .ToList());

    public Task AddAsync(Game game, CancellationToken ct = default)
    {
        _games.Add(game);
        return Task.CompletedTask;
    }

    public Task UpdateAsync(Game game, CancellationToken ct = default) => Task.CompletedTask;

    public Task<int> SaveChangesAsync(CancellationToken ct = default) => Task.FromResult(1);
}
