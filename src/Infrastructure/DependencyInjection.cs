using Kart.Wishlist.Application.Common.Interfaces;
using Kart.Wishlist.Infrastructure.BackgroundJobs;
using Kart.Wishlist.Infrastructure.Caching;
using Kart.Wishlist.Infrastructure.ExternalClients;
using Kart.Wishlist.Infrastructure.Messaging;
using Kart.Wishlist.Infrastructure.Persistence;
using Kart.Wishlist.Infrastructure.ReadModel;
using Kart.Wishlist.Infrastructure.Security;
using Kart.Shared.Messaging;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using MongoDB.Driver;
using RabbitMQ.Client;
using StackExchange.Redis;

namespace Kart.Wishlist.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<IDateTimeProvider, SystemDateTimeProvider>();

        // --- AuthN: this service validates Identity-issued JWTs, it never mints them -----------
        services.AddOptions<JwtOptions>().Bind(configuration.GetSection(JwtOptions.SectionName));
        services.AddMemoryCache();
        services.AddHttpClient(nameof(JwksSigningKeyResolver));
        services.AddSingleton<JwksSigningKeyResolver>();
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<JwksSigningKeyResolver, IOptions<JwtOptions>>((options, resolver, jwtOptions) =>
            {
                options.MapInboundClaims = false;
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = jwtOptions.Value.Issuer,
                    ValidateAudience = false,
                    ValidateLifetime = true,
                    IssuerSigningKeyResolver = (_, _, kid, _) => resolver.ResolveSigningKeys(kid),
                };
            });
        services.AddAuthorization();

        // --- PostgreSQL write side (source of truth, RLS-scoped) ------------------------------
        services.AddScoped<CurrentPrincipalConnectionInterceptor>();
        services.AddDbContext<WishlistDbContext>((sp, options) =>
        {
            options.UseNpgsql(configuration.GetConnectionString("WishlistDb"));
            options.AddInterceptors(sp.GetRequiredService<CurrentPrincipalConnectionInterceptor>());
        });
        services.AddScoped<IWishlistDbContext>(sp => sp.GetRequiredService<WishlistDbContext>());
        services.AddScoped<IUnitOfWork, EfUnitOfWork>();

        // --- MongoDB read side (sharded, denormalized CQRS query side) -----------------------
        services.AddOptions<MongoOptions>().Bind(configuration.GetSection(MongoOptions.SectionName));
        services.AddSingleton<IMongoClient>(sp => new MongoClient(sp.GetRequiredService<IOptions<MongoOptions>>().Value.ConnectionString));
        services.AddSingleton(sp =>
        {
            var options = sp.GetRequiredService<IOptions<MongoOptions>>().Value;
            return sp.GetRequiredService<IMongoClient>().GetDatabase(options.DatabaseName);
        });
        services.AddSingleton(sp =>
        {
            var options = sp.GetRequiredService<IOptions<MongoOptions>>().Value;
            return sp.GetRequiredService<IMongoDatabase>().GetCollection<WishlistReadDocument>(options.CollectionName);
        });
        services.AddScoped<IWishlistReadModelRepository, MongoWishlistReadModelRepository>();

        // --- Redis-backed per-user alert-batching/digest accumulator -------------------------
        services.AddOptions<RedisOptions>().Bind(configuration.GetSection(RedisOptions.SectionName));
        services.AddSingleton<IConnectionMultiplexer>(sp =>
            ConnectionMultiplexer.Connect(sp.GetRequiredService<IOptions<RedisOptions>>().Value.ConnectionString));
        services.AddScoped<IWishlistDigestAccumulator, RedisWishlistDigestAccumulator>();

        // --- Product Service client: the one synchronous outbound dependency (architecture.md),
        // resilient by default (timeout + retry + circuit breaker) via the standard handler ------
        services.AddOptions<ProductServiceOptions>().Bind(configuration.GetSection(ProductServiceOptions.SectionName));
        services.AddHttpClient<IProductServiceClient, ProductServiceClient>((sp, client) =>
            {
                client.BaseAddress = new Uri(sp.GetRequiredService<IOptions<ProductServiceOptions>>().Value.BaseUrl);
            })
            .AddStandardResilienceHandler();

        // --- Config-driven message bus (BRD §9) -----------------------------------------------
        services.AddOptions<RabbitMqOptions>().Bind(configuration.GetSection(RabbitMqOptions.SectionName))
            .Validate(
                o => string.IsNullOrEmpty(o.UserName) == string.IsNullOrEmpty(o.Password),
                "RabbitMq:UserName and RabbitMq:Password must either both be set or both be left unset.")
            .ValidateOnStart();
        services.AddKartMessageBusManifest(sp => sp.GetRequiredService<IOptions<RabbitMqOptions>>().Value.ManifestPath);
        services.AddKartRabbitMqConnectionFactory(sp =>
        {
            var options = sp.GetRequiredService<IOptions<RabbitMqOptions>>().Value;
            return new RabbitMqConnectionSettings(options.HostName, options.Port, options.UserName, options.Password);
        });

        services.AddKartRabbitMqTopologyStartup();
        services.AddHostedService<OutboxRelayHostedService>();
        services.AddHostedService<ReadModelProjectionHostedService>();
        services.AddHostedService<ProductEventsConsumerHostedService>();
        services.AddHostedService<UserEventsConsumerHostedService>();

        // --- Scheduled background jobs (WL-5, WL-7) -------------------------------------------
        services.AddHostedService<DigestFlushHostedService>();
        services.AddHostedService<ReconciliationHostedService>();

        return services;
    }
}
