using Kart.Wishlist.Application.Common.Interfaces;
using Kart.Wishlist.Domain.Outbox;
using Kart.Shared.Domain;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Kart.Wishlist.Application.Features.RemoveWishlistEntry;

public sealed class RemoveWishlistEntryCommandHandler(
    IWishlistDbContext dbContext,
    IDateTimeProvider dateTimeProvider,
    ILogger<RemoveWishlistEntryCommandHandler> logger)
    : IRequestHandler<RemoveWishlistEntryCommand, Result>
{
    public async Task<Result> Handle(RemoveWishlistEntryCommand request, CancellationToken cancellationToken)
    {
        var entry = await dbContext.WishlistEntries
            .FirstOrDefaultAsync(e => e.UserId == request.UserId && e.Sku == request.Sku, cancellationToken);

        if (entry is null)
        {
            // Absent-sku delete is a no-op success (api-contract.yaml) — no outbox row needed since
            // nothing about this user's wishlist state actually changed.
            logger.LogInformation("Stage {Stage}: remove-wishlist-entry no-op, sku {Sku} was not on user {UserId}'s wishlist", "RemoveWishlistEntryNoOpAbsentSku", request.Sku, request.UserId);
            return Result.Success();
        }

        dbContext.WishlistEntries.Remove(entry);
        var mutationMarker = WishlistOutboxEvent.CreateMutationMarker(request.UserId, request.Sku, dateTimeProvider.UtcNow, request.ActingPrincipalId);
        dbContext.WishlistOutboxEvents.Add(mutationMarker);

        await dbContext.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Stage {Stage}: sku {Sku} removed from user {UserId}'s wishlist, entry {EntryId} removed, outbox event {OutboxId} ({EventType}) enqueued",
            "RemoveWishlistEntryCompleted",
            entry.Sku,
            request.UserId,
            entry.EntryId,
            mutationMarker.OutboxId,
            mutationMarker.EventType);

        return Result.Success();
    }
}
