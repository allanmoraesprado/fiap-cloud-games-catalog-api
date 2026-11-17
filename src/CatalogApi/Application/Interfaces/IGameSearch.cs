using CatalogApi.Application.Dtos.Search;
using CatalogApi.Domain;

namespace CatalogApi.Application.Interfaces;

// Full-text game search backed by a dedicated search engine (Elasticsearch), independent of the
// relational read model. Implementations throw SearchUnavailableException when the engine cannot
// answer.
public interface IGameSearchService
{
    Task<GameSearchResponse> SearchAsync(GameSearchQuery query, CancellationToken ct = default);
}

// Write side of the search index. Documents are keyed by the game id, so every operation is an
// idempotent upsert. Implementations throw SearchUnavailableException on engine failures.
public interface IGameSearchIndexer
{
    string IndexName { get; }

    // Creates the index with its mapping when missing. Returns true when the index was created
    // (a brand-new or lost index must be fully rebuilt).
    Task<bool> EnsureIndexAsync(CancellationToken ct = default);

    Task RecreateIndexAsync(CancellationToken ct = default);

    // Upserts one game and waits until it is visible to searches.
    Task IndexAsync(Game game, CancellationToken ct = default);

    Task IndexManyAsync(IReadOnlyCollection<Game> games, CancellationToken ct = default);

    Task RefreshAsync(CancellationToken ct = default);
}

// Keeps the search index eventually consistent with PostgreSQL (the source of truth).
public interface IGameSearchSync
{
    // Best effort, called after the database commit: failures are logged and left to the
    // reconciliation job, never propagated to the caller.
    Task TryIndexAsync(Game game, CancellationToken ct = default);

    // Indexes every game changed at or after `since` (all games when null). Returns the count.
    Task<int> SyncChangedSinceAsync(DateTime? since, CancellationToken ct = default);

    Task<ReindexResponse> ReindexAllAsync(bool recreate, CancellationToken ct = default);
}
