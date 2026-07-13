using Dapper;
using CatalogApi.Application.Dtos.Games;
using CatalogApi.Application.Dtos.Library;
using CatalogApi.Application.Interfaces;
using Npgsql;

namespace CatalogApi.Infrastructure.Dapper;

public class GameQueryService : IGameQueryService
{
    private readonly string _connectionString;

    public GameQueryService(string connectionString)
    {
        _connectionString = connectionString;
    }

    public async Task<IReadOnlyList<GameResponse>> ListActiveGamesAsync(CancellationToken ct = default)
    {
        const string sql = @"
            SELECT ""Id"", ""Title"", ""Description"", ""Genre"", ""Price"", ""ReleaseDate"", ""IsActive""
            FROM games
            WHERE ""IsActive"" = TRUE
            ORDER BY ""Title"";";

        await using var conn = new NpgsqlConnection(_connectionString);
        var rows = await conn.QueryAsync<GameResponse>(new CommandDefinition(sql, cancellationToken: ct));
        return rows.ToList();
    }

    public async Task<IReadOnlyList<UserGameResponse>> ListUserLibraryAsync(Guid userId, CancellationToken ct = default)
    {
        const string sql = @"
            SELECT g.""Id"" AS GameId,
                   g.""Title"" AS Title,
                   g.""Genre"" AS Genre,
                   g.""Price"" AS Price,
                   ug.""AcquiredAt"" AS AcquiredAt
            FROM user_games ug
            INNER JOIN games g ON g.""Id"" = ug.""GameId""
            WHERE ug.""UserId"" = @UserId
            ORDER BY ug.""AcquiredAt"" DESC;";

        await using var conn = new NpgsqlConnection(_connectionString);
        var rows = await conn.QueryAsync<UserGameResponse>(new CommandDefinition(sql, new { UserId = userId }, cancellationToken: ct));
        return rows.ToList();
    }
}
