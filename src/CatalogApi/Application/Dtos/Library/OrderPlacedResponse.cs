namespace CatalogApi.Application.Dtos.Library;

// Returned (202 Accepted) when a purchase is started. The library is written later
// (M5) when the approved PaymentProcessedEvent is consumed.
public record OrderPlacedResponse(Guid OrderId, string Status);
