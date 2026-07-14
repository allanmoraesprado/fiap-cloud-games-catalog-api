namespace CatalogApi.Contracts;

// Canonical reference: orchestration/contracts/README.md (mirrored copy).
public record OrderPlacedEvent(
    Guid EventId,
    Guid OrderId,
    Guid UserId,
    Guid GameId,
    decimal Price,
    DateTime OccurredAt);
