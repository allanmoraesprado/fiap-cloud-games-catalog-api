using CatalogApi.Domain;

namespace CatalogApi.Application.Interfaces;

public interface IGameRepository
{
    Task<Game?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<Game>> ListAsync(CancellationToken ct = default);
    Task AddAsync(Game game, CancellationToken ct = default);
    Task UpdateAsync(Game game, CancellationToken ct = default);
}
