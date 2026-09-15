using CatalogApi.Application.Dtos.Games;
using CatalogApi.Application.Interfaces;
using CatalogApi.Infrastructure.Caching;
using FluentAssertions;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace CatalogApi.Tests;

// Exercises the cache-aside logic over an in-memory IDistributedCache (same contract as Redis)
// and over a backend that always fails (Redis down).
public class RedisCatalogCacheTests
{
    private static MemoryDistributedCache InMemory() => new(Options.Create(new MemoryDistributedCacheOptions()));

    private static RedisCatalogCache Build(IDistributedCache backend, CacheOutcomeContext outcome, int ttlSeconds = 60) =>
        new(backend, Options.Create(new RedisSettings { DefaultTtlSeconds = ttlSeconds }), outcome, NullLogger<RedisCatalogCache>.Instance);

    [Fact]
    public async Task First_call_misses_and_populates_then_second_call_hits()
    {
        var outcome = new CacheOutcomeContext();
        var cache = Build(InMemory(), outcome);
        var loads = 0;
        Task<string> Load(CancellationToken _) { loads++; return Task.FromResult("from-db"); }

        var first = await cache.GetOrCreateAsync("k", Load);
        var firstOutcome = outcome.Last;
        var second = await cache.GetOrCreateAsync("k", Load);

        first.Should().Be("from-db");
        second.Should().Be("from-db");
        firstOutcome.Should().Be(CacheOutcome.Miss);
        outcome.Last.Should().Be(CacheOutcome.Hit);
        loads.Should().Be(1);
    }

    [Fact]
    public async Task Remove_forces_the_next_call_to_miss_again()
    {
        var outcome = new CacheOutcomeContext();
        var cache = Build(InMemory(), outcome);
        var loads = 0;
        Task<string> Load(CancellationToken _) { loads++; return Task.FromResult("v" + loads); }

        await cache.GetOrCreateAsync("k", Load);
        await cache.RemoveAsync("k");
        var afterRemove = await cache.GetOrCreateAsync("k", Load);

        afterRemove.Should().Be("v2");
        outcome.Last.Should().Be(CacheOutcome.Miss);
        loads.Should().Be(2);
    }

    [Fact]
    public async Task Backend_failure_falls_back_to_the_loader_without_throwing()
    {
        var outcome = new CacheOutcomeContext();
        var cache = Build(new FailingDistributedCache(), outcome);
        var loads = 0;
        Task<string> Load(CancellationToken _) { loads++; return Task.FromResult("from-db"); }

        var value = await cache.GetOrCreateAsync("k", Load);
        var remove = () => cache.RemoveAsync("k");

        value.Should().Be("from-db");
        outcome.Last.Should().Be(CacheOutcome.Bypass);
        loads.Should().Be(1);
        await remove.Should().NotThrowAsync();
    }

    [Fact]
    public async Task Read_model_lists_survive_the_json_round_trip()
    {
        var outcome = new CacheOutcomeContext();
        var cache = Build(InMemory(), outcome);
        IReadOnlyList<GameResponse> games = new[]
        {
            new GameResponse(Guid.NewGuid(), "Pixel Racers", "Retro racing", "Racing", 49.90m, new DateTime(2025, 1, 20), true)
        };

        await cache.GetOrCreateAsync("games", _ => Task.FromResult(games));
        var cached = await cache.GetOrCreateAsync("games", _ => Task.FromResult<IReadOnlyList<GameResponse>>(Array.Empty<GameResponse>()));

        outcome.Last.Should().Be(CacheOutcome.Hit);
        cached.Should().BeEquivalentTo(games);
    }

    [Fact]
    public async Task NoOp_cache_bypasses_and_always_calls_the_loader()
    {
        var outcome = new CacheOutcomeContext();
        var cache = new NoOpCatalogCache(outcome);
        var loads = 0;
        Task<string> Load(CancellationToken _) { loads++; return Task.FromResult("from-db"); }

        await cache.GetOrCreateAsync("k", Load);
        await cache.GetOrCreateAsync("k", Load);

        outcome.Last.Should().Be(CacheOutcome.Bypass);
        loads.Should().Be(2);
    }

    // Simulates Redis being unreachable: every operation throws (as StackExchange.Redis does).
    private sealed class FailingDistributedCache : IDistributedCache
    {
        private static Exception Down() => new InvalidOperationException("redis down");
        public byte[]? Get(string key) => throw Down();
        public Task<byte[]?> GetAsync(string key, CancellationToken token = default) => throw Down();
        public void Set(string key, byte[] value, DistributedCacheEntryOptions options) => throw Down();
        public Task SetAsync(string key, byte[] value, DistributedCacheEntryOptions options, CancellationToken token = default) => throw Down();
        public void Refresh(string key) => throw Down();
        public Task RefreshAsync(string key, CancellationToken token = default) => throw Down();
        public void Remove(string key) => throw Down();
        public Task RemoveAsync(string key, CancellationToken token = default) => throw Down();
    }
}
