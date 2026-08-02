using Kart.Wishlist.Domain.Outbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Kart.Wishlist.Infrastructure.Persistence.Configurations;

/// <summary>database-design.md's <c>wishlist_outbox_events</c> table — the Transactional Outbox.
/// See <see cref="WishlistOutboxEvent"/>'s own remarks for why <c>event_type</c> carries two
/// distinct values (one externally published, one an internal read-model-projection marker) and
/// why <c>published_at</c>/<c>projected_at</c> are two independent completion markers.</summary>
public sealed class WishlistOutboxEventConfiguration : IEntityTypeConfiguration<WishlistOutboxEvent>
{
    public void Configure(EntityTypeBuilder<WishlistOutboxEvent> builder)
    {
        builder.ToTable("wishlist_outbox_events", t => t.HasCheckConstraint(
            "ck_wishlist_outbox_events_event_type",
            $"event_type IN ('{WishlistOutboxEvent.WishlistPriceAlertTriggeredEventType}', '{WishlistOutboxEvent.EntryMutatedEventType}')"));

        builder.HasKey(e => e.OutboxId);
        builder.Property(e => e.OutboxId).HasColumnName("outbox_id").HasDefaultValueSql("gen_random_uuid()");

        builder.Property(e => e.UserId).HasColumnName("user_id").IsRequired();
        builder.Property(e => e.Sku).HasColumnName("sku").IsRequired();
        builder.Property(e => e.EventType).HasColumnName("event_type").IsRequired();
        builder.Property(e => e.Payload).HasColumnName("payload").HasColumnType("jsonb").IsRequired();
        builder.Property(e => e.OccurredAt).HasColumnName("occurred_at").IsRequired();
        builder.Property(e => e.PublishedAt).HasColumnName("published_at");
        builder.Property(e => e.ProjectedAt).HasColumnName("projected_at");
        builder.Property(e => e.CreatedBy).HasColumnName("created_by").IsRequired();
        builder.Property(e => e.UpdatedAt).HasColumnName("updated_at").IsRequired();
        builder.Property(e => e.UpdatedBy).HasColumnName("updated_by").IsRequired();

        // The Outbox relay's own scan: "find everything not yet published (WishlistPriceAlertTriggered
        // rows only, see OutboxRelayHostedService), oldest first."
        builder.HasIndex(e => e.OccurredAt, "idx_wishlist_outbox_unpublished").HasFilter("published_at IS NULL");

        // The read-model projector's own scan: "find everything not yet projected, regardless of
        // event_type" — a distinct index name is required since EF Core would otherwise treat two
        // HasIndex(e => e.OccurredAt) calls as configuring the same index (kart-cart-service precedent).
        builder.HasIndex(e => e.OccurredAt, "idx_wishlist_outbox_unprojected").HasFilter("projected_at IS NULL");
    }
}
