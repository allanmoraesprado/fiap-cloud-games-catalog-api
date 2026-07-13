namespace CatalogApi.Configuration;

// CatalogAPI only VALIDATES tokens; the values must match UsersAPI's (shared secret).
public class JwtSettings
{
    public string Issuer { get; set; } = "FiapCloudGames";
    public string Audience { get; set; } = "FiapCloudGames";
    public string SecretKey { get; set; } = string.Empty;
    public int ExpirationMinutes { get; set; } = 120;
}
