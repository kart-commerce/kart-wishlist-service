using Kart.Wishlist.Application.Common.Interfaces;
using Kart.Wishlist.Domain.Entities;
using Kart.Wishlist.Domain.Outbox;
using Kart.Wishlist.Infrastructure.Auditing;
using Microsoft.EntityFrameworkCore;

namespace Kart.Wishlist.Infrastructure.Persistence;

public sealed class WishlistDbContext(DbContextOptions<WishlistDbContext> options) : DbContext(options), IWishlistDbContext
{
    public DbSet<WishlistEntry> WishlistEntries => Set<WishlistEntry>();

    public DbSet<WishlistAlertDedup> WishlistAlertDedups => Set<WishlistAlertDedup>();

    public DbSet<WishlistOutboxEvent> WishlistOutboxEvents => Set<WishlistOutboxEvent>();

    /// <summary>BRD §24.3 audit trail — see <see cref="EfCoreAuditLogWriter"/>.</summary>
    public DbSet<WishlistAuditLogRecord> AuditLogRecords => Set<WishlistAuditLogRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasPostgresExtension("pgcrypto");
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(WishlistDbContext).Assembly);
        base.OnModelCreating(modelBuilder);
    }
}
