using CatalogApi.Application.Dtos.Games;
using CatalogApi.Application.Dtos.Search;
using CatalogApi.Application.Interfaces;
using CatalogApi.Application.Validators;
using CatalogApi.Domain.Exceptions;
using CatalogApi.Observability;
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
    private readonly IGameSearchService _search;
    private readonly IGameSearchSync _searchSync;

    public GamesController(IGameService games, IGameQueryService queries, IGameSearchService search, IGameSearchSync searchSync)
    {
        _games = games;
        _queries = queries;
        _search = search;
        _searchSync = searchSync;
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<GameResponse>>> List(CancellationToken ct)
        => Ok(await _queries.ListActiveGamesAsync(ct));

    // Full-text search (Elasticsearch): fuzzy matching on title/genre/description, ordered by
    // relevance, active games only. 400 on invalid input, 503 when the search engine is unavailable.
    [HttpGet("search")]
    [ProducesResponseType(typeof(GameSearchResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<GameSearchResponse>> Search(
        [FromQuery] string? q,
        [FromQuery] string? genre,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 10,
        CancellationToken ct = default)
    {
        GameSearchQuery query;
        try
        {
            query = GameSearchValidator.ToQuery(q, genre, page, pageSize);
        }
        catch (DomainException)
        {
            FcgMetrics.SearchRequests.WithLabels("invalid").Inc();
            throw;
        }
        return Ok(await _search.SearchAsync(query, ct));
    }

    // Rebuilds the search index from PostgreSQL. recreate=true drops the index first (applies
    // mapping changes; searches return no results until the rebuild finishes).
    [HttpPost("search/reindex")]
    [Authorize(Policy = "AdminOnly")]
    [ProducesResponseType(typeof(ReindexResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<ReindexResponse>> Reindex([FromQuery] bool recreate = false, CancellationToken ct = default)
        => Ok(await _searchSync.ReindexAllAsync(recreate, ct));

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
