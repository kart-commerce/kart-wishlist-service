using Kart.Wishlist.Domain.Enums;

namespace Kart.Wishlist.Domain.Entities;

/// <summary>
/// database-design.md's <c>wishlist_entries</c> table — this service's one aggregate root
/// (ddd-model.md), identified by <see cref="EntryId"/>, unique on <see cref="UserId"/>/<see cref="Sku"/>.
/// <see cref="UserId"/> and <see cref="Sku"/> are opaque foreign keys into Identity/User Service's
/// and Product Service's own identities respectively (ddd-model.md's Anti-Corruption Layer rule) —
/// never validated or joined against another service's table.
///
/// Not modeled as an <c>AggregateRoot</c>/domain-event-collecting type (Kart.Shared.Domain): unlike
/// kart-category-service's single-aggregate context, this aggregate's Outbox row is written once
/// per user per digest flush (design-decisions.md's batching decision), not once per
/// <see cref="WishlistEntry"/> mutation — an in-process domain-event list tied 1:1 to this
/// entity's own mutations would not map cleanly onto that write cadence. This mirrors
/// kart-product-service's own <c>Variant</c>, which opts out of the shared base for the same
/// "outbox cadence doesn't match entity mutation cadence" reason.
/// </summary>
public sealed class WishlistEntry
{
    /// <summary>requirement-spec §4, §6 item 2: an inbound <c>ProductPriceChanged</c> is
    /// alert-worthy only if the new price is at least 5% below the tracked reference price.</summary>
    private const decimal AlertThresholdFraction = 0.05m;

    /// <summary>requirement-spec §4, §6 item 3: at most one alert per (userId, sku) pair per
    /// rolling 24-hour window, regardless of how many further qualifying drops occur inside it.</summary>
    private static readonly TimeSpan AlertCooldownWindow = TimeSpan.FromHours(24);

    /// <summary>ddd-model.md invariant: a user may hold at most 500 active entries.</summary>
    public const int MaxActiveEntriesPerUser = 500;

    public Guid EntryId { get; private set; }

    public Guid UserId { get; private set; }

    public string Sku { get; private set; } = string.Empty;

    /// <summary>ddd-model.md's <c>ReferencePrice</c> value object — the baseline this entry's
    /// 5%-drop evaluation runs against. Set to the price observed at add-time (no retroactive
    /// backfill — edge-cases.md's "Wishlist Entry Added After the Price Drop Already Happened"
    /// decision), reset to the alerted price every time <c>WishlistPriceAlertTriggered</c> fires.</summary>
    public decimal ReferencePrice { get; private set; }

    public WishlistEntryStatus Status { get; private set; }

    /// <summary>ddd-model.md's <c>AlertCooldownState</c> value object. Null = never alerted.</summary>
    public DateTimeOffset? LastAlertedAt { get; private set; }

    public DateTimeOffset AddedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public string CreatedBy { get; private set; } = string.Empty;

    public string UpdatedBy { get; private set; } = string.Empty;

    /// <summary>EF Core materialization constructor.</summary>
    private WishlistEntry()
    {
    }

    public static WishlistEntry Create(Guid userId, string sku, decimal referencePrice, DateTimeOffset now, string createdBy) =>
        new()
        {
            EntryId = Guid.NewGuid(),
            UserId = userId,
            Sku = sku,
            ReferencePrice = referencePrice,
            Status = WishlistEntryStatus.Active,
            LastAlertedAt = null,
            AddedAt = now,
            UpdatedAt = now,
            CreatedBy = createdBy,
            UpdatedBy = createdBy,
        };

    /// <summary>requirement-spec §4: an inbound price is alert-worthy only if it is at least 5%
    /// below the current <see cref="ReferencePrice"/>.</summary>
    public bool IsAlertWorthy(decimal newPrice) => newPrice <= ReferencePrice * (1 - AlertThresholdFraction);

    /// <summary>requirement-spec §4, §6 item 3: at most one alert per rolling 24-hour window.</summary>
    public bool IsCooldownActive(DateTimeOffset now) => LastAlertedAt is { } lastAlertedAt && now - lastAlertedAt < AlertCooldownWindow;

    /// <summary>
    /// ddd-model.md: "reset to the newly-alerted price every time WishlistPriceAlertTriggered
    /// fires, so the next evaluation requires another 5%+ drop from that new, lower point." Called
    /// by the digest-flush handler once an alert for this entry actually publishes — not at
    /// qualify-time (design-decisions.md's batching decision).
    /// </summary>
    public void ResetReferencePriceAfterAlert(decimal newReferencePrice, DateTimeOffset now, string updatedBy)
    {
        ReferencePrice = newReferencePrice;
        LastAlertedAt = now;
        UpdatedAt = now;
        UpdatedBy = updatedBy;
    }

    /// <summary>
    /// ddd-model.md: "Stale-entry marking is one-directional and non-destructive" — set by either
    /// the <c>ProductDiscontinued</c> consumer or the hourly reconciliation job. Idempotent: marking
    /// an already-stale entry stale again is a no-op (no audit-column churn on repeat delivery).
    /// </summary>
    public void MarkStale(DateTimeOffset now, string updatedBy)
    {
        if (Status == WishlistEntryStatus.Stale)
        {
            return;
        }

        Status = WishlistEntryStatus.Stale;
        UpdatedAt = now;
        UpdatedBy = updatedBy;
    }
}
