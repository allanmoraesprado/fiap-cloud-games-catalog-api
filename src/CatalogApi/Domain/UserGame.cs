namespace CatalogApi.Domain;

public class UserGame
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid UserId { get; private set; }
    public Guid GameId { get; private set; }
    public DateTime AcquiredAt { get; private set; } = DateTime.UtcNow;

    private UserGame() { }

    public UserGame(Guid userId, Guid gameId)
    {
        UserId = userId;
        GameId = gameId;
    }
}
