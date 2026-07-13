namespace CatalogApi.Application.Dtos.Library;

public record UserGameResponse(Guid GameId, string Title, string Genre, decimal Price, DateTime AcquiredAt);
