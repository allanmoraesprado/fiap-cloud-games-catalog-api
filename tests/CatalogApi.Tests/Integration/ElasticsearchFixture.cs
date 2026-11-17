using CatalogApi.Infrastructure.Search;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Elastic.Clients.Elasticsearch;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace CatalogApi.Tests.Integration;

// One real single-node Elasticsearch (same major version as the managed cluster) for the whole
// integration collection. Every test works on its own index, so tests stay isolated.
// Override the image with FCG_TEST_ELASTICSEARCH_IMAGE; requires a running Docker engine.
public sealed class ElasticsearchFixture : IAsyncLifetime
{
    public const string DefaultImage = "docker.elastic.co/elasticsearch/elasticsearch:9.5.5";
    private const int HttpPort = 9200;

    private readonly IContainer _container = new ContainerBuilder(
            Environment.GetEnvironmentVariable("FCG_TEST_ELASTICSEARCH_IMAGE") ?? DefaultImage)
        .WithEnvironment("discovery.type", "single-node")
        .WithEnvironment("xpack.security.enabled", "false")
        .WithEnvironment("ES_JAVA_OPTS", "-Xms512m -Xmx512m")
        .WithPortBinding(HttpPort, assignRandomHostPort: true)
        .WithWaitStrategy(Wait.ForUnixContainer().UntilHttpRequestIsSucceeded(r => r
            .ForPort(HttpPort)
            .ForPath("/_cluster/health?wait_for_status=yellow&timeout=5s")))
        .Build();

    public Uri Url => new($"http://{_container.Hostname}:{_container.GetMappedPublicPort(HttpPort)}");

    public Task InitializeAsync() => _container.StartAsync();

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();

    public ElasticsearchSettings Settings(string index, Uri? url = null) => new()
    {
        Url = (url ?? Url).ToString(),
        IndexName = index,
        RequestTimeoutSeconds = 5,
        ReconcileOverlapSeconds = 30
    };

    public static ElasticsearchClient Client(ElasticsearchSettings settings) =>
        new(new ElasticsearchClientSettings(new Uri(settings.Url))
            .DefaultIndex(settings.IndexName)
            .RequestTimeout(TimeSpan.FromSeconds(settings.RequestTimeoutSeconds)));

    public static (ElasticGameIndexer Indexer, ElasticGameSearchService Search) Create(ElasticsearchSettings settings)
    {
        var client = Client(settings);
        var options = Options.Create(settings);
        return (new ElasticGameIndexer(client, options, NullLogger<ElasticGameIndexer>.Instance),
            new ElasticGameSearchService(client, options, NullLogger<ElasticGameSearchService>.Instance));
    }
}

[CollectionDefinition(Name)]
public sealed class ElasticsearchCollection : ICollectionFixture<ElasticsearchFixture>
{
    public const string Name = "elasticsearch";
}
