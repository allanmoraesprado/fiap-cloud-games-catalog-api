using System.Security.Claims;
using CatalogApi.Application.Interfaces;

namespace CatalogApi.Configuration;

public class CurrentUser : ICurrentUser
{
    public CurrentUser(IHttpContextAccessor accessor)
    {
        var user = accessor.HttpContext?.User;
        if (user?.Identity?.IsAuthenticated != true) return;

        IsAuthenticated = true;
        Email = user.FindFirstValue(ClaimTypes.Email);
        Role = user.FindFirstValue(ClaimTypes.Role);
        var idClaim = user.FindFirstValue(ClaimTypes.NameIdentifier)
                      ?? user.FindFirstValue("sub");
        if (Guid.TryParse(idClaim, out var id)) UserId = id;
    }

    public Guid? UserId { get; }
    public string? Email { get; }
    public string? Role { get; }
    public bool IsAuthenticated { get; }
    public bool IsAdmin => string.Equals(Role, "Admin", StringComparison.OrdinalIgnoreCase);
}
