using CatalogApi.Contracts;

namespace CatalogApi.Application.Interfaces;

public interface IPurchaseCompletionService
{
    // Adds the game to the user's library when the payment was Approved (idempotent).
    Task CompleteAsync(PaymentProcessedEvent evt, CancellationToken ct = default);
}
