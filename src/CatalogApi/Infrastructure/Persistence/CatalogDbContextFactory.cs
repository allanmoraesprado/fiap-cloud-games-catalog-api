using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace CatalogApi.Infrastructure.Persistence;

// Design-time factory so `dotnet ef migrations` does not need to boot the web host.
public class CatalogDbContextFactory : IDesignTimeDbContextFactory<CatalogDbContext>
{
    public CatalogDbContext CreateDbContext(string[] args)
    {
        var conn = Environment.GetEnvironmentVariable("ConnectionStrings__Postgres")
                   ?? "Host=localhost;Port=5432;Database=fcg_catalog;Username=fcg;Password=fcg";

        var options = new DbContextOptionsBuilder<CatalogDbContext>()
            .UseNpgsql(conn)
            .Options;

        return new CatalogDbContext(options);
    }
}
