using CatalogApi.Application.Interfaces;

namespace CatalogApi.Infrastructure.Caching;

// Scoped per request: records the outcome of the cached read performed while handling it,
// so CacheOutcomeHeaderMiddleware can emit the X-FCG-Cache header without touching controllers.
public sealed class CacheOutcomeContext
{
    public CacheOutcome? Last { get; private set; }

    public void Record(CacheOutcome outcome) => Last = outcome;
}
