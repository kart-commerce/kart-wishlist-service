using Kart.Wishlist.Application.Common.Interfaces;
using Kart.Wishlist.Domain.Enums;
using Kart.Wishlist.Domain.Outbox;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Kart.Wishlist.Application.Features.ReconcileStaleWishlistEntries;

/// <summary>
/// design-decisions.md's "Resilience & Fan-out Pattern for the Stale-Entry Reconciliation Job" —
/// a bounded-concurrency bulkhead (capped worker pool) with a run-wide failure-rate circuit
/// breaker: if too large a fraction of Product Service calls fail outright (as opposed to
/// cleanly returning "not found"), the whole cycle aborts cleanly and simply retries at the next
/// scheduled run, rather than partially completing or blocking Product Service's own capacity.
/// </summary>
public sealed class ReconcileStaleWishlistEntriesCommandHandler(
    IWishlistDbContext dbContext,
    IProductServiceClient productServiceClient,
    IDateTimeProvider dateTimeProvider,
    ILogger<ReconcileStaleWishlistEntriesCommandHandler> logger)
    : IRequestHandler<ReconcileStaleWishlistEntriesCommand, int>
{
    private const string ActingPrincipal = "system:wishlist-reconciliation-job";
    private const int MaxConcurrency = 10;

    /// <summary>Run-wide circuit breaker: abort the cycle once more than this fraction of calls
    /// fail outright (timeouts/exceptions, not clean "not found" responses) — a symptom of
    /// Product Service itself being degraded, not of individual discontinued SKUs.</summary>
    private const double FailureRateAbortThreshold = 0.5;

    public async Task<int> Handle(ReconcileStaleWishlistEntriesCommand request, CancellationToken cancellationToken)
    {
        // idx_wishlist_entries_status_sku (database-design.md) backs this "distinct active skus
        // across the whole table" scan with an index-only scan rather than a full-table scan.
        var skus = await dbContext.WishlistEntries
            .Where(e => e.Status == WishlistEntryStatus.Active)
            .Select(e => e.Sku)
            .Distinct()
            .ToListAsync(cancellationToken);

        if (skus.Count == 0)
        {
            return 0;
        }

        using var semaphore = new SemaphoreSlim(MaxConcurrency);
        var errorCount = 0;
        var staleSkus = new List<string>();
        var staleSkusLock = new object();
        var errorCountLock = new object();
        var minSamplesBeforeAbort = Math.Max(5, skus.Count / 10);

        var tasks = skus.Select(async sku =>
        {
            await semaphore.WaitAsync(cancellationToken);
            try
            {
                Common.Models.ProductInfo? product;
                try
                {
                    product = await productServiceClient.GetProductAsync(sku, cancellationToken);
                }
                catch (Exception ex)
                {
                    lock (errorCountLock)
                    {
                        errorCount++;
                    }

                    logger.LogWarning(ex, "Reconciliation check failed for {Sku}; will retry next cycle.", sku);
                    return;
                }

                if (product is null || !product.IsActive)
                {
                    lock (staleSkusLock)
                    {
                        staleSkus.Add(sku);
                    }
                }
            }
            finally
            {
                semaphore.Release();
            }
        }).ToList();

        await Task.WhenAll(tasks);

        // Circuit-breaker abort: too large a fraction of calls failed outright — this run's
        // findings are unreliable (may reflect Product Service's own outage, not real
        // discontinuations), so skip applying any stale-marking this cycle and let the next
        // scheduled run try again once the dependency has recovered.
        if (skus.Count >= minSamplesBeforeAbort && (double)errorCount / skus.Count > FailureRateAbortThreshold)
        {
            logger.LogError(
                "Reconciliation cycle aborted: {ErrorCount}/{TotalCount} Product Service calls failed outright — treating this as a degraded dependency, not real discontinuations.",
                errorCount,
                skus.Count);
            return 0;
        }

        if (staleSkus.Count == 0)
        {
            return 0;
        }

        var now = dateTimeProvider.UtcNow;
        var affectedEntries = await dbContext.WishlistEntries
            .Where(e => e.Status == WishlistEntryStatus.Active && staleSkus.Contains(e.Sku))
            .ToListAsync(cancellationToken);

        foreach (var entry in affectedEntries)
        {
            entry.MarkStale(now, ActingPrincipal);
            dbContext.WishlistOutboxEvents.Add(WishlistOutboxEvent.CreateMutationMarker(entry.UserId, entry.Sku, now, ActingPrincipal));
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Reconciliation cycle marked {EntryCount} entries stale across {SkuCount} discontinued/missing SKU(s).", affectedEntries.Count, staleSkus.Count);
        return affectedEntries.Count;
    }
}
