using System.Text.Json;
using Kart.Wishlist.Infrastructure.Persistence;
using Kart.Shared.Auditing;

namespace Kart.Wishlist.Infrastructure.Auditing;

/// <summary>The first concrete <see cref="IAuditLogWriter"/> implementation on this platform
/// (<c>Kart.Shared.Auditing</c>'s own README: "No service has wired a concrete IAuditLogWriter
/// yet"). Persists to the same PostgreSQL database as the rest of this service's write side, in
/// its own <c>wishlist_audit_log</c> table — a distinct <c>SaveChangesAsync</c> from the business
/// mutation it accompanies (an audit trail write is informational, not itself part of the
/// invariant the business transaction protects).</summary>
public sealed class EfCoreAuditLogWriter(WishlistDbContext dbContext) : IAuditLogWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task WriteAsync(AuditLogEntry entry, CancellationToken cancellationToken = default)
    {
        var metadataJson = entry.Metadata is { Count: > 0 } ? JsonSerializer.Serialize(entry.Metadata, JsonOptions) : null;

        var record = WishlistAuditLogRecord.Create(
            entry.EntryId,
            entry.ServiceName,
            entry.ActorId,
            entry.ActorType,
            entry.Action,
            entry.EntityType,
            entry.EntityId,
            entry.OccurredAt,
            metadataJson);

        dbContext.Set<WishlistAuditLogRecord>().Add(record);
        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
