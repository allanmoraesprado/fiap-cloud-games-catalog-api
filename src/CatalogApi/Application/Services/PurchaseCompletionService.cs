using CatalogApi.Application.Interfaces;
using CatalogApi.Contracts;
using CatalogApi.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace CatalogApi.Application.Services;

public class PurchaseCompletionService : IPurchaseCompletionService
{
    private const string Approved = "Approved";

    private readonly IUserGameRepository _userGames;
    private readonly IUnitOfWork _uow;
    private readonly ILogger<PurchaseCompletionService> _logger;

    public PurchaseCompletionService(
        IUserGameRepository userGames,
        IUnitOfWork uow,
        ILogger<PurchaseCompletionService> logger)
    {
        _userGames = userGames;
        _uow = uow;
        _logger = logger;
    }

    public async Task CompleteAsync(PaymentProcessedEvent evt, CancellationToken ct = default)
    {
        if (!string.Equals(evt.Status, Approved, StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogInformation("Payment {Status} for order {OrderId}; not adding to library.", evt.Status, evt.OrderId);
            return;
        }

        // Idempotency #1: skip if already owned (handles duplicate approved events + re-delivery).
        if (await _userGames.ExistsAsync(evt.UserId, evt.GameId, ct))
        {
            _logger.LogInformation("User {UserId} already owns game {GameId}; skipping (idempotent).", evt.UserId, evt.GameId);
            return;
        }

        try
        {
            await _userGames.AddAsync(new UserGame(evt.UserId, evt.GameId), ct);
            await _uow.SaveChangesAsync(ct);
            _logger.LogInformation("Added game {GameId} to user {UserId} library (order {OrderId}).", evt.GameId, evt.UserId, evt.OrderId);
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            // Idempotency #2: the unique (UserId, GameId) index is authoritative under races.
            _logger.LogInformation("Duplicate approved payment for user {UserId} game {GameId}; already owned (idempotent).", evt.UserId, evt.GameId);
        }
    }

    private static bool IsUniqueViolation(DbUpdateException ex)
        => ex.InnerException is PostgresException pg && pg.SqlState == PostgresErrorCodes.UniqueViolation;
}
