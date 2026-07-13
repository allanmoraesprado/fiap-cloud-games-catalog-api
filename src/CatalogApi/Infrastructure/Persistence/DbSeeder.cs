using CatalogApi.Domain;
using Microsoft.EntityFrameworkCore;

namespace CatalogApi.Infrastructure.Persistence;

public static class DbSeeder
{
    public static async Task SeedAsync(CatalogDbContext context, CancellationToken ct = default)
    {
        await context.Database.MigrateAsync(ct);

        // Seed sample games only. No users, no user_games (CatalogAPI does not own users).
        if (!await context.Games.AnyAsync(ct))
        {
            var games = new[]
            {
                new Game("Cosmic Odyssey", "Sci-fi RPG set across galaxies.", "RPG", 199.90m, new DateTime(2024, 03, 15)),
                new Game("Shadow Realms", "Action adventure with dark fantasy.", "Action", 149.90m, new DateTime(2023, 11, 02)),
                new Game("Pixel Racers", "Retro-style arcade racing.", "Racing", 49.90m, new DateTime(2025, 01, 20)),
                new Game("Mythic Lands", "Open-world fantasy MMO.", "MMORPG", 99.90m, new DateTime(2022, 06, 10))
            };
            await context.Games.AddRangeAsync(games, ct);
            await context.SaveChangesAsync(ct);
        }
    }
}
