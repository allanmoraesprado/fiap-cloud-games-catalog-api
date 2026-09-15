using CatalogApi.Application.Interfaces;
using CatalogApi.Observability;

namespace CatalogApi.Infrastructure.Caching;

// Used when Redis:Enabled=false: every read goes straight to the database (outcome BYPASS)
// and invalidations are no-ops.
public sealed class NoOpCatalogCache : ICatalogCache
{
    private readonly CacheOutcomeContext _outcome;

    public NoOpCatalogCache(CacheOutcomeContext outcome) => _outcome = outcome;

    public Task<T> GetOrCreateAsync<T>(string key, Func<CancellationToken, Task<T>> factory, CancellationToken ct = default)
    {
        _outcome.Record(CacheOutcome.Bypass);
        FcgMetrics.CacheRequests.WithLabels("bypass").Inc();
        return factory(ct);
    }

    public Task RemoveAsync(string key, CancellationToken ct = default) => Task.CompletedTask;
}
