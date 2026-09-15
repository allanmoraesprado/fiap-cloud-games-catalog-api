using System.Text.Json;
using CatalogApi.Application.Interfaces;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CatalogApi.Infrastructure.Caching;

// Cache-aside over IDistributedCache (Redis). Values are stored as JSON with an absolute TTL.
// Every backend failure is logged as a warning and the request is served from the database
// (outcome BYPASS): Redis being down must never break CatalogAPI.
public sealed class RedisCatalogCache : ICatalogCache
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IDistributedCache _cache;
    private readonly CacheOutcomeContext _outcome;
    private readonly TimeSpan _ttl;
    private readonly ILogger<RedisCatalogCache> _logger;

    public RedisCatalogCache(
        IDistributedCache cache,
        IOptions<RedisSettings> options,
        CacheOutcomeContext outcome,
        ILogger<RedisCatalogCache> logger)
    {
        _cache = cache;
        _outcome = outcome;
        _ttl = TimeSpan.FromSeconds(Math.Max(1, options.Value.DefaultTtlSeconds));
        _logger = logger;
    }

    public async Task<T> GetOrCreateAsync<T>(string key, Func<CancellationToken, Task<T>> factory, CancellationToken ct = default)
    {
        byte[]? cached;
        try
        {
            cached = await _cache.GetAsync(key, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Cache BYPASS for {Key}: Redis unavailable, serving from the database.", key);
            _outcome.Record(CacheOutcome.Bypass);
            return await factory(ct);
        }

        if (cached is not null)
        {
            try
            {
                var hit = JsonSerializer.Deserialize<T>(cached, JsonOptions);
                if (hit is not null)
                {
                    _logger.LogInformation("Cache HIT for {Key}.", key);
                    _outcome.Record(CacheOutcome.Hit);
                    return hit;
                }
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "Cache entry for {Key} is unreadable; treating it as a MISS.", key);
            }
        }

        _logger.LogInformation("Cache MISS for {Key}; loading from the database.", key);
        _outcome.Record(CacheOutcome.Miss);
        var value = await factory(ct);

        try
        {
            await _cache.SetAsync(
                key,
                JsonSerializer.SerializeToUtf8Bytes(value, JsonOptions),
                new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = _ttl },
                ct);
            _logger.LogInformation("Cache SET for {Key} (ttl {TtlSeconds}s).", key, _ttl.TotalSeconds);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Cache SET failed for {Key}; response already served from the database.", key);
        }

        return value;
    }

    public async Task RemoveAsync(string key, CancellationToken ct = default)
    {
        try
        {
            await _cache.RemoveAsync(key, ct);
            _logger.LogInformation("Cache INVALIDATED {Key}.", key);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Bounded staleness: the entry still expires by TTL.
            _logger.LogWarning(ex, "Cache invalidation failed for {Key}; the entry expires by TTL.", key);
        }
    }
}
