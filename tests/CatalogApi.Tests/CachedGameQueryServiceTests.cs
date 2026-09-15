using CatalogApi.Application.Dtos.Games;
using CatalogApi.Application.Dtos.Library;
using CatalogApi.Application.Interfaces;
using CatalogApi.Infrastructure.Caching;
using FluentAssertions;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace CatalogApi.Tests;

public class CachedGameQueryServiceTests
{
    private readonly Mock<IGameQueryService> _inner = new();
    private readonly CacheOutcomeContext _outcome = new();
    private readonly CachedGameQueryService _sut;

    public CachedGameQueryServiceTests()
    {
        var backend = new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions()));
        var cache = new RedisCatalogCache(backend, Options.Create(new RedisSettings()), _outcome, NullLogger<RedisCatalogCache>.Instance);
        _sut = new CachedGameQueryService(_inner.Object, cache);
    }

    [Fact]
    public async Task Active_games_list_is_loaded_once_and_then_served_from_cache()
    {
        IReadOnlyList<GameResponse> games = new[]
        {
            new GameResponse(Guid.NewGuid(), "Cosmic Odyssey", "Sci-fi", "RPG", 199.90m, new DateTime(2024, 3, 15), true)
        };
        _inner.Setup(q => q.ListActiveGamesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(games);

        var first = await _sut.ListActiveGamesAsync();
        var second = await _sut.ListActiveGamesAsync();

        first.Should().BeEquivalentTo(games);
        second.Should().BeEquivalentTo(games);
        _outcome.Last.Should().Be(CacheOutcome.Hit);
        _inner.Verify(q => q.ListActiveGamesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task User_library_is_cached_per_user()
    {
        var alice = Guid.NewGuid();
        var bob = Guid.NewGuid();
        _inner.Setup(q => q.ListUserLibraryAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync((Guid userId, CancellationToken _) => new[]
              {
                  new UserGameResponse(Guid.NewGuid(), $"Game of {userId}", "Genre", 10m, DateTime.UtcNow)
              });

        var alice1 = await _sut.ListUserLibraryAsync(alice);
        var alice2 = await _sut.ListUserLibraryAsync(alice);
        var bob1 = await _sut.ListUserLibraryAsync(bob);

        alice2.Should().BeEquivalentTo(alice1);
        bob1.Single().Title.Should().Contain(bob.ToString());
        _inner.Verify(q => q.ListUserLibraryAsync(alice, It.IsAny<CancellationToken>()), Times.Once);
        _inner.Verify(q => q.ListUserLibraryAsync(bob, It.IsAny<CancellationToken>()), Times.Once);
    }
}
