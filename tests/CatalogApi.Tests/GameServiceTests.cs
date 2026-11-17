using CatalogApi.Application.Dtos.Games;
using CatalogApi.Application.Interfaces;
using CatalogApi.Application.Services;
using CatalogApi.Domain;
using CatalogApi.Domain.Exceptions;
using CatalogApi.Infrastructure.Caching;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace CatalogApi.Tests;

public class GameServiceTests
{
    private readonly Mock<IGameRepository> _games = new();
    private readonly Mock<IUnitOfWork> _uow = new();
    private readonly Mock<ICurrentUser> _user = new();
    private readonly Mock<ICatalogCache> _cache = new();
    private readonly Mock<IGameSearchSync> _search = new();

    private GameService Build() =>
        new(_games.Object, _uow.Object, _user.Object, _cache.Object, _search.Object, NullLogger<GameService>.Instance);

    private static GameRequest Request() => new("Title", "Desc", "Action", 99m, DateTime.UtcNow);

    [Fact]
    public async Task NonAdmin_cannot_create_game()
    {
        _user.SetupGet(u => u.IsAdmin).Returns(false);
        var act = () => Build().CreateAsync(new GameRequest("T", "D", "G", 10m, DateTime.UtcNow));
        await act.Should().ThrowAsync<ForbiddenException>();
    }

    [Fact]
    public async Task Admin_can_create_game()
    {
        _user.SetupGet(u => u.IsAdmin).Returns(true);
        var resp = await Build().CreateAsync(Request());
        resp.Title.Should().Be("Title");
        _games.Verify(r => r.AddAsync(It.IsAny<Game>(), It.IsAny<CancellationToken>()), Times.Once);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task NonAdmin_cannot_delete_game()
    {
        _user.SetupGet(u => u.IsAdmin).Returns(false);
        var act = () => Build().DeleteAsync(Guid.NewGuid());
        await act.Should().ThrowAsync<ForbiddenException>();
    }

    [Fact]
    public async Task Create_invalidates_the_games_list_cache()
    {
        _user.SetupGet(u => u.IsAdmin).Returns(true);

        await Build().CreateAsync(Request());

        _cache.Verify(c => c.RemoveAsync(CacheKeys.ActiveGames, It.IsAny<CancellationToken>()), Times.Once);
        _cache.Verify(c => c.RemoveAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Update_invalidates_the_games_list_and_the_game_entry()
    {
        _user.SetupGet(u => u.IsAdmin).Returns(true);
        var game = new Game("Old", "D", "G", 10m, DateTime.UtcNow);
        _games.Setup(r => r.GetByIdAsync(game.Id, It.IsAny<CancellationToken>())).ReturnsAsync(game);

        await Build().UpdateAsync(game.Id, Request());

        _cache.Verify(c => c.RemoveAsync(CacheKeys.ActiveGames, It.IsAny<CancellationToken>()), Times.Once);
        _cache.Verify(c => c.RemoveAsync(CacheKeys.Game(game.Id), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Delete_invalidates_the_games_list_and_the_game_entry()
    {
        _user.SetupGet(u => u.IsAdmin).Returns(true);
        var game = new Game("Old", "D", "G", 10m, DateTime.UtcNow);
        _games.Setup(r => r.GetByIdAsync(game.Id, It.IsAny<CancellationToken>())).ReturnsAsync(game);

        await Build().DeleteAsync(game.Id);

        _cache.Verify(c => c.RemoveAsync(CacheKeys.ActiveGames, It.IsAny<CancellationToken>()), Times.Once);
        _cache.Verify(c => c.RemoveAsync(CacheKeys.Game(game.Id), It.IsAny<CancellationToken>()), Times.Once);
    }
    [Fact]
    public async Task Create_indexes_the_new_game_after_saving()
    {
        _user.SetupGet(u => u.IsAdmin).Returns(true);
        var saved = false;
        _uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).Callback(() => saved = true).ReturnsAsync(1);
        _search.Setup(s => s.TryIndexAsync(It.IsAny<Game>(), It.IsAny<CancellationToken>()))
            .Callback(() => saved.Should().BeTrue("the index is updated only after the database commit"))
            .Returns(Task.CompletedTask);

        var resp = await Build().CreateAsync(Request());

        _search.Verify(s => s.TryIndexAsync(It.Is<Game>(g => g.Id == resp.Id && g.Title == "Title"), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Update_reindexes_the_game_with_the_new_values()
    {
        _user.SetupGet(u => u.IsAdmin).Returns(true);
        var game = new Game("Old", "D", "G", 10m, DateTime.UtcNow);
        _games.Setup(r => r.GetByIdAsync(game.Id, It.IsAny<CancellationToken>())).ReturnsAsync(game);

        await Build().UpdateAsync(game.Id, Request());

        _search.Verify(s => s.TryIndexAsync(It.Is<Game>(g => g.Id == game.Id && g.Title == "Title"), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Delete_reindexes_the_game_as_inactive()
    {
        _user.SetupGet(u => u.IsAdmin).Returns(true);
        var game = new Game("Old", "D", "G", 10m, DateTime.UtcNow);
        _games.Setup(r => r.GetByIdAsync(game.Id, It.IsAny<CancellationToken>())).ReturnsAsync(game);

        await Build().DeleteAsync(game.Id);

        _search.Verify(s => s.TryIndexAsync(It.Is<Game>(g => g.Id == game.Id && !g.IsActive), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Failed_write_does_not_touch_the_index()
    {
        _user.SetupGet(u => u.IsAdmin).Returns(true);
        _uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException("db down"));

        var act = () => Build().CreateAsync(Request());

        await act.Should().ThrowAsync<InvalidOperationException>();
        _search.Verify(s => s.TryIndexAsync(It.IsAny<Game>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
