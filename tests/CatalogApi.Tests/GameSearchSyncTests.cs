using CatalogApi.Application.Exceptions;
using CatalogApi.Application.Interfaces;
using CatalogApi.Application.Services;
using CatalogApi.Domain;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace CatalogApi.Tests;

public class GameSearchSyncTests
{
    private readonly Mock<IGameSearchIndexer> _indexer = new();
    private readonly Mock<IGameRepository> _games = new();

    private GameSearchSync Build(int batchSize = 500) =>
        new(_indexer.Object, _games.Object, NullLogger<GameSearchSync>.Instance, batchSize);

    private static Game NewGame(string title = "Cosmic Odyssey") => new(title, "D", "RPG", 10m, DateTime.UtcNow);

    [Fact]
    public async Task TryIndex_upserts_the_game()
    {
        var game = NewGame();

        await Build().TryIndexAsync(game);

        _indexer.Verify(i => i.IndexAsync(game, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task TryIndex_never_propagates_search_engine_failures()
    {
        _indexer.Setup(i => i.IndexAsync(It.IsAny<Game>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new SearchUnavailableException("down"));

        var act = () => Build().TryIndexAsync(NewGame());

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task Sync_indexes_changed_games_in_batches()
    {
        var games = Enumerable.Range(0, 5).Select(i => NewGame($"Game {i}")).ToList();
        var since = DateTime.UtcNow.AddMinutes(-1);
        _games.Setup(r => r.ListUpdatedSinceAsync(since, It.IsAny<CancellationToken>())).ReturnsAsync(games);

        var count = await Build(batchSize: 2).SyncChangedSinceAsync(since);

        count.Should().Be(5);
        _indexer.Verify(i => i.IndexManyAsync(It.IsAny<IReadOnlyCollection<Game>>(), It.IsAny<CancellationToken>()), Times.Exactly(3));
    }

    [Fact]
    public async Task Sync_propagates_failures_so_the_reconciler_keeps_its_watermark()
    {
        _games.Setup(r => r.ListUpdatedSinceAsync(null, It.IsAny<CancellationToken>())).ReturnsAsync(new[] { NewGame() });
        _indexer.Setup(i => i.IndexManyAsync(It.IsAny<IReadOnlyCollection<Game>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new SearchUnavailableException("down"));

        var act = () => Build().SyncChangedSinceAsync(null);

        await act.Should().ThrowAsync<SearchUnavailableException>();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ReindexAll_loads_every_game_and_refreshes(bool recreate)
    {
        _indexer.SetupGet(i => i.IndexName).Returns("fcg-games");
        _games.Setup(r => r.ListUpdatedSinceAsync(null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { NewGame("A"), NewGame("B") });

        var result = await Build().ReindexAllAsync(recreate);

        result.Indexed.Should().Be(2);
        result.Recreated.Should().Be(recreate);
        result.Index.Should().Be("fcg-games");
        _indexer.Verify(i => i.RecreateIndexAsync(It.IsAny<CancellationToken>()), recreate ? Times.Once() : Times.Never());
        _indexer.Verify(i => i.EnsureIndexAsync(It.IsAny<CancellationToken>()), recreate ? Times.Never() : Times.Once());
        _indexer.Verify(i => i.RefreshAsync(It.IsAny<CancellationToken>()), Times.Once);
    }
}
