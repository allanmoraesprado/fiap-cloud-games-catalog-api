using CatalogApi.Application.Dtos.Library;

namespace CatalogApi.Application.Interfaces;

public interface ILibraryService
{
    // M4: starts the asynchronous purchase by publishing OrderPlacedEvent.
    // Does NOT write the library (that happens in M5 on approved payment).
    Task<OrderPlacedResponse> AcquireAsync(Guid gameId, CancellationToken ct = default);

    Task<IReadOnlyList<UserGameResponse>> GetMyLibraryAsync(CancellationToken ct = default);
    Task<IReadOnlyList<UserGameResponse>> GetUserLibraryAsync(Guid userId, CancellationToken ct = default);
}
