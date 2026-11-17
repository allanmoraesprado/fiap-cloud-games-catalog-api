using CatalogApi.Application.Dtos.Games;
using CatalogApi.Application.Dtos.Search;
using CatalogApi.Application.Exceptions;
using CatalogApi.Application.Interfaces;
using CatalogApi.Application.Services;
using CatalogApi.Domain;
using CatalogApi.Infrastructure.Search;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace CatalogApi.Tests.Integration;

// End-to-end search behaviour against a real Elasticsearch: index mapping, fuzzy matching,
// relevance ordering, synchronization after writes, reindexing and engine outages.
[Collection(ElasticsearchCollection.Name)]
[Trait("Category", "Integration")]
public sealed class GameSearchIntegrationTests : IAsyncLifetime
{
    private readonly ElasticsearchFixture _es;
    private readonly ElasticsearchSettings _settings;
    private readonly ElasticGameIndexer _indexer;
    private readonly ElasticGameSearchService _search;
    private readonly InMemoryGameRepository _repository = new();

    public GameSearchIntegrationTests(ElasticsearchFixture es)
    {
        _es = es;
        _settings = es.Settings($"fcg-games-test-{Guid.NewGuid():N}");
        (_indexer, _search) = ElasticsearchFixture.Create(_settings);
    }

    public async Task InitializeAsync()
    {
        foreach (var game in SeedGames()) await _repository.AddAsync(game);
        // Initial indexing goes through the same path the API uses on startup.
        await Reconciler(_indexer).RunOnceAsync();
        await _indexer.RefreshAsync();
    }

    public async Task DisposeAsync()
    {
        await ElasticsearchFixture.Client(_settings).Indices.DeleteAsync(_settings.IndexName);
    }

    private static IEnumerable<Game> SeedGames() => new[]
    {
        new Game("Cosmic Odyssey", "Sci-fi RPG set across galaxies.", "RPG", 199.90m, new DateTime(2024, 3, 15)),
        new Game("Galaxy Odyssey Tactics", "Turn-based tactics with a cosmic twist.", "Strategy", 89.90m, new DateTime(2023, 5, 1)),
        new Game("Shadow Realms", "Action adventure with dark fantasy.", "Action", 149.90m, new DateTime(2023, 11, 2)),
        new Game("Night Ops", "Stealth missions in the shadow of the city.", "Action", 59.90m, new DateTime(2022, 2, 10)),
        new Game("Pixel Racers", "Retro-style arcade racing.", "Racing", 49.90m, new DateTime(2025, 1, 20)),
        new Game("Mythic Lands", "Open-world fantasy MMO.", "MMORPG", 99.90m, new DateTime(2022, 6, 10))
    };

    private Task<GameSearchResponse> Search(string text, string? genre = null, int page = 1, int pageSize = 10)
        => _search.SearchAsync(new GameSearchQuery(text, genre, page, pageSize));

    private GameSearchSync Sync(IGameSearchIndexer indexer) =>
        new(indexer, _repository, NullLogger<GameSearchSync>.Instance);

    private SearchIndexReconciler Reconciler(IGameSearchIndexer indexer)
    {
        var services = new ServiceCollection().AddScoped<IGameSearchSync>(_ => Sync(indexer)).BuildServiceProvider();
        return new SearchIndexReconciler(services.GetRequiredService<IServiceScopeFactory>(), indexer,
            Options.Create(_settings), NullLogger<SearchIndexReconciler>.Instance);
    }

    private GameService AdminGameService(IGameSearchIndexer indexer)
    {
        var admin = new Mock<ICurrentUser>();
        admin.SetupGet(u => u.IsAdmin).Returns(true);
        return new GameService(_repository, _repository, admin.Object, Mock.Of<ICatalogCache>(), Sync(indexer),
            NullLogger<GameService>.Instance);
    }

    [Fact]
    public async Task Exact_title_is_the_top_result()
    {
        var result = await Search("Cosmic Odyssey");

        result.Items.Should().NotBeEmpty();
        result.Items[0].Title.Should().Be("Cosmic Odyssey");
    }

    [Theory]
    [InlineData("Cosmik Odisey", "Cosmic Odyssey")]
    [InlineData("shadw realms", "Shadow Realms")]
    [InlineData("Pixl Racer", "Pixel Racers")]
    [InlineData("mythik", "Mythic Lands")]
    public async Task Misspelled_query_still_finds_the_intended_game(string typo, string expected)
    {
        var result = await Search(typo);

        result.Items.Should().NotBeEmpty();
        result.Items[0].Title.Should().Be(expected);
    }

    [Fact]
    public async Task Results_are_ordered_by_relevance()
    {
        // "shadow" is in the title of one game and only in the description of another.
        var result = await Search("shadow");

        result.Items.Select(i => i.Title).Should().ContainInOrder("Shadow Realms", "Night Ops");
        result.Items.Select(i => i.Score).Should().BeInDescendingOrder();
    }

