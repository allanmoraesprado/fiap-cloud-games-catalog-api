namespace CatalogApi.Application.Interfaces;

public enum CacheOutcome
{
    Hit,     // served from the cache
    Miss,    // loaded from the database and stored in the cache
    Bypass   // cache disabled or unavailable; served from the database
}

// Cache-aside abstraction for the read side (Phase 3, Redis). Implementations must never
// fail a request because of the cache backend: any backend error falls back to the factory.
public interface ICatalogCache
{
    Task<T> GetOrCreateAsync<T>(string key, Func<CancellationToken, Task<T>> factory, CancellationToken ct = default);
    Task RemoveAsync(string key, CancellationToken ct = default);
}
