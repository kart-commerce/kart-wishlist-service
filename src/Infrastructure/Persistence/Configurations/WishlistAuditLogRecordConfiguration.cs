using Kart.Wishlist.Infrastructure.Auditing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Kart.Wishlist.Infrastructure.Persistence.Configurations;

/// <summary>BRD §24.3 — this service's own audit trail table, backing <c>EfCoreAuditLogWriter</c>.</summary>
public sealed class WishlistAuditLogRecordConfiguration : IEntityTypeConfiguration<WishlistAuditLogRecord>
{
    public void Configure(EntityTypeBuilder<WishlistAuditLogRecord> builder)
    {
        builder.ToTable("wishlist_audit_log");

        builder.HasKey(r => r.EntryId);
        builder.Property(r => r.EntryId).HasColumnName("entry_id");

        builder.Property(r => r.ServiceName).HasColumnName("service_name").IsRequired();
        builder.Property(r => r.ActorId).HasColumnName("actor_id").IsRequired();
        builder.Property(r => r.ActorType).HasColumnName("actor_type").IsRequired();
        builder.Property(r => r.Action).HasColumnName("action").IsRequired();
        builder.Property(r => r.EntityType).HasColumnName("entity_type").IsRequired();
        builder.Property(r => r.EntityId).HasColumnName("entity_id").IsRequired();
        builder.Property(r => r.OccurredAt).HasColumnName("occurred_at").IsRequired();
        builder.Property(r => r.MetadataJson).HasColumnName("metadata").HasColumnType("jsonb");

        // "Every audit action recorded against a given entity" — the read pattern an audit trail
        // actually needs (e.g. "everything ever done to this userId").
        builder.HasIndex(r => new { r.EntityType, r.EntityId }).HasDatabaseName("idx_wishlist_audit_log_entity");
    }
}
