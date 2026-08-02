using Kart.Wishlist.Application.Common.Interfaces;
using Kart.Wishlist.Application.Common.Mapping;
using Kart.Wishlist.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Kart.Wishlist.Infrastructure.Messaging;

/// <summary>
/// The CQRS read-side projector (database-design.md's Read Model section): an in-process poller
/// (no RabbitMQ hop) reading unprojected <c>wishlist_outbox_events</c> rows — regardless of
/// <c>EventType</c>, including the internal <see cref="Domain.Outbox.WishlistOutboxEvent.EntryMutatedEventType"/>
/// marker — and rebuilding each affected user's <c>wishlist_read</c> MongoDB document from the
/// <i>current</i> PostgreSQL write-model state, never from the outbox row's own payload, so the
/// read model is literally rebuildable from the write model (kart-cart-service's
/// <c>ReadModelProjectionHostedService</c> precedent for this exact "one outbox table, two
/// independent completion markers" pattern).
/// </summary>
public sealed class ReadModelProjectionHostedService(
    IServiceScopeFactory scopeFactory,
    ILogger<ReadModelProjectionHostedService> logger) : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(2);
    private const int BatchSize = 100;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProjectPendingBatchAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Wishlist read-model projection cycle failed; will retry next poll.");
            }

            await Task.Delay(PollInterval, stoppingToken);
        }
    }

    private async Task ProjectPendingBatchAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<WishlistDbContext>();
        var readModel = scope.ServiceProvider.GetRequiredService<IWishlistReadModelRepository>();

        // Ordered client-side — the unprojected set is always small in practice, and Sqlite (this
        // repo's own integration tests) cannot translate ORDER BY over DateTimeOffset server-side
        // (kart-cart-service's identical comment on its own OutboxRelayHostedService/
        // ReadModelProjectionHostedService).
        var pending = (await dbContext.WishlistOutboxEvents
                .Where(e => e.ProjectedAt == null)
                .ToListAsync(cancellationToken))
            .OrderBy(e => e.OccurredAt)
            .Take(BatchSize)
            .ToList();

        if (pending.Count == 0)
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;

        foreach (var userId in pending.Select(e => e.UserId).Distinct())
        {
            var entries = await dbContext.WishlistEntries
                .Where(e => e.UserId == userId)
                .OrderBy(e => e.Sku)
                .ToListAsync(cancellationToken);

            var responses = entries.Select(WishlistEntryMapper.ToResponse).ToList();
            await readModel.UpsertUserDocumentAsync(userId, responses, now, cancellationToken);
        }

        foreach (var outboxEvent in pending)
        {
            outboxEvent.MarkProjected(now, "system:wishlist-read-model-projector");
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
