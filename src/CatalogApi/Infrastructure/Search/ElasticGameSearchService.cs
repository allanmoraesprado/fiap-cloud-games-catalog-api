using System.Diagnostics;
using CatalogApi.Application.Dtos.Search;
using CatalogApi.Application.Exceptions;
using CatalogApi.Application.Interfaces;
using CatalogApi.Observability;
using Elastic.Clients.Elasticsearch;
using Elastic.Clients.Elasticsearch.Core.Search;
using Elastic.Clients.Elasticsearch.QueryDsl;
using Microsoft.Extensions.Options;

namespace CatalogApi.Infrastructure.Search;

// Relevance-ranked, typo-tolerant search over active games.
//
// Scoring: a fuzzy multi_match (fuzziness AUTO, first character must match) over title (x3),
// genre (x2) and description is required; an exact phrase in the title and an exact
// (case/accent-insensitive) title add strong boosts so exact matches rank above fuzzy ones.
// Results are ordered by _score, ties broken by title.
public sealed class ElasticGameSearchService : IGameSearchService
{
    private readonly ElasticsearchClient _client;
    private readonly ElasticsearchSettings _settings;
    private readonly ILogger<ElasticGameSearchService> _logger;

    public ElasticGameSearchService(ElasticsearchClient client, IOptions<ElasticsearchSettings> options, ILogger<ElasticGameSearchService> logger)
    {
        _client = client;
        _settings = options.Value;
        _logger = logger;
    }

    public async Task<GameSearchResponse> SearchAsync(GameSearchQuery query, CancellationToken ct = default)
    {
        var request = BuildRequest(_settings.IndexName, query);

        var stopwatch = Stopwatch.StartNew();
        SearchResponse<GameDocument> response;
        try
        {
            response = await _client.SearchAsync<GameDocument>(request, ct);
        }
        finally
        {
            FcgMetrics.SearchDuration.Observe(stopwatch.Elapsed.TotalSeconds);
        }

        if (!response.IsValidResponse)
        {
            FcgMetrics.SearchRequests.WithLabels("unavailable").Inc();
            var error = ElasticResponses.Unavailable(response, "search");
            _logger.LogWarning(error, "Game search unavailable.");
            throw error;
        }

        var items = response.Hits
            .Where(h => h.Source is not null)
            .Select(h => new GameSearchHit(
                h.Source!.Id, h.Source.Title, h.Source.Description, h.Source.Genre,
                h.Source.Price, h.Source.ReleaseDate, h.Score ?? 0d))
            .ToList();

        FcgMetrics.SearchRequests.WithLabels(items.Count == 0 ? "empty" : "results").Inc();
        _logger.LogInformation("Game search '{Query}' (genre {Genre}, page {Page}) returned {Count} of {Total} in {TookMs} ms.",
            query.Text, query.Genre ?? "-", query.Page, items.Count, response.Total, response.Took);

        return new GameSearchResponse(query.Text, query.Genre, query.Page, query.PageSize, response.Total, response.Took, items);
    }

    internal static SearchRequest<GameDocument> BuildRequest(string index, GameSearchQuery query)
    {
        var filters = new List<Query> { new TermQuery(GameDocument.FieldIsActive, true) };
        if (query.Genre is not null)
            filters.Add(new TermQuery(GameDocument.FieldGenreKeyword, query.Genre));

        var fuzzy = new MultiMatchQuery
        {
            Query = query.Text,
            Fields = new[] { $"{GameDocument.FieldTitle}^3", $"{GameDocument.FieldGenre}^2", GameDocument.FieldDescription },
            Type = TextQueryType.BestFields,
            Fuzziness = new Fuzziness("AUTO"),
            PrefixLength = 1,
            MaxExpansions = 50
        };

        var exactBoosts = new List<Query>
        {
            new MatchPhraseQuery(GameDocument.FieldTitle, query.Text) { Boost = 4 },
            new TermQuery(GameDocument.FieldTitleKeyword, query.Text) { Boost = 8 }
        };

        return new SearchRequest<GameDocument>(index)
        {
            Query = new BoolQuery { Must = new List<Query> { fuzzy }, Should = exactBoosts, Filter = filters },
            From = query.From,
            Size = query.PageSize,
            TrackTotalHits = new TrackHits(true),
            Sort = new List<SortOptions>
            {
                new SortOptions { Score = new ScoreSort { Order = SortOrder.Desc } },
                new FieldSort(GameDocument.FieldTitleKeyword) { Order = SortOrder.Asc }
            }
        };
    }
}

// Used when Elasticsearch:Enabled=false: searches answer 503, index writes are skipped.
public sealed class DisabledGameSearch : IGameSearchService, IGameSearchIndexer
{
    public string IndexName => "disabled";

    public Task<GameSearchResponse> SearchAsync(GameSearchQuery query, CancellationToken ct = default)
    {
        FcgMetrics.SearchRequests.WithLabels("unavailable").Inc();
        throw new SearchUnavailableException("Search is disabled in this environment.");
    }

    public Task<bool> EnsureIndexAsync(CancellationToken ct = default) => throw new SearchUnavailableException("Search is disabled in this environment.");
    public Task RecreateIndexAsync(CancellationToken ct = default) => throw new SearchUnavailableException("Search is disabled in this environment.");
    public Task IndexAsync(Domain.Game game, CancellationToken ct = default) => Task.CompletedTask;
    public Task IndexManyAsync(IReadOnlyCollection<Domain.Game> games, CancellationToken ct = default) => Task.CompletedTask;
    public Task RefreshAsync(CancellationToken ct = default) => Task.CompletedTask;
}
