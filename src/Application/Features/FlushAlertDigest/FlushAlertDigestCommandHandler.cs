using System.Text.Json;
using Kart.Wishlist.Application.Common.Interfaces;
using Kart.Wishlist.Domain.Outbox;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Kart.Wishlist.Application.Features.FlushAlertDigest;

public sealed class FlushAlertDigestCommandHandler(
    IWishlistDbContext dbContext,
    IWishlistDigestAccumulator digestAccumulator,
    IProductServiceClient productServiceClient,
    IDateTimeProvider dateTimeProvider,
    ILogger<FlushAlertDigestCommandHandler> logger)
    : IRequestHandler<FlushAlertDigestCommand>
{
    private static readonly JsonSerializerOptions PayloadOptions = new(JsonSerializerDefaults.Web);

    public async Task Handle(FlushAlertDigestCommand request, CancellationToken cancellationToken)
    {
        // Guards against two overlapping sweep ticks (or a sweep tick racing a slow prior flush)
        // ever processing the same user's digest twice (design-decisions.md's batching decision).
        var lockAcquired = await digestAccumulator.TryAcquireFlushLockAsync(request.UserId, cancellationToken);
        if (!lockAcquired)
        {
            return;
        }

        try
        {
            var pendingItems = await digestAccumulator.DequeueAllAsync(request.UserId, cancellationToken);
            if (pendingItems.Count == 0)
            {
                return;
            }

            var now = dateTimeProvider.UtcNow;
            var outboxRowsToAdd = new List<WishlistOutboxEvent>();

            foreach (var item in pendingItems)
            {
                var entry = await dbContext.WishlistEntries
                    .FirstOrDefaultAsync(e => e.UserId == request.UserId && e.Sku == item.Sku, cancellationToken);

                // The entry was removed or marked stale after it was queued but before this flush —
                // nothing left to alert on.
                if (entry is null || entry.Status != Domain.Enums.WishlistEntryStatus.Active)
                {
                    continue;
                }

                var baseline = entry.ReferencePrice;

                // edge-cases.md's "Price Rebound During the Batching/Digest Window" decision:
                // re-check the current price immediately before send. Fail-safe (design-decisions.md's
                // "Resilience Pattern for the Digest-Send-Time Price Re-Check" decision): a
                // timeout/circuit-open on this call suppresses the item from this cycle rather than
                // sending an unverified price — the entry stays wishlisted, so a later qualifying
                // ProductPriceChanged can still surface it.
                Common.Models.ProductInfo? currentProduct;
                try
                {
                    currentProduct = await productServiceClient.GetProductAsync(item.Sku, cancellationToken);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Digest-send-time price re-check failed for {Sku}; suppressing from this cycle.", item.Sku);
                    continue;
                }

                if (currentProduct is null || !currentProduct.IsActive)
                {
                    continue;
                }

                if (currentProduct.Price >= baseline)
                {
                    // Rebounded to/above the baseline it was triggered on — no drop left to report.
                    continue;
                }

                entry.ResetReferencePriceAfterAlert(currentProduct.Price, now, "system:wishlist-digest-flush");

                var payload = JsonSerializer.Serialize(
                    new { userId = request.UserId, sku = item.Sku, oldPrice = baseline, newPrice = currentProduct.Price },
                    PayloadOptions);

                outboxRowsToAdd.Add(WishlistOutboxEvent.CreateAlertTriggered(request.UserId, item.Sku, payload, now));
            }

            if (outboxRowsToAdd.Count > 0)
            {
                dbContext.WishlistOutboxEvents.AddRange(outboxRowsToAdd);
                await dbContext.SaveChangesAsync(cancellationToken);
            }
        }
        finally
        {
            await digestAccumulator.ReleaseFlushLockAsync(request.UserId, cancellationToken);
        }
    }
}
