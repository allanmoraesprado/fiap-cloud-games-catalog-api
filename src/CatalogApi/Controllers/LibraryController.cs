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

    // Starts the asynchronous purchase: publishes OrderPlacedEvent and returns 202.
    // The game is NOT added to the library here (that happens in M5 on approved payment).
    [HttpPost("acquire/{gameId:guid}")]
    [ProducesResponseType(typeof(OrderPlacedResponse), StatusCodes.Status202Accepted)]
    public async Task<ActionResult<OrderPlacedResponse>> Acquire(Guid gameId, CancellationToken ct)
    {
        var result = await _library.AcquireAsync(gameId, ct);
        return Accepted(result);
    }

    [HttpGet("my-games")]
    public async Task<ActionResult<IReadOnlyList<UserGameResponse>>> MyGames(CancellationToken ct)
        => Ok(await _library.GetMyLibraryAsync(ct));

    [HttpGet("user/{userId:guid}")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<ActionResult<IReadOnlyList<UserGameResponse>>> UserLibrary(Guid userId, CancellationToken ct)
        => Ok(await _library.GetUserLibraryAsync(userId, ct));
}
