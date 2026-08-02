using Kart.Wishlist.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Kart.Wishlist.Infrastructure.Persistence.Configurations;

/// <summary>database-design.md's <c>wishlist_alert_dedup</c> table — the redelivery-idempotency
/// guard for <c>WishlistPriceAlertTriggered</c> publication.</summary>
public sealed class WishlistAlertDedupConfiguration : IEntityTypeConfiguration<WishlistAlertDedup>
{
    public void Configure(EntityTypeBuilder<WishlistAlertDedup> builder)
    {
        builder.ToTable("wishlist_alert_dedup");

        builder.HasKey(d => d.DedupId);
        builder.Property(d => d.DedupId).HasColumnName("dedup_id").HasDefaultValueSql("gen_random_uuid()");

        builder.Property(d => d.UserId).HasColumnName("user_id").IsRequired();
        builder.Property(d => d.Sku).HasColumnName("sku").IsRequired();
        builder.Property(d => d.PriceObserved).HasColumnName("price_observed").HasColumnType("numeric(12,2)").IsRequired();
        builder.Property(d => d.AlertedAt).HasColumnName("alerted_at").IsRequired();
        builder.Property(d => d.CreatedBy).HasColumnName("created_by").IsRequired();
        builder.Property(d => d.UpdatedAt).HasColumnName("updated_at").IsRequired();
        builder.Property(d => d.UpdatedBy).HasColumnName("updated_by").IsRequired();

        // The actual idempotency guard: inserting the same (user_id, sku, price_observed) twice
        // violates this constraint, which the publish path treats as "already alerted, skip."
        builder.HasIndex(d => new { d.UserId, d.Sku, d.PriceObserved }).IsUnique().HasDatabaseName("uq_wishlist_alert_dedup");

        // UserDataErased bulk delete; per-pair dedup lookup before insert.
        builder.HasIndex(d => new { d.UserId, d.Sku }).HasDatabaseName("idx_wishlist_alert_dedup_user_sku");
    }
}
