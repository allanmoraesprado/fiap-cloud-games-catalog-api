using CatalogApi.Application.Dtos.Library;
using CatalogApi.Application.Interfaces;
using CatalogApi.Domain.Exceptions;

namespace CatalogApi.Application.Services;

// M3: read-only library access. The acquire/purchase write path is added in M4.
public class LibraryService : ILibraryService
{
    private readonly ICurrentUser _currentUser;
    private readonly IGameQueryService _gameQueries;

    public LibraryService(ICurrentUser currentUser, IGameQueryService gameQueries)
    {
        _currentUser = currentUser;
        _gameQueries = gameQueries;
    }

    public async Task<IReadOnlyList<UserGameResponse>> GetMyLibraryAsync(CancellationToken ct = default)
    {
        if (!_currentUser.IsAuthenticated || _currentUser.UserId is null)
            throw new UnauthorizedException("User must be authenticated.");
        return await _gameQueries.ListUserLibraryAsync(_currentUser.UserId.Value, ct);
    }

    public async Task<IReadOnlyList<UserGameResponse>> GetUserLibraryAsync(Guid userId, CancellationToken ct = default)
    {
        if (!_currentUser.IsAdmin)
            throw new ForbiddenException("Only Admin can view another user's library.");
        return await _gameQueries.ListUserLibraryAsync(userId, ct);
    }
}
