using Kart.Wishlist.Application.Common.Interfaces;
using Kart.Wishlist.Domain.Enums;
using Kart.Wishlist.Domain.Outbox;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Kart.Wishlist.Application.Features.MarkEntriesStaleOnProductDiscontinued;

public sealed class MarkEntriesStaleOnProductDiscontinuedCommandHandler(
    IWishlistDbContext dbContext,
    IDateTimeProvider dateTimeProvider,
    ILogger<MarkEntriesStaleOnProductDiscontinuedCommandHandler> logger)
    : IRequestHandler<MarkEntriesStaleOnProductDiscontinuedCommand>
{
    private const string ActingPrincipal = "system:wishlist-discontinuation-consumer";

    public async Task Handle(MarkEntriesStaleOnProductDiscontinuedCommand request, CancellationToken cancellationToken)
    {
        // idx_wishlist_entries_sku (database-design.md, partial index on status='active').
        var entries = await dbContext.WishlistEntries
            .Where(e => e.Sku == request.Sku && e.Status == WishlistEntryStatus.Active)
            .ToListAsync(cancellationToken);

        if (entries.Count == 0)
        {
            logger.LogInformation("Stage {Stage}: no active wishlist entries hold discontinued sku {Sku}, mark-stale is a no-op", "MarkEntriesStaleNoOpNoActiveEntries", request.Sku);
            return;
        }

        var now = dateTimeProvider.UtcNow;
        var outboxIds = new List<Guid>(entries.Count);

        foreach (var entry in entries)
        {
            entry.MarkStale(now, ActingPrincipal);
            var mutationMarker = WishlistOutboxEvent.CreateMutationMarker(entry.UserId, entry.Sku, now, ActingPrincipal);
            dbContext.WishlistOutboxEvents.Add(mutationMarker);
            outboxIds.Add(mutationMarker.OutboxId);
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Stage {Stage}: {Count} wishlist entry/entries for discontinued sku {Sku} persisted stale, outbox events {OutboxIds} enqueued",
            "WishlistEntriesMarkedStalePersistedOutboxEnqueued",
            entries.Count,
            request.Sku,
            string.Join(",", outboxIds));

        logger.LogInformation(
            "Stage {Stage}: sku {Sku} discontinuation processed, {Count} wishlist entry/entries marked stale",
            "MarkEntriesStaleOnProductDiscontinuedCompleted",
            request.Sku,
            entries.Count);
    }
}