    [Fact]
    public async Task Exact_match_outranks_partial_matches()
    {
        var result = await Search("Cosmic Odyssey");

        result.Items.Select(i => i.Title).Should().StartWith(new[] { "Cosmic Odyssey", "Galaxy Odyssey Tactics" });
        result.Items[0].Score.Should().BeGreaterThan(result.Items[1].Score);
    }

    [Fact]
    public async Task Unknown_terms_return_an_empty_page()
    {
        var result = await Search("zzqxv blorpt");

        result.Total.Should().Be(0);
        result.Items.Should().BeEmpty();
    }

    [Fact]
    public async Task Genre_filter_is_case_insensitive()
    {
        var result = await Search("fantasy", genre: "action");

        result.Items.Select(i => i.Title).Should().Equal("Shadow Realms");
    }

    [Fact]
    public async Task Results_are_paginated_without_overlap()
    {
        var service = AdminGameService(_indexer);
        for (var i = 1; i <= 12; i++)
            await service.CreateAsync(new GameRequest($"Arena Fighter {i:00}", "Versus fighting.", "Fighting", 10m, DateTime.UtcNow));

        var pages = new List<GameSearchResponse>();
        for (var page = 1; page <= 3; page++) pages.Add(await Search("arena fighter", pageSize: 5, page: page));

        pages.Should().AllSatisfy(p => p.Total.Should().Be(12));
        pages.Select(p => p.Items.Count).Should().Equal(5, 5, 2);
        pages.SelectMany(p => p.Items).Select(i => i.Id).Should().OnlyHaveUniqueItems().And.HaveCount(12);
    }

    [Fact]
    public async Task Created_game_is_searchable_right_after_creation()
    {
        var created = await AdminGameService(_indexer)
            .CreateAsync(new GameRequest("Stellar Harvest", "Farming among the stars.", "Simulation", 39.90m, DateTime.UtcNow));

        var result = await Search("Stelar Harvset");

        result.Items.Should().ContainSingle(i => i.Id == created.Id);
        result.Items[0].Id.Should().Be(created.Id);
    }

    [Fact]
    public async Task Edited_game_is_found_by_its_new_values_only()
    {
        var game = _repository.All.Single(g => g.Title == "Pixel Racers");

        await AdminGameService(_indexer).UpdateAsync(game.Id,
            new GameRequest("Turbo Drift Legends", "Neon street racing.", "Racing", 59.90m, game.ReleaseDate));

        (await Search("Turbo Drift")).Items.Should().Contain(i => i.Id == game.Id && i.Price == 59.90m);
        (await Search("Pixel Racers")).Items.Should().NotContain(i => i.Id == game.Id);
    }

    [Fact]
    public async Task Deleted_game_disappears_from_search()
    {
        var game = _repository.All.Single(g => g.Title == "Mythic Lands");

        await AdminGameService(_indexer).DeleteAsync(game.Id);

        (await Search("Mythic Lands")).Items.Should().NotContain(i => i.Id == game.Id);
    }

    [Fact]
    public async Task Full_reindex_rebuilds_the_index_from_the_database()
    {
        var result = await Sync(_indexer).ReindexAllAsync(recreate: true);

        result.Indexed.Should().Be(_repository.All.Count);
        (await Search("Cosmic Odyssey")).Items[0].Title.Should().Be("Cosmic Odyssey");
    }

    [Fact]
    public async Task Lost_index_is_recreated_and_fully_reloaded_by_the_reconciler()
    {
        var reconciler = Reconciler(_indexer);
        await reconciler.RunOnceAsync();
        await ElasticsearchFixture.Client(_settings).Indices.DeleteAsync(_settings.IndexName);

        var count = await reconciler.RunOnceAsync();
        await _indexer.RefreshAsync();

        count.Should().Be(_repository.All.Count);
        (await Search("Shadow Realms")).Items[0].Title.Should().Be("Shadow Realms");
    }

    [Fact]
    public async Task Engine_outage_answers_unavailable_keeps_writes_working_and_recovers()
    {
        // Nothing listens on port 1: every call fails fast, like an Elasticsearch outage.
        var down = ElasticsearchFixture.Create(_es.Settings(_settings.IndexName, new Uri("http://127.0.0.1:1")));

        var search = () => down.Search.SearchAsync(new GameSearchQuery("Cosmic", null, 1, 10));
        await search.Should().ThrowAsync<SearchUnavailableException>();

        // The write still succeeds (database first, index best effort)...
        var created = await AdminGameService(down.Indexer)
            .CreateAsync(new GameRequest("Abyss Divers", "Deep sea exploration.", "Adventure", 29.90m, DateTime.UtcNow));
        (await Search("Abyss Divers")).Items.Should().NotContain(i => i.Id == created.Id);

        // ...and once the engine is back the reconciliation job indexes the missed change.
        await Reconciler(_indexer).RunOnceAsync();
        await _indexer.RefreshAsync();
        (await Search("Abyss Divers")).Items.Should().ContainSingle(i => i.Id == created.Id);
    }
}
