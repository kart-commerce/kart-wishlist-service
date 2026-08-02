namespace Kart.Wishlist.Domain.Outbox;

/// <summary>
/// database-design.md's <c>wishlist_outbox_events</c> table — the Transactional Outbox backing
/// reliable publication of <c>WishlistPriceAlertTriggered</c> (design-decisions.md). Does NOT
/// extend <see cref="Kart.Shared.Domain.OutboxEventBase"/>: that base is Guid-<c>AggregateId</c>-keyed,
/// while the approved schema for this table is <c>user_id</c>/<c>sku</c>-keyed (this service's
/// events correlate on a (userId, sku) pair, not a single Guid aggregate) — the same "forcing this
/// table into the shared base's shape would fight the approved DB design" reasoning
/// <c>kart-product-service</c>'s own <c>ProductOutboxEvent</c> documents for itself.
///
/// <see cref="EventType"/> carries two distinct values sharing this one table, mirroring
/// <c>kart-cart-service</c>'s own precedent (its <c>OutboxEvent.CartMutated</c> marker alongside
/// the externally-published <c>CartCheckedOut</c>):
/// <list type="bullet">
/// <item><description><see cref="WishlistPriceAlertTriggeredEventType"/> — a real, externally
/// published event (event-contract.md), relayed to RabbitMQ by <c>OutboxRelayHostedService</c> and
/// tracked via <see cref="PublishedAt"/>.</description></item>
/// <item><description><see cref="EntryMutatedEventType"/> — an internal-only marker, never
/// externally published (not in event-contract.md's Published Events table), written by every
/// write path that mutates <c>wishlist_entries</c> for a user (add/remove/stale-mark) purely to
/// drive the MongoDB read-model re-projection for that user (database-design.md's Read Model
/// section: "every write to wishlist_entries... re-projects that user's document in full").
/// Tracked via the independent <see cref="ProjectedAt"/> marker, exactly like
/// <c>kart-cart-service</c>'s own <c>ProjectedAt</c>/<c>CartMutated</c> resolution of this identical
/// "the read model must reflect every mutation, but only one event type is ever actually
/// published" gap.</description></item>
/// </list>
/// A <see cref="WishlistPriceAlertTriggeredEventType"/> row serves double duty: it is both relayed
/// to RabbitMQ (<see cref="PublishedAt"/>) AND drives read-model projection
/// (<see cref="ProjectedAt"/>), since an alert firing also changes the entry's
/// <c>reference_price</c>/<c>last_alerted_at</c> — no separate marker row is needed for that case.
/// </summary>
public sealed class WishlistOutboxEvent
{
    public const string WishlistPriceAlertTriggeredEventType = "WishlistPriceAlertTriggered";
    public const string EntryMutatedEventType = "WishlistEntryMutated";

    public Guid OutboxId { get; private set; }

    public Guid UserId { get; private set; }

    public string Sku { get; private set; } = string.Empty;

    public string EventType { get; private set; } = string.Empty;

    public string Payload { get; private set; } = string.Empty;

    public DateTimeOffset OccurredAt { get; private set; }

    public DateTimeOffset? PublishedAt { get; private set; }

    /// <summary>Independent of <see cref="PublishedAt"/> — see type-level remarks. Drives the
    /// MongoDB read-model projector, not the RabbitMQ relay.</summary>
    public DateTimeOffset? ProjectedAt { get; private set; }

    public string CreatedBy { get; private set; } = string.Empty;

    public DateTimeOffset UpdatedAt { get; private set; }

    public string UpdatedBy { get; private set; } = string.Empty;

    /// <summary>EF Core materialization constructor.</summary>
    private WishlistOutboxEvent()
    {
    }

    /// <summary>The one row type this service ever externally publishes.</summary>
    public static WishlistOutboxEvent CreateAlertTriggered(Guid userId, string sku, string payloadJson, DateTimeOffset now) =>
        new()
        {
            OutboxId = Guid.NewGuid(),
            UserId = userId,
            Sku = sku,
            EventType = WishlistPriceAlertTriggeredEventType,
            Payload = payloadJson,
            OccurredAt = now,
            CreatedBy = "system:wishlist-digest-flush",
            UpdatedAt = now,
            UpdatedBy = "system:wishlist-digest-flush",
        };

    /// <summary>Internal-only read-model-projection trigger — never relayed to RabbitMQ.</summary>
    public static WishlistOutboxEvent CreateMutationMarker(Guid userId, string sku, DateTimeOffset now, string createdBy) =>
        new()
        {
            OutboxId = Guid.NewGuid(),
            UserId = userId,
            Sku = sku,
            EventType = EntryMutatedEventType,
            Payload = "{}",
            OccurredAt = now,
            CreatedBy = createdBy,
            UpdatedAt = now,
            UpdatedBy = createdBy,
        };

    /// <summary>Called by <c>OutboxRelayHostedService</c> once a row has been published to
    /// RabbitMQ. Throws if already published — the relay only ever selects unpublished rows.</summary>
    public void MarkPublished(DateTimeOffset publishedAt, string updatedBy)
    {
        if (PublishedAt is not null)
        {
            throw new InvalidOperationException($"Outbox event {OutboxId} was already published at {PublishedAt:O} and cannot be re-published.");
        }

        PublishedAt = publishedAt;
        UpdatedAt = publishedAt;
        UpdatedBy = updatedBy;
    }

    /// <summary>Called by <c>ReadModelProjectionHostedService</c> once this user's MongoDB
    /// document has been re-projected. Throws if already projected — the projector only ever
    /// selects unprojected rows.</summary>
    public void MarkProjected(DateTimeOffset projectedAt, string updatedBy)
    {
        if (ProjectedAt is not null)
        {
            throw new InvalidOperationException($"Outbox event {OutboxId} was already projected at {ProjectedAt:O} and cannot be re-projected.");
        }

        ProjectedAt = projectedAt;
        UpdatedAt = projectedAt;
        UpdatedBy = updatedBy;
    }
}
