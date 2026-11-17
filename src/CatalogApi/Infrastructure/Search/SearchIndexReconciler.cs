using CatalogApi.Application.Interfaces;
using CatalogApi.Observability;
using Microsoft.Extensions.Options;

namespace CatalogApi.Infrastructure.Search;

// Background job that keeps the search index converging to PostgreSQL:
// - first run (and whenever the index had to be created, e.g. after data loss): full load;
// - next runs: games whose UpdatedAt is at or after the last successful run minus an overlap
//   (covers commit latency and clock skew between replicas);
// - failures (engine down) keep the watermark, so everything missed is retried on the next run.
// Upserts are idempotent, so several API replicas may run it concurrently.
public sealed class SearchIndexReconciler : BackgroundService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly IGameSearchIndexer _indexer;
    private readonly ILogger<SearchIndexReconciler> _logger;
    private readonly TimeSpan _interval;
    private readonly TimeSpan _overlap;

    public SearchIndexReconciler(
        IServiceScopeFactory scopes,
        IGameSearchIndexer indexer,
        IOptions<ElasticsearchSettings> options,
        ILogger<SearchIndexReconciler> logger)
    {
        _scopes = scopes;
        _indexer = indexer;
        _logger = logger;
        _interval = TimeSpan.FromSeconds(Math.Max(1, options.Value.ReconcileIntervalSeconds));
        _overlap = TimeSpan.FromSeconds(Math.Max(0, options.Value.ReconcileOverlapSeconds));
    }

    // Start time of the last successful run; null until the first full load succeeds.
    public DateTime? Watermark { get; private set; }

    public async Task<int> RunOnceAsync(CancellationToken ct = default)
    {
        try
        {
            if (await _indexer.EnsureIndexAsync(ct))
                Watermark = null;

            var startedAt = DateTime.UtcNow;
            var since = Watermark?.Subtract(_overlap);

            using var scope = _scopes.CreateScope();
            var sync = scope.ServiceProvider.GetRequiredService<IGameSearchSync>();
            var count = await sync.SyncChangedSinceAsync(since, ct);

            if (since is null)
                _logger.LogInformation("Search index {Index} full load: {Count} games indexed.", _indexer.IndexName, count);
            else if (count > 0)
                _logger.LogInformation("Search index {Index} reconciled: {Count} games changed since {Since:O}.", _indexer.IndexName, count, since);

            Watermark = startedAt;
            FcgMetrics.SearchIndexOperations.WithLabels("reconcile", "success").Inc();
            return count;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            FcgMetrics.SearchIndexOperations.WithLabels("reconcile", "failure").Inc();
            throw;
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Search index reconciliation failed; retrying in {Interval}s.", _interval.TotalSeconds);
            }

            try { await Task.Delay(_interval, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }
}
