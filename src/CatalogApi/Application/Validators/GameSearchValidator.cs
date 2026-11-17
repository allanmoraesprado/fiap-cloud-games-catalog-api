using CatalogApi.Application.Dtos.Search;
using CatalogApi.Domain.Exceptions;

namespace CatalogApi.Application.Validators;

public static class GameSearchValidator
{
    public const int MinQueryLength = 2;
    public const int MaxQueryLength = 100;
    public const int MaxGenreLength = 100;
    public const int MaxPageSize = 50;

    // Elasticsearch default index.max_result_window: from + size must not exceed it.
    public const int MaxResultWindow = 10_000;

    public static ValidationResult Validate(string? text, string? genre, int page, int pageSize)
    {
        var errors = new List<string>();
        var q = text?.Trim() ?? string.Empty;
        var g = genre?.Trim();

        if (q.Length < MinQueryLength || q.Length > MaxQueryLength)
            errors.Add($"Query parameter 'q' must have between {MinQueryLength} and {MaxQueryLength} characters.");
        if (g is { Length: > MaxGenreLength })
            errors.Add($"Query parameter 'genre' must have at most {MaxGenreLength} characters.");
        if (page < 1)
            errors.Add("Query parameter 'page' must be greater than or equal to 1.");
        if (pageSize < 1 || pageSize > MaxPageSize)
            errors.Add($"Query parameter 'pageSize' must be between 1 and {MaxPageSize}.");
        if (page >= 1 && pageSize is >= 1 and <= MaxPageSize && (long)page * pageSize > MaxResultWindow)
            errors.Add($"Pagination is limited to the first {MaxResultWindow} results.");

        return errors.Count == 0 ? ValidationResult.Success() : ValidationResult.Fail(errors.ToArray());
    }

    // Validates and normalizes the raw query-string values; invalid input becomes HTTP 400.
    public static GameSearchQuery ToQuery(string? text, string? genre, int page, int pageSize)
    {
        var result = Validate(text, genre, page, pageSize);
        if (!result.IsValid)
            throw new DomainException(string.Join(" ", result.Errors));

        var g = genre?.Trim();
        return new GameSearchQuery(text!.Trim(), string.IsNullOrEmpty(g) ? null : g, page, pageSize);
    }
}
