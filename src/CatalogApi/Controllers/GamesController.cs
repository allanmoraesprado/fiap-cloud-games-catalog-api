using CatalogApi.Application.Dtos.Games;
using CatalogApi.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CatalogApi.Controllers;

[ApiController]
[Route("api/games")]
[Authorize]
public class GamesController : ControllerBase
{
    private readonly IGameService _games;
    private readonly IGameQueryService _queries;

    public GamesController(IGameService games, IGameQueryService queries)
    {
        _games = games;
        _queries = queries;
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<GameResponse>>> List(CancellationToken ct)
        => Ok(await _queries.ListActiveGamesAsync(ct));

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<GameResponse>> Get(Guid id, CancellationToken ct)
        => Ok(await _games.GetAsync(id, ct));

    [HttpPost]
    [Authorize(Policy = "AdminOnly")]
    public async Task<ActionResult<GameResponse>> Create([FromBody] GameRequest request, CancellationToken ct)
    {
        var game = await _games.CreateAsync(request, ct);
        return CreatedAtAction(nameof(Get), new { id = game.Id }, game);
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<ActionResult<GameResponse>> Update(Guid id, [FromBody] GameRequest request, CancellationToken ct)
        => Ok(await _games.UpdateAsync(id, request, ct));

    [HttpDelete("{id:guid}")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await _games.DeleteAsync(id, ct);
        return NoContent();
    }
}
