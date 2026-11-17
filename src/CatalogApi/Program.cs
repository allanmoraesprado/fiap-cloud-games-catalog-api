using CatalogApi.Configuration;
using CatalogApi.Infrastructure.Persistence;
using CatalogApi.Middleware;
using CatalogApi.Observability;
using Prometheus;
using Serilog;

// Keeps Npgsql DateTime handling compatible with the domain model.
AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);

var builder = WebApplication.CreateBuilder(args);

// Secrets mounted as files (Key Vault CSI driver on AKS) override every other source.
builder.Configuration.AddMountedSecrets(builder.Configuration[RuntimeConfiguration.SecretsPathKey]);

Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .CreateLogger();

builder.Host.UseSerilog();
Log.Information("Starting CatalogApi version {Version}", RuntimeConfiguration.AppVersion);

builder.Services.AddControllers();
builder.Services.AddFcgServices(builder.Configuration);
builder.Services.AddFcgCache(builder.Configuration);
builder.Services.AddFcgSearch(builder.Configuration);
builder.Services.AddFcgAuth(builder.Configuration);
builder.Services.AddFcgSwagger();
builder.Services.AddHealthChecks();

var app = builder.Build();

// Apply migrations and seed sample games on startup (dev convenience).
using (var scope = app.Services.CreateScope())
{
    var ctx = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
    try { await DbSeeder.SeedAsync(ctx); }
    catch (Exception ex) { Log.Warning(ex, "Database seeding failed (database may be unavailable)."); }
}

app.UseAppVersionHeader();
app.UseMiddleware<GlobalExceptionMiddleware>();
app.UseMiddleware<CacheOutcomeHeaderMiddleware>();
app.UseSerilogRequestLogging();

// Prometheus (Phase 3): default HTTP request metrics + custom counters on /metrics.
FcgMetrics.EnsureInitialized();
app.UseHttpMetrics();

app.UseSwagger();
app.UseSwaggerUI();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapHealthChecks("/health");
app.MapMetrics();

app.Run();

public partial class Program { }
