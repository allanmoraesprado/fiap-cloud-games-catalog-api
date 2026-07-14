namespace CatalogApi.Contracts;

// Canonical reference: orchestration/contracts/README.md (mirrored copy). Consumed.
public record PaymentProcessedEvent(
    Guid EventId,
    Guid OrderId,
    Guid UserId,
    Guid GameId,
    decimal Price,
    string Status,        // "Approved" | "Rejected"
    DateTime OccurredAt);
