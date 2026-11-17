using System.Text;
using CatalogApi.Application.Interfaces;
using CatalogApi.Application.Services;
using CatalogApi.Consumers;
using CatalogApi.Infrastructure.Caching;
using CatalogApi.Infrastructure.Dapper;
using CatalogApi.Infrastructure.Persistence;
using CatalogApi.Infrastructure.Search;
using CatalogApi.Messaging;
using Elastic.Clients.Elasticsearch;
using Elastic.Transport;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

namespace CatalogApi.Configuration;

public static class DependencyInjection
{
    public static IServiceCollection AddFcgServices(this IServiceCollection services, IConfiguration config)
    {
        var pgConn = config.GetConnectionString("Postgres")
                     ?? throw new InvalidOperationException("Missing Postgres connection string.");

        services.AddDbContext<CatalogDbContext>(opts => opts.UseNpgsql(pgConn));
        services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<CatalogDbContext>());

        services.AddScoped<IGameRepository, GameRepository>();
        services.AddScoped<IUserGameRepository, UserGameRepository>();
        services.AddScoped<IGameService, GameService>();
        services.AddScoped<ILibraryService, LibraryService>();
        services.AddScoped<IPurchaseCompletionService, PurchaseCompletionService>();

        // Read model (Dapper) uses the same PostgreSQL connection; exposed through the Redis
        // cache-aside decorator (Phase 3). Controllers/LibraryService still see IGameQueryService.
        services.AddSingleton(_ => new GameQueryService(pgConn));
        services.AddScoped<IGameQueryService>(sp =>
            new CachedGameQueryService(sp.GetRequiredService<GameQueryService>(), sp.GetRequiredService<ICatalogCache>()));

        services.Configure<KafkaSettings>(config.GetSection("Kafka"));
        services.AddSingleton<IEventPublisher, KafkaEventPublisher>();
        services.AddHostedService<PaymentProcessedConsumer>();

        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentUser, CurrentUser>();

        return services;
    }

    // Distributed cache (Phase 3): Redis via IDistributedCache with cache-aside semantics.
    // Redis:Enabled=false swaps in a no-op cache; Redis unavailable at runtime => BYPASS (see RedisCatalogCache).
    public static IServiceCollection AddFcgCache(this IServiceCollection services, IConfiguration config)
    {
        var section = config.GetSection("Redis");
        var redis = section.Get<RedisSettings>() ?? new RedisSettings();
        services.Configure<RedisSettings>(section);
        services.AddScoped<CacheOutcomeContext>();

        if (!redis.Enabled)
        {
            services.AddScoped<ICatalogCache, NoOpCatalogCache>();
            return services;
        }

        services.AddStackExchangeRedisCache(options =>
        {
            options.Configuration = redis.ConnectionString;
            options.InstanceName = CacheKeys.InstancePrefix;
        });
        services.AddScoped<ICatalogCache, RedisCatalogCache>();
        return services;
    }

    // Search (Elasticsearch): typed client, query service, index writer, sync service and the
    // reconciliation job. Elasticsearch:Enabled=false keeps the API running without a search
    // engine (search answers 503, index writes are skipped).
    public static IServiceCollection AddFcgSearch(this IServiceCollection services, IConfiguration config)
    {
        var section = config.GetSection("Elasticsearch");
        var settings = section.Get<ElasticsearchSettings>() ?? new ElasticsearchSettings();
        services.Configure<ElasticsearchSettings>(section);

        services.AddScoped<IGameSearchSync>(sp => new GameSearchSync(
            sp.GetRequiredService<IGameSearchIndexer>(),
            sp.GetRequiredService<IGameRepository>(),
            sp.GetRequiredService<ILogger<GameSearchSync>>(),
            settings.BulkBatchSize));

        if (!settings.Enabled)
        {
            services.AddSingleton<DisabledGameSearch>();
            services.AddSingleton<IGameSearchService>(sp => sp.GetRequiredService<DisabledGameSearch>());
            services.AddSingleton<IGameSearchIndexer>(sp => sp.GetRequiredService<DisabledGameSearch>());
            return services;
        }

        services.AddSingleton(_ => new ElasticsearchClient(BuildClientSettings(settings)));
        services.AddSingleton<IGameSearchService, ElasticGameSearchService>();
        services.AddSingleton<IGameSearchIndexer, ElasticGameIndexer>();
        services.AddHostedService<SearchIndexReconciler>();
        return services;
    }

    internal static ElasticsearchClientSettings BuildClientSettings(ElasticsearchSettings settings)
    {
        var auth = string.IsNullOrWhiteSpace(settings.ApiKey) ? null : new ApiKey(settings.ApiKey);
        ElasticsearchClientSettings clientSettings = !string.IsNullOrWhiteSpace(settings.CloudId) && auth is not null
            ? new ElasticsearchClientSettings(settings.CloudId, auth)
            : new ElasticsearchClientSettings(new Uri(settings.Url));

        if (auth is not null) clientSettings = clientSettings.Authentication(auth);
        return clientSettings
            .DefaultIndex(settings.IndexName)
            .RequestTimeout(TimeSpan.FromSeconds(Math.Max(1, settings.RequestTimeoutSeconds)));
    }

    public static IServiceCollection AddFcgAuth(this IServiceCollection services, IConfiguration config)
    {
        // Validates tokens issued by UsersAPI using the SAME shared secret/issuer/audience.
        var jwt = config.GetSection("Jwt").Get<JwtSettings>()
                  ?? throw new InvalidOperationException("Missing Jwt settings.");

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    ValidIssuer = jwt.Issuer,
                    ValidAudience = jwt.Audience,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.SecretKey)),
                    ClockSkew = TimeSpan.FromSeconds(30)
                };
            });

        services.AddAuthorization(options =>
        {
            options.AddPolicy("AdminOnly", p => p.RequireRole("Admin"));
            options.AddPolicy("AuthenticatedUser", p => p.RequireAuthenticatedUser());
        });

        return services;
    }
}
