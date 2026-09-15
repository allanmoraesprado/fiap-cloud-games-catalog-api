namespace CatalogApi.Infrastructure.Caching;

// Cache key catalogue. Keys are prefixed with "fcg:catalog:" by the Redis cache instance name.
// Only public catalog data and library listings are cached: never tokens, sessions or
// authorization decisions.
public static class CacheKeys
{
    public const string InstancePrefix = "fcg:catalog:";

    public const string ActiveGames = "games:active";
    public static string Game(Guid gameId) => $"game:{gameId}";
    public static string Library(Guid userId) => $"library:{userId}";

    // Bounded label value for metrics (never the id part of the key).
    public static string TargetOf(string key) =>
        key == ActiveGames ? "games"
        : key.StartsWith("game:", StringComparison.Ordinal) ? "game"
        : key.StartsWith("library:", StringComparison.Ordinal) ? "library"
        : "other";
}
