using Kart.Wishlist.Application.Common.Interfaces;
using Kart.Wishlist.Domain.Outbox;
using Kart.Shared.Domain;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Kart.Wishlist.Application.Features.RemoveWishlistEntry;

public sealed class RemoveWishlistEntryCommandHandler(
    IWishlistDbContext dbContext,
    IDateTimeProvider dateTimeProvider)
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
            return Result.Success();
        }

        dbContext.WishlistEntries.Remove(entry);
        dbContext.WishlistOutboxEvents.Add(
            WishlistOutboxEvent.CreateMutationMarker(request.UserId, request.Sku, dateTimeProvider.UtcNow, request.ActingPrincipalId));

        await dbContext.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
