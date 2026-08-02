namespace Kart.Wishlist.Infrastructure.Auditing;

/// <summary>
/// Persisted form of <c>Kart.Shared.Auditing.AuditLogEntry</c> (BRD §24.3) — this service is the
/// first adopter of a concrete <c>IAuditLogWriter</c> anywhere on the platform (the shared
/// package's README notes no service had wired one yet). Table <c>wishlist_audit_log</c>: a
/// durable, queryable audit trail of every system-initiated mutation this service makes on a
/// user's behalf (currently: GDPR erasure — <see cref="EfCoreAuditLogWriter"/>'s only caller
/// today, WL-8). An append-only table, never updated in place.
/// </summary>
public sealed class WishlistAuditLogRecord
{
    public Guid EntryId { get; private set; }

    public string ServiceName { get; private set; } = string.Empty;

    public string ActorId { get; private set; } = string.Empty;

    public string ActorType { get; private set; } = string.Empty;

    public string Action { get; private set; } = string.Empty;

    public string EntityType { get; private set; } = string.Empty;

    public string EntityId { get; private set; } = string.Empty;

    public DateTimeOffset OccurredAt { get; private set; }

    /// <summary>JSON-serialized <c>AuditLogEntry.Metadata</c>, or null if none was supplied.</summary>
    public string? MetadataJson { get; private set; }

    /// <summary>EF Core materialization constructor.</summary>
    private WishlistAuditLogRecord()
    {
    }

    public static WishlistAuditLogRecord Create(
        Guid entryId, string serviceName, string actorId, string actorType, string action, string entityType, string entityId, DateTimeOffset occurredAt, string? metadataJson) =>
        new()
        {
            EntryId = entryId,
            ServiceName = serviceName,
            ActorId = actorId,
            ActorType = actorType,
            Action = action,
            EntityType = entityType,
            EntityId = entityId,
            OccurredAt = occurredAt,
            MetadataJson = metadataJson,
        };
}
