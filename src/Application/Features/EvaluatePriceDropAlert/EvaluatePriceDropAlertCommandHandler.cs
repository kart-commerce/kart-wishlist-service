using Kart.Wishlist.Application.Common.Interfaces;
using Kart.Wishlist.Domain.Entities;
using Kart.Wishlist.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Kart.Wishlist.Application.Features.EvaluatePriceDropAlert;

public sealed class EvaluatePriceDropAlertCommandHandler(
    IWishlistDbContext dbContext,
    IWishlistDigestAccumulator digestAccumulator,
    IDateTimeProvider dateTimeProvider,
    ILogger<EvaluatePriceDropAlertCommandHandler> logger)
    : IRequestHandler<EvaluatePriceDropAlertCommand>
{
    public async Task Handle(EvaluatePriceDropAlertCommand request, CancellationToken cancellationToken)
    {
        var now = dateTimeProvider.UtcNow;

        // idx_wishlist_entries_sku (database-design.md, partial index on status='active') backs
        // this fan-out from one event to every affected (userId, sku) row without a full-table scan.
        var candidates = await dbContext.WishlistEntries
            .Where(e => e.Sku == request.Sku && e.Status == WishlistEntryStatus.Active)
            .ToListAsync(cancellationToken);

        if (candidates.Count == 0)
        {
            return;
        }

        var dedupRowsToAdd = new List<WishlistAlertDedup>();

        foreach (var entry in candidates)
        {
            if (!entry.IsAlertWorthy(request.NewPrice) || entry.IsCooldownActive(now))
            {
                continue;
            }

            // Redelivery-idempotency guard (edge-cases.md's "Duplicate Alert Delivery" decision):
            // a redelivered ProductPriceChanged for a price already alerted on for this
            // (userId, sku) pair must not re-queue a second trigger.
            var alreadyAlerted = await dbContext.WishlistAlertDedups.AnyAsync(
                d => d.UserId == entry.UserId && d.Sku == entry.Sku && d.PriceObserved == request.NewPrice,
                cancellationToken);
            if (alreadyAlerted)
            {
                continue;
            }

            dedupRowsToAdd.Add(WishlistAlertDedup.Create(entry.UserId, entry.Sku, request.NewPrice, now));
            await digestAccumulator.EnqueueAsync(entry.UserId, entry.Sku, request.OldPrice, request.NewPrice, now, cancellationToken);
        }

        if (dedupRowsToAdd.Count == 0)
        {
            return;
        }

        dbContext.WishlistAlertDedups.AddRange(dedupRowsToAdd);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex)
        {
            // A concurrent redelivery of the same event landed mid-batch and won the unique-
            // constraint race on uq_wishlist_alert_dedup for one or more rows in this batch — the
            // redelivery this loses to will simply be retried by the consumer's own retry-ladder;
            // no data was lost, this batch's remaining qualifying entries just wait for the next
            // qualifying ProductPriceChanged.
            logger.LogWarning(ex, "Dedup insert batch for {Sku} at {NewPrice} hit a concurrent duplicate; will retry on next qualifying delivery.", request.Sku, request.NewPrice);
        }
    }
}
