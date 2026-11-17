using CatalogApi.Application.Exceptions;
using Elastic.Transport.Products.Elasticsearch;

namespace CatalogApi.Infrastructure.Search;

internal static class ElasticResponses
{
    // Turns any non-successful call (connection refused, timeout, cluster error) into a
    // SearchUnavailableException carrying the HTTP status and the server reason when present.
    public static void EnsureSuccess(ElasticsearchResponse response, string operation)
    {
        if (response.IsValidResponse) return;
        throw Unavailable(response, operation);
    }

    public static SearchUnavailableException Unavailable(ElasticsearchResponse response, string operation)
    {
        var status = response.ApiCallDetails?.HttpStatusCode;
        var reason = response.ElasticsearchServerError?.Error?.Reason;
        response.TryGetOriginalException(out var original);
        var detail = reason ?? original?.Message ?? "no response";
        return new SearchUnavailableException(
            $"Elasticsearch {operation} failed (HTTP {(status?.ToString() ?? "n/a")}): {detail}", original);
    }

    public static int? StatusOf(ElasticsearchResponse response) => response.ApiCallDetails?.HttpStatusCode;
}
