using CatalogApi.Application.Dtos.Search;
using CatalogApi.Application.Interfaces;
using CatalogApi.Domain;
using CatalogApi.Observability;
using Microsoft.Extensions.Logging;

namespace CatalogApi.Application.Services;

// PostgreSQL is the source of truth; the search index is a derived, eventually consistent copy.
// No distributed transaction: the database commit happens first, the index is updated right
// after (best effort) and SearchIndexReconciler repairs anything that was missed.
public sealed class GameSearchSync : IGameSearchSync
{
    private readonly IGameSearchIndexer _indexer;
    private readonly IGameRepository _games;
    private readonly ILogger<GameSearchSync> _logger;
    private readonly int _batchSize;

    public GameSearchSync(IGameSearchIndexer indexer, IGameRepository games, ILogger<GameSearchSync> logger, int batchSize = 500)
    {
        _indexer = indexer;
        _games = games;
        _logger = logger;
        _batchSize = Math.Max(1, batchSize);
    }

    public async Task TryIndexAsync(Game game, CancellationToken ct = default)
    {
        try
        {
            await _indexer.IndexAsync(game, ct);
            FcgMetrics.SearchIndexOperations.WithLabels("upsert", "success").Inc();
            _logger.LogInformation("Search index updated for game {GameId} ({Title}, active={IsActive}).", game.Id, game.Title, game.IsActive);
        }
        catch (Exception ex)
        {
            // Never fail the write that already committed: the reconciliation job picks the
            // change up through the game's UpdatedAt.
            FcgMetrics.SearchIndexOperations.WithLabels("upsert", "failure").Inc();
            _logger.LogWarning(ex, "Search index update deferred for game {GameId}; the reconciliation job will retry.", game.Id);
        }
    }

    public async Task<int> SyncChangedSinceAsync(DateTime? since, CancellationToken ct = default)
    {
        var changed = await _games.ListUpdatedSinceAsync(since, ct);
        foreach (var batch in changed.Chunk(_batchSize))
        {
            try
            {
                await _indexer.IndexManyAsync(batch, ct);
                FcgMetrics.SearchIndexOperations.WithLabels("bulk", "success").Inc();
            }
            catch
            {
                FcgMetrics.SearchIndexOperations.WithLabels("bulk", "failure").Inc();
                throw;
            }
        }
        return changed.Count;
    }

    public async Task<ReindexResponse> ReindexAllAsync(bool recreate, CancellationToken ct = default)
    {
        try
        {
            if (recreate) await _indexer.RecreateIndexAsync(ct);
            else await _indexer.EnsureIndexAsync(ct);

            var count = await SyncChangedSinceAsync(null, ct);
            await _indexer.RefreshAsync(ct);

            FcgMetrics.SearchIndexOperations.WithLabels("reindex", "success").Inc();
            _logger.LogInformation("Search index {Index} rebuilt: {Count} games indexed (recreated={Recreated}).", _indexer.IndexName, count, recreate);
            return new ReindexResponse(_indexer.IndexName, count, recreate);
        }
        catch
        {
            FcgMetrics.SearchIndexOperations.WithLabels("reindex", "failure").Inc();
            throw;
        }
    }
}
