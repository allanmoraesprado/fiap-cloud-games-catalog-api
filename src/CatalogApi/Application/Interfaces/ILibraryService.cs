using CatalogApi.Application.Dtos.Library;

namespace CatalogApi.Application.Interfaces;

// M3: read-only. AcquireAsync (library write) is added in M4 as the purchase producer.
public interface ILibraryService
{
    Task<IReadOnlyList<UserGameResponse>> GetMyLibraryAsync(CancellationToken ct = default);
    Task<IReadOnlyList<UserGameResponse>> GetUserLibraryAsync(Guid userId, CancellationToken ct = default);
}
