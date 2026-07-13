namespace CatalogApi.Application.Dtos.Games;

public record GameRequest(string Title, string Description, string Genre, decimal Price, DateTime ReleaseDate);
