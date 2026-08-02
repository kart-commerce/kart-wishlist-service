using Kart.Wishlist.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using MongoDB.Driver;
using RabbitMQ.Client;
using StackExchange.Redis;

namespace Kart.Wishlist.Api;

/// <summary>Verifies every infra dependency is reachable right after boot, one Connecting/connected
/// log pair per dependency, so a misconfigured or unreachable Postgres/Mongo/Redis/RabbitMQ shows
/// up immediately in the startup log instead of surfacing later as the first request's failure
/// (kart-identity-service's <c>StartupConnectivityChecks</c> precedent).</summary>
public static class StartupConnectivityChecks
{
    public static async Task RunAsync(WebApplication app)
    {
        // WebApplicationFactory-based tests (Contract/Integration) run this same Program.cs but
        // swap Postgres for Sqlite and remove the Mongo/Redis/RabbitMQ registrations entirely —
        // real connectivity is neither available nor meaningful there, so those factories mark
        // themselves "Testing" and this step is a deliberate no-op for them.
        if (app.Environment.IsEnvironment("Testing"))
        {
            return;
        }

        var logger = app.Logger;

        await CheckAsync(logger, "PostgreSQL", async () =>
        {
            using var scope = app.Services.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<WishlistDbContext>();
            await dbContext.Database.CanConnectAsync();
        });

        await CheckAsync(logger, "MongoDB", () =>
        {
            var database = app.Services.GetRequiredService<IMongoDatabase>();
            return database.RunCommandAsync((Command<MongoDB.Bson.BsonDocument>)"{ping:1}");
        });

        await CheckAsync(logger, "Redis", () =>
        {
            app.Services.GetRequiredService<IConnectionMultiplexer>();
            return Task.CompletedTask;
        });

        await CheckAsync(logger, "RabbitMQ", () =>
        {
            var connectionFactory = app.Services.GetRequiredService<IConnectionFactory>();
            using var connection = connectionFactory.CreateConnection();
            return Task.CompletedTask;
        });
    }

    private static async Task CheckAsync(ILogger logger, string dependency, Func<Task> connect)
    {
        logger.LogInformation("Connecting Wishlist {Dependency} ...", dependency);
        try
        {
            await connect();
        }
        catch (Exception exception)
        {
            logger.LogCritical(exception, "Failed to connect to Wishlist {Dependency}", dependency);
            throw;
        }

        logger.LogInformation("{Dependency} connected", dependency);
    }
}
