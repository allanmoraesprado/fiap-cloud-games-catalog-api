namespace CatalogApi.Application.Exceptions;

// The search engine is disabled, unreachable or failed to answer. Mapped to HTTP 503.
public class SearchUnavailableException : Exception
{
    public SearchUnavailableException(string message, Exception? inner = null) : base(message, inner) { }
}
