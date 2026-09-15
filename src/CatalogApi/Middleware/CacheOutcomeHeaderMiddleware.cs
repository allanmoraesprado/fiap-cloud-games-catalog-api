using CatalogApi.Infrastructure.Caching;
using Microsoft.Extensions.Options;

namespace CatalogApi.Middleware;

// Emits the diagnostic X-FCG-Cache header (HIT / MISS / BYPASS) when the request performed a
// cached read. Development aid for demos; disabled with Redis__ExposeOutcomeHeader=false.
public class CacheOutcomeHeaderMiddleware
{
    public const string HeaderName = "X-FCG-Cache";

    private readonly RequestDelegate _next;
    private readonly bool _enabled;

    public CacheOutcomeHeaderMiddleware(RequestDelegate next, IOptions<RedisSettings> options)
    {
        _next = next;
        _enabled = options.Value.ExposeOutcomeHeader;
    }

    public async Task InvokeAsync(HttpContext context, CacheOutcomeContext outcome)
    {
        if (_enabled)
        {
            context.Response.OnStarting(() =>
            {
                if (outcome.Last is { } result)
                    context.Response.Headers[HeaderName] = result.ToString().ToUpperInvariant();
                return Task.CompletedTask;
            });
        }

        await _next(context);
    }
}
