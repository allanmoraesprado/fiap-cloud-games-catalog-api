using CatalogApi.Application.Dtos.Library;
using CatalogApi.Application.Interfaces;
using CatalogApi.Contracts;
using CatalogApi.Domain.Exceptions;
using CatalogApi.Messaging;
using Microsoft.Extensions.Options;

namespace CatalogApi.Application.Services;

public class LibraryService : ILibraryService
{
    private readonly ICurrentUser _currentUser;
    private readonly IGameQueryService _gameQueries;
    private readonly IGameRepository _games;
    private readonly IEventPublisher _publisher;
    private readonly KafkaSettings _kafka;

    public LibraryService(
        ICurrentUser currentUser,
        IGameQueryService gameQueries,
        IGameRepository games,
        IEventPublisher publisher,
        IOptions<KafkaSettings> kafkaOptions)
    {
        _currentUser = currentUser;
        _gameQueries = gameQueries;
        _games = games;
        _publisher = publisher;
        _kafka = kafkaOptions.Value;
    }

    public async Task<OrderPlacedResponse> AcquireAsync(Guid gameId, CancellationToken ct = default)
    {
        if (!_currentUser.IsAuthenticated || _currentUser.UserId is null)
            throw new UnauthorizedException("User must be authenticated.");

        var userId = _currentUser.UserId.Value;
        var game = await _games.GetByIdAsync(gameId, ct) ?? throw new NotFoundException("Game not found.");
        if (!game.IsActive)
            throw new DomainException("Game is not active.");

        // Start the asynchronous purchase: publish an order with the price read from our own
        // database. The library is NOT written here; that happens in M5 when CatalogAPI
        // consumes the approved PaymentProcessedEvent.
        var orderId = Guid.NewGuid();
        var evt = new OrderPlacedEvent(Guid.NewGuid(), orderId, userId, gameId, game.Price, DateTime.UtcNow);
        await _publisher.PublishAsync(_kafka.OrderPlacedTopic, orderId.ToString(), evt, ct);

        return new OrderPlacedResponse(orderId, "Pending");
    }

    public async Task<IReadOnlyList<UserGameResponse>> GetMyLibraryAsync(CancellationToken ct = default)
    {
        if (!_currentUser.IsAuthenticated || _currentUser.UserId is null)
            throw new UnauthorizedException("User must be authenticated.");
        return await _gameQueries.ListUserLibraryAsync(_currentUser.UserId.Value, ct);
    }

    public async Task<IReadOnlyList<UserGameResponse>> GetUserLibraryAsync(Guid userId, CancellationToken ct = default)
    {
        if (!_currentUser.IsAdmin)
            throw new ForbiddenException("Only Admin can view another user's library.");
        return await _gameQueries.ListUserLibraryAsync(userId, ct);
    }
}
