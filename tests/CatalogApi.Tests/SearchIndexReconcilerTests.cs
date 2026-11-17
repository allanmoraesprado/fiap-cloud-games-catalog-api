using CatalogApi.Application.Exceptions;
using CatalogApi.Application.Interfaces;
using CatalogApi.Infrastructure.Search;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace CatalogApi.Tests;

public class SearchIndexReconcilerTests
{
    private readonly Mock<IGameSearchIndexer> _indexer = new();
    private readonly Mock<IGameSearchSync> _sync = new();
    private readonly SearchIndexReconciler _sut;

    public SearchIndexReconcilerTests()
    {
        _indexer.SetupGet(i => i.IndexName).Returns("fcg-games");
        var services = new ServiceCollection().AddScoped(_ => _sync.Object).BuildServiceProvider();
        var settings = Options.Create(new ElasticsearchSettings { ReconcileOverlapSeconds = 30 });
        _sut = new SearchIndexReconciler(services.GetRequiredService<IServiceScopeFactory>(), _indexer.Object, settings,
            NullLogger<SearchIndexReconciler>.Instance);
    }

    [Fact]
    public async Task First_run_is_a_full_load()
    {
        await _sut.RunOnceAsync();

        _sync.Verify(s => s.SyncChangedSinceAsync(null, It.IsAny<CancellationToken>()), Times.Once);
        _sut.Watermark.Should().NotBeNull();
    }

    [Fact]
    public async Task Next_runs_are_incremental_with_an_overlap()
    {
        await _sut.RunOnceAsync();
        var watermark = _sut.Watermark!.Value;

        await _sut.RunOnceAsync();

        _sync.Verify(s => s.SyncChangedSinceAsync(watermark.AddSeconds(-30), It.IsAny<CancellationToken>()), Times.Once);
        _sut.Watermark.Should().BeOnOrAfter(watermark);
    }

    [Fact]
    public async Task A_recreated_index_triggers_a_new_full_load()
    {
        await _sut.RunOnceAsync();
        _indexer.Setup(i => i.EnsureIndexAsync(It.IsAny<CancellationToken>())).ReturnsAsync(true);

        await _sut.RunOnceAsync();

        _sync.Verify(s => s.SyncChangedSinceAsync(null, It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task Failure_keeps_the_watermark_so_missed_changes_are_retried()
    {
        await _sut.RunOnceAsync();
        var watermark = _sut.Watermark;
        _sync.Setup(s => s.SyncChangedSinceAsync(It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new SearchUnavailableException("down"));

        var act = () => _sut.RunOnceAsync();

        await act.Should().ThrowAsync<SearchUnavailableException>();
        _sut.Watermark.Should().Be(watermark);
    }

    [Fact]
    public async Task Unreachable_engine_on_startup_leaves_the_full_load_pending()
    {
        _indexer.Setup(i => i.EnsureIndexAsync(It.IsAny<CancellationToken>())).ThrowsAsync(new SearchUnavailableException("down"));

        var act = () => _sut.RunOnceAsync();

        await act.Should().ThrowAsync<SearchUnavailableException>();
        _sut.Watermark.Should().BeNull();
        _sync.Verify(s => s.SyncChangedSinceAsync(It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
