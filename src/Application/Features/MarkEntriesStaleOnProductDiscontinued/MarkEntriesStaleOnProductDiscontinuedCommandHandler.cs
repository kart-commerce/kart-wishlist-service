using Kart.Wishlist.Application.Common.Interfaces;
using Kart.Wishlist.Domain.Enums;
using Kart.Wishlist.Domain.Outbox;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Kart.Wishlist.Application.Features.MarkEntriesStaleOnProductDiscontinued;

public sealed class MarkEntriesStaleOnProductDiscontinuedCommandHandler(
    IWishlistDbContext dbContext,
    IDateTimeProvider dateTimeProvider)
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
            return;
        }

        var now = dateTimeProvider.UtcNow;

        foreach (var entry in entries)
        {
            entry.MarkStale(now, ActingPrincipal);
            dbContext.WishlistOutboxEvents.Add(WishlistOutboxEvent.CreateMutationMarker(entry.UserId, entry.Sku, now, ActingPrincipal));
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
