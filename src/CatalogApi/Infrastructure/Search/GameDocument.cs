using System.Text.Json.Serialization;
using CatalogApi.Domain;

namespace CatalogApi.Infrastructure.Search;

// Search-side projection of a game. Field names are explicit so query field references
// ("title", "genre.keyword", ...) stay stable regardless of serializer defaults.
public sealed class GameDocument
{
    public const string FieldId = "id";
    public const string FieldTitle = "title";
    public const string FieldTitleKeyword = "title.keyword";
    public const string FieldDescription = "description";
    public const string FieldGenre = "genre";
    public const string FieldGenreKeyword = "genre.keyword";
    public const string FieldPrice = "price";
    public const string FieldReleaseDate = "releaseDate";
    public const string FieldIsActive = "isActive";
    public const string FieldUpdatedAt = "updatedAt";

    [JsonPropertyName(FieldId)] public Guid Id { get; set; }
    [JsonPropertyName(FieldTitle)] public string Title { get; set; } = string.Empty;
    [JsonPropertyName(FieldDescription)] public string Description { get; set; } = string.Empty;
    [JsonPropertyName(FieldGenre)] public string Genre { get; set; } = string.Empty;
    [JsonPropertyName(FieldPrice)] public decimal Price { get; set; }
    [JsonPropertyName(FieldReleaseDate)] public DateTime ReleaseDate { get; set; }
    [JsonPropertyName(FieldIsActive)] public bool IsActive { get; set; }
    [JsonPropertyName(FieldUpdatedAt)] public DateTime UpdatedAt { get; set; }

    public static GameDocument From(Game game) => new()
    {
        Id = game.Id,
        Title = game.Title,
        Description = game.Description,
        Genre = game.Genre,
        Price = game.Price,
        ReleaseDate = game.ReleaseDate,
        IsActive = game.IsActive,
        UpdatedAt = game.UpdatedAt
    };
}
