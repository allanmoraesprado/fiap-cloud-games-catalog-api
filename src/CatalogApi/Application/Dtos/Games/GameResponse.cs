namespace CatalogApi.Application.Dtos.Games;

public record GameResponse(Guid Id, string Title, string Description, string Genre, decimal Price, DateTime ReleaseDate, bool IsActive);
