using Kart.Wishlist.Domain.Entities;
using Kart.Wishlist.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Kart.Wishlist.Infrastructure.Persistence.Configurations;

/// <summary>database-design.md's <c>wishlist_entries</c> table.</summary>
public sealed class WishlistEntryConfiguration : IEntityTypeConfiguration<WishlistEntry>
{
    public void Configure(EntityTypeBuilder<WishlistEntry> builder)
    {
        builder.ToTable("wishlist_entries", t => t.HasCheckConstraint(
            "ck_wishlist_entries_status",
            "status IN ('active', 'stale')"));

        builder.HasKey(e => e.EntryId);
        builder.Property(e => e.EntryId).HasColumnName("entry_id").HasDefaultValueSql("gen_random_uuid()");

        builder.Property(e => e.UserId).HasColumnName("user_id").IsRequired();
        builder.Property(e => e.Sku).HasColumnName("sku").IsRequired();
        builder.Property(e => e.ReferencePrice).HasColumnName("reference_price").HasColumnType("numeric(12,2)").IsRequired();

        builder.Property(e => e.Status)
            .HasColumnName("status")
            .HasConversion(
                status => status.ToString().ToLowerInvariant(),
                value => value == "stale" ? WishlistEntryStatus.Stale : WishlistEntryStatus.Active)
            .HasMaxLength(10)
            .IsRequired();

        builder.Property(e => e.LastAlertedAt).HasColumnName("last_alerted_at");
        builder.Property(e => e.AddedAt).HasColumnName("added_at").IsRequired();
        builder.Property(e => e.UpdatedAt).HasColumnName("updated_at").IsRequired();
        builder.Property(e => e.CreatedBy).HasColumnName("created_by").IsRequired();
        builder.Property(e => e.UpdatedBy).HasColumnName("updated_by").IsRequired();

        // ddd-model.md invariant: a (userId, sku) pair maps to at most one WishlistEntry.
        builder.HasIndex(e => new { e.UserId, e.Sku }).IsUnique().HasDatabaseName("uq_wishlist_entries_user_sku");

        // GET /wishlist projection source; 500-active-entry cap count-check at add-time;
        // UserDataErased bulk delete.
        builder.HasIndex(e => new { e.UserId, e.Status }).HasDatabaseName("idx_wishlist_entries_user_status");

        // ProductPriceChanged/ProductDiscontinued consumer fan-out: "which active entries hold
        // this sku."
        builder.HasIndex(e => e.Sku, "idx_wishlist_entries_sku").HasFilter("status = 'active'");

        // Reconciliation job's "distinct active skus across the whole table" scan.
        builder.HasIndex(e => new { e.Status, e.Sku }).HasDatabaseName("idx_wishlist_entries_status_sku");
    }
}
