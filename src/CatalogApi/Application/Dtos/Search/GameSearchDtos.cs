namespace CatalogApi.Application.Dtos.Search;

// Normalized and validated search input (see GameSearchValidator).
public sealed record GameSearchQuery(string Text, string? Genre, int Page, int PageSize)
{
    public int From => (Page - 1) * PageSize;
}

public sealed record GameSearchHit(
    Guid Id,
    string Title,
    string Description,
    string Genre,
    decimal Price,
    DateTime ReleaseDate,
    double Score);

public sealed record GameSearchResponse(
    string Query,
    string? Genre,
    int Page,
    int PageSize,
    long Total,
    long TookMs,
    IReadOnlyList<GameSearchHit> Items);

public sealed record ReindexResponse(string Index, int Indexed, bool Recreated);
