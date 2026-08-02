namespace Kart.Wishlist.Domain.Entities;

/// <summary>
/// database-design.md's <c>wishlist_alert_dedup</c> table — the redelivery-idempotency guard
/// (design-decisions.md's "Idempotent Alert Publication" decision; edge-cases.md's "Duplicate
/// Alert Delivery from At-Least-Once Redelivery" decision). One row per price point an alert has
/// actually qualified for a given (userId, sku) — deliberately not a single "last alerted price"
/// column on <see cref="WishlistEntry"/> itself, because it must survive the same price being
/// re-announced under a different message id (republish/backfill), not just a literal redelivery
/// of the same message. The unique constraint on (userId, sku, priceObserved) IS the idempotency
/// mechanism: a constraint violation on insert means "already alerted on this exact price, skip."
/// </summary>
public sealed class WishlistAlertDedup
{
    public Guid DedupId { get; private set; }

    public Guid UserId { get; private set; }

    public string Sku { get; private set; } = string.Empty;

    public decimal PriceObserved { get; private set; }

    public DateTimeOffset AlertedAt { get; private set; }

    public string CreatedBy { get; private set; } = "system:wishlist-price-evaluator";

    public DateTimeOffset UpdatedAt { get; private set; }

    public string UpdatedBy { get; private set; } = "system:wishlist-price-evaluator";

    /// <summary>EF Core materialization constructor.</summary>
    private WishlistAlertDedup()
    {
    }

    public static WishlistAlertDedup Create(Guid userId, string sku, decimal priceObserved, DateTimeOffset now) =>
        new()
        {
            DedupId = Guid.NewGuid(),
            UserId = userId,
            Sku = sku,
            PriceObserved = priceObserved,
            AlertedAt = now,
            UpdatedAt = now,
        };
}
