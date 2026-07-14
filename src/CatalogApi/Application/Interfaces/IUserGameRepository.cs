using CatalogApi.Domain;

namespace CatalogApi.Application.Interfaces;

public interface IUserGameRepository
{
    Task<bool> ExistsAsync(Guid userId, Guid gameId, CancellationToken ct = default);
    Task AddAsync(UserGame userGame, CancellationToken ct = default);
}
