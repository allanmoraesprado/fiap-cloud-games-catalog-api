using CatalogApi.Application.Interfaces;
using CatalogApi.Application.Services;
using CatalogApi.Contracts;
using CatalogApi.Domain;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace CatalogApi.Tests;

public class PurchaseCompletionServiceTests
{
    private readonly Mock<IUserGameRepository> _userGames = new();
    private readonly Mock<IUnitOfWork> _uow = new();

    private PurchaseCompletionService Build() =>
        new(_userGames.Object, _uow.Object, NullLogger<PurchaseCompletionService>.Instance);

    private static PaymentProcessedEvent Evt(string status) =>
        new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 49.90m, status, DateTime.UtcNow);

    [Fact]
    public async Task Approved_and_not_owned_adds_to_library()
    {
        _userGames.Setup(r => r.ExistsAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);

        await Build().CompleteAsync(Evt("Approved"));

        _userGames.Verify(r => r.AddAsync(It.IsAny<UserGame>(), It.IsAny<CancellationToken>()), Times.Once);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Approved_but_already_owned_does_not_add()
    {
        _userGames.Setup(r => r.ExistsAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);

        await Build().CompleteAsync(Evt("Approved"));

        _userGames.Verify(r => r.AddAsync(It.IsAny<UserGame>(), It.IsAny<CancellationToken>()), Times.Never);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Rejected_does_not_add()
    {
        await Build().CompleteAsync(Evt("Rejected"));

        _userGames.Verify(r => r.ExistsAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        _userGames.Verify(r => r.AddAsync(It.IsAny<UserGame>(), It.IsAny<CancellationToken>()), Times.Never);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }
}
