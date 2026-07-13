using CatalogApi.Domain.Exceptions;

namespace CatalogApi.Domain;

public class Game
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public string Title { get; private set; } = string.Empty;
    public string Description { get; private set; } = string.Empty;
    public string Genre { get; private set; } = string.Empty;
    public decimal Price { get; private set; }
    public DateTime ReleaseDate { get; private set; }
    public DateTime CreatedAt { get; private set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; private set; } = DateTime.UtcNow;
    public bool IsActive { get; private set; } = true;

    private Game() { }

    public Game(string title, string description, string genre, decimal price, DateTime releaseDate)
    {
        Validate(title, price);
        Title = title;
        Description = description;
        Genre = genre;
        Price = price;
        ReleaseDate = releaseDate;
    }

    public void Update(string title, string description, string genre, decimal price, DateTime releaseDate)
    {
        Validate(title, price);
        Title = title;
        Description = description;
        Genre = genre;
        Price = price;
        ReleaseDate = releaseDate;
        UpdatedAt = DateTime.UtcNow;
    }

    public void Deactivate()
    {
        IsActive = false;
        UpdatedAt = DateTime.UtcNow;
    }

    private static void Validate(string title, decimal price)
    {
        if (string.IsNullOrWhiteSpace(title))
            throw new DomainException("Game title is required.");
        if (price < 0)
            throw new DomainException("Game price cannot be negative.");
    }
}
