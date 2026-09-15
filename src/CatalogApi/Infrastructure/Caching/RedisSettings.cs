namespace CatalogApi.Infrastructure.Caching;

// Bound from the "Redis" configuration section (Redis__* environment variables).
// Local/dev placeholders only; Redis has no auth in the local stack.
public class RedisSettings
{
    public bool Enabled { get; set; } = true;

    // StackExchange.Redis configuration string. abortConnect=false + short timeouts keep the
    // API responsive when Redis is down (the cache then falls back to PostgreSQL).
    public string ConnectionString { get; set; } = "localhost:6379,abortConnect=false,connectTimeout=1000,syncTimeout=1000";

    // Short TTL on purpose: easy to demonstrate locally, bounded staleness.
    public int DefaultTtlSeconds { get; set; } = 60;

    // Adds the diagnostic X-FCG-Cache response header (HIT / MISS / BYPASS). Development aid;
    // set to false where the header is not wanted.
    public bool ExposeOutcomeHeader { get; set; } = true;
}
