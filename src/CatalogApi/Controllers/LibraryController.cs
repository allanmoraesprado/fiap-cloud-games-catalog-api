using CatalogApi.Application.Dtos.Library;
using CatalogApi.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CatalogApi.Controllers;

[ApiController]
[Route("api/library")]
[Authorize]
public class LibraryController : ControllerBase
{
    private readonly ILibraryService _library;
    public LibraryController(ILibraryService library) => _library = library;

    // Read-only in M3. POST acquire/{gameId} (the purchase producer) is added in M4.

    [HttpGet("my-games")]
    public async Task<ActionResult<IReadOnlyList<UserGameResponse>>> MyGames(CancellationToken ct)
        => Ok(await _library.GetMyLibraryAsync(ct));

    [HttpGet("user/{userId:guid}")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<ActionResult<IReadOnlyList<UserGameResponse>>> UserLibrary(Guid userId, CancellationToken ct)
        => Ok(await _library.GetUserLibraryAsync(userId, ct));
}
