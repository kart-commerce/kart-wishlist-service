using Kart.Wishlist.Application.Common.Interfaces;
using Kart.Shared.Auditing;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Kart.Wishlist.Application.Features.EraseUserWishlistDataOnUserDataErased;

public sealed class EraseUserWishlistDataOnUserDataErasedCommandHandler(
    IWishlistDbContext dbContext,
    IWishlistReadModelRepository readModel,
    IWishlistDigestAccumulator digestAccumulator,
    IAuditLogWriter auditLogWriter)
    : IRequestHandler<EraseUserWishlistDataOnUserDataErasedCommand>
{
    public async Task Handle(EraseUserWishlistDataOnUserDataErasedCommand request, CancellationToken cancellationToken)
    {
        // idx_wishlist_entries_user_status / idx_wishlist_alert_dedup_user_sku (database-design.md)
        // back both deletes with an index scan.
        var entries = await dbContext.WishlistEntries.Where(e => e.UserId == request.UserId).ToListAsync(cancellationToken);
        var dedupRows = await dbContext.WishlistAlertDedups.Where(d => d.UserId == request.UserId).ToListAsync(cancellationToken);

        if (entries.Count > 0)
        {
            dbContext.WishlistEntries.RemoveRange(entries);
        }

        if (dedupRows.Count > 0)
        {
            dbContext.WishlistAlertDedups.RemoveRange(dedupRows);
        }

        if (entries.Count > 0 || dedupRows.Count > 0)
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        // Always attempt these three, even on an already-erased user (idempotent no-op there) —
        // in case an earlier delivery partially applied (e.g. crashed after the Postgres delete
        // but before the Redis/Mongo cleanup completed).
        await digestAccumulator.RemoveUserAsync(request.UserId, cancellationToken);
        await readModel.DeleteByUserIdAsync(request.UserId, cancellationToken);

        await auditLogWriter.WriteAsync(AuditLogEntry.Create(
            serviceName: "kart-wishlist-service",
            actorId: "system:user-service-erasure-consumer",
            actorType: "system",
            action: "wishlist.erased",
            entityType: "WishlistEntry",
            entityId: request.UserId.ToString(),
            metadata: new Dictionary<string, object?> { ["entryCount"] = entries.Count, ["dedupRowCount"] = dedupRows.Count }),
            cancellationToken);
    }
}
