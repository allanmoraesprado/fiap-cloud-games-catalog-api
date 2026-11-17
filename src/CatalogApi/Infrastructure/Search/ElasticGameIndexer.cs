using CatalogApi.Application.Interfaces;
using CatalogApi.Domain;
using Elastic.Clients.Elasticsearch;
using Elastic.Clients.Elasticsearch.Analysis;
using Elastic.Clients.Elasticsearch.Core.Bulk;
using Elastic.Clients.Elasticsearch.IndexManagement;
using Elastic.Clients.Elasticsearch.Mapping;
using Microsoft.Extensions.Options;

namespace CatalogApi.Infrastructure.Search;

public sealed class ElasticGameIndexer : IGameSearchIndexer
{
    // Analyzer for every searchable text field: standard tokenizer + lowercase + asciifolding, so
    // "Pokémon" and "pokemon" match. Keyword sub-fields use a lowercase/asciifolding normalizer
    // for case-insensitive exact matches, genre filters and stable sorting.
    public const string TextAnalyzer = "fcg_text";
    public const string KeywordNormalizer = "fcg_keyword";

    private readonly ElasticsearchClient _client;
    private readonly ElasticsearchSettings _settings;
    private readonly ILogger<ElasticGameIndexer> _logger;

    public ElasticGameIndexer(ElasticsearchClient client, IOptions<ElasticsearchSettings> options, ILogger<ElasticGameIndexer> logger)
    {
        _client = client;
        _settings = options.Value;
        _logger = logger;
    }

    public string IndexName => _settings.IndexName;

    public async Task<bool> EnsureIndexAsync(CancellationToken ct = default)
    {
        var exists = await _client.Indices.ExistsAsync(IndexName, ct);
        var status = ElasticResponses.StatusOf(exists);
        if (status == 200) return false;
        if (status != 404) throw ElasticResponses.Unavailable(exists, "index lookup");

        return await CreateIndexAsync(ct);
    }

    public async Task RecreateIndexAsync(CancellationToken ct = default)
    {
        var deleted = await _client.Indices.DeleteAsync(IndexName, ct);
        if (!deleted.IsValidResponse && ElasticResponses.StatusOf(deleted) != 404)
            throw ElasticResponses.Unavailable(deleted, "index delete");

        await CreateIndexAsync(ct);
        _logger.LogInformation("Search index {Index} recreated.", IndexName);
    }

    public async Task IndexAsync(Game game, CancellationToken ct = default)
    {
        var document = GameDocument.From(game);
        var request = new IndexRequest<GameDocument>(document, IndexName, document.Id.ToString())
        {
            // Read-your-writes for the admin that just changed the game (bounded by the refresh interval).
            Refresh = Refresh.WaitFor
        };
        var response = await _client.IndexAsync(request, ct);
        ElasticResponses.EnsureSuccess(response, "index");
    }

    public async Task IndexManyAsync(IReadOnlyCollection<Game> games, CancellationToken ct = default)
    {
        if (games.Count == 0) return;

        var operations = new BulkOperationsCollection();
        foreach (var game in games)
        {
            var document = GameDocument.From(game);
            operations.Add(new BulkIndexOperation<GameDocument>(document) { Id = document.Id.ToString() });
        }

        var response = await _client.BulkAsync(new BulkRequest(IndexName) { Operations = operations }, ct);
        ElasticResponses.EnsureSuccess(response, "bulk");
        if (response.Errors)
        {
            var first = response.ItemsWithErrors.FirstOrDefault()?.Error?.Reason ?? "unknown item error";
            throw new Application.Exceptions.SearchUnavailableException(
                $"Elasticsearch bulk indexing rejected {response.ItemsWithErrors.Count()} item(s): {first}");
        }
    }

    public async Task RefreshAsync(CancellationToken ct = default)
    {
        var response = await _client.Indices.RefreshAsync(IndexName, ct);
        ElasticResponses.EnsureSuccess(response, "refresh");
    }

    private async Task<bool> CreateIndexAsync(CancellationToken ct)
    {
        var request = new CreateIndexRequest(IndexName)
        {
            Settings = new IndexSettings
            {
                NumberOfShards = 1,
                // 0 replicas on a single node, 1 when the cluster has a second node (managed clusters).
                AutoExpandReplicas = "0-1",
                Analysis = new IndexSettingsAnalysis
                {
                    Analyzers = new Analyzers
                    {
                        { TextAnalyzer, new CustomAnalyzer { Tokenizer = "standard", Filter = new List<string> { "lowercase", "asciifolding" } } }
                    },
                    Normalizers = new Normalizers
                    {
                        { KeywordNormalizer, new CustomNormalizer { Filter = new List<string> { "lowercase", "asciifolding" } } }
                    }
                }
            },
            Mappings = new TypeMapping
            {
                Properties = new Properties
                {
                    { GameDocument.FieldId, new KeywordProperty() },
                    { GameDocument.FieldTitle, TextWithKeyword() },
                    { GameDocument.FieldDescription, new TextProperty { Analyzer = TextAnalyzer } },
                    { GameDocument.FieldGenre, TextWithKeyword() },
                    { GameDocument.FieldPrice, new ScaledFloatNumberProperty { ScalingFactor = 100 } },
                    { GameDocument.FieldReleaseDate, new DateProperty() },
                    { GameDocument.FieldIsActive, new BooleanProperty() },
                    { GameDocument.FieldUpdatedAt, new DateProperty() }
                }
            }
        };

        var response = await _client.Indices.CreateAsync(request, ct);
        if (response.IsValidResponse)
        {
            _logger.LogInformation("Search index {Index} created.", IndexName);
            return true;
        }

        // Another replica created it first.
        if (response.ElasticsearchServerError?.Error?.Type == "resource_already_exists_exception")
            return false;

        throw ElasticResponses.Unavailable(response, "index create");
    }

    private static TextProperty TextWithKeyword() => new()
    {
        Analyzer = TextAnalyzer,
        Fields = new Properties
        {
            { "keyword", new KeywordProperty { Normalizer = KeywordNormalizer, IgnoreAbove = 256 } }
        }
    };
}
