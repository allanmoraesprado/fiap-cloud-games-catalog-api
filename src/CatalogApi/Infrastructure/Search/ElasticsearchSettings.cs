namespace CatalogApi.Infrastructure.Search;

// Bound from the "Elasticsearch" configuration section (Elasticsearch__* environment variables).
// Local development uses an unsecured single-node container; managed clusters (Elastic Cloud)
// use HTTPS plus an API key, which is a secret and must never be committed.
public class ElasticsearchSettings
{
    public bool Enabled { get; set; } = true;

    // Cluster endpoint, e.g. http://localhost:9200 or the Elastic Cloud HTTPS endpoint.
    public string Url { get; set; } = "http://localhost:9200";

    // Optional: Elastic Cloud ID. Used instead of Url when set.
    public string? CloudId { get; set; }

    // Optional: base64 API key ("encoded" value returned by Elasticsearch/Kibana).
    public string? ApiKey { get; set; }

    public string IndexName { get; set; } = "fcg-games";

    // Fails fast when the engine is unreachable so searches answer 503 quickly.
    public int RequestTimeoutSeconds { get; set; } = 5;

    // Background reconciliation: re-indexes games changed since the last successful run.
    public int ReconcileIntervalSeconds { get; set; } = 30;

    // Safety overlap subtracted from the watermark (commit latency and clock skew between replicas).
    public int ReconcileOverlapSeconds { get; set; } = 30;

    public int BulkBatchSize { get; set; } = 500;
}
