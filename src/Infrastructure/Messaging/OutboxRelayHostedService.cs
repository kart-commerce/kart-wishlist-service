using System.Text;
using Kart.Wishlist.Domain.Outbox;
using Kart.Wishlist.Infrastructure.Persistence;
using Kart.Shared.Messaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;

namespace Kart.Wishlist.Infrastructure.Messaging;

/// <summary>
/// Relays <c>wishlist_outbox_events</c> rows to <c>wishlist.exchange</c> — but only the
/// <see cref="WishlistOutboxEvent.WishlistPriceAlertTriggeredEventType"/> rows (the one event this
/// service actually publishes externally, event-contract.md). Internal-only
/// <see cref="WishlistOutboxEvent.EntryMutatedEventType"/> marker rows are never selected here —
/// they exist purely to drive <see cref="ReadModelProjectionHostedService"/>'s independent
/// <c>ProjectedAt</c> marker and are simply skipped by this relay's own <c>EventType</c> filter
/// (their <c>PublishedAt</c> stays null forever, which is fine — nothing else queries on it).
/// Declares the manifest topology idempotently on every (re)connect. Owns its own retrying
/// connection — a RabbitMQ outage never crashes the process, it only delays publish latency.
/// </summary>
public sealed class OutboxRelayHostedService(
    IServiceScopeFactory scopeFactory,
    IConnectionFactory connectionFactory,
    MessageBusManifest manifest,
    ILogger<OutboxRelayHostedService> logger) : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan ReconnectDelay = TimeSpan.FromSeconds(10);
    private const int BatchSize = 50;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var connection = connectionFactory.CreateConnection();
                using var channel = connection.CreateModel();
                RabbitMqTopologyProvisioner.Declare(channel, manifest);

                while (!stoppingToken.IsCancellationRequested)
                {
                    await RelayBatchAsync(channel, stoppingToken);
                    await Task.Delay(PollInterval, stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Wishlist outbox relay lost its RabbitMQ connection; reconnecting in {Delay}.", ReconnectDelay);
                await Task.Delay(ReconnectDelay, stoppingToken);
            }
        }
    }

    private async Task RelayBatchAsync(IModel channel, CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<WishlistDbContext>();

        var batch = await dbContext.WishlistOutboxEvents
            .Where(e => e.EventType == WishlistOutboxEvent.WishlistPriceAlertTriggeredEventType && e.PublishedAt == null)
            .OrderBy(e => e.OccurredAt)
            .Take(BatchSize)
            .ToListAsync(cancellationToken);

        if (batch.Count == 0)
        {
            return;
        }

        var publishedAt = DateTimeOffset.UtcNow;

        foreach (var outboxEvent in batch)
        {
            var properties = channel.CreateBasicProperties();
            properties.Persistent = true;
            properties.MessageId = outboxEvent.OutboxId.ToString();
            properties.ContentType = "application/json";

            channel.BasicPublish(
                exchange: manifest.ExchangeFor(outboxEvent.EventType),
                routingKey: manifest.RoutingKeyFor(outboxEvent.EventType),
                basicProperties: properties,
                body: Encoding.UTF8.GetBytes(outboxEvent.Payload));

            outboxEvent.MarkPublished(publishedAt, "system:wishlist-outbox-poller");
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Relayed {Count} WishlistPriceAlertTriggered event(s) to {Exchange}.", batch.Count, manifest.ExchangeFor(WishlistOutboxEvent.WishlistPriceAlertTriggeredEventType));
    }
}
