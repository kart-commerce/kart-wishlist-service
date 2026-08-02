using Kart.Wishlist.Application.Common.Exceptions;
using Kart.Wishlist.Application.Common.Interfaces;
using Kart.Wishlist.Application.Common.Mapping;
using Kart.Wishlist.Application.Common.Models;
using Kart.Wishlist.Domain.Entities;
using Kart.Wishlist.Domain.Outbox;
using Kart.Shared.Domain;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Kart.Wishlist.Application.Features.AddWishlistEntry;

public sealed class AddWishlistEntryCommandHandler(
    IWishlistDbContext dbContext,
    IUnitOfWork unitOfWork,
    IProductServiceClient productServiceClient,
    IDateTimeProvider dateTimeProvider)
    : IRequestHandler<AddWishlistEntryCommand, Result<WishlistEntryResponse>>
{
    public async Task<Result<WishlistEntryResponse>> Handle(AddWishlistEntryCommand request, CancellationToken cancellationToken)
    {
        var product = await productServiceClient.GetProductAsync(request.Sku, cancellationToken);
        if (product is null || !product.IsActive)
        {
            return Result.Failure<WishlistEntryResponse>(
                Error.Custom("sku_not_found", $"'{request.Sku}' does not resolve to an active product."));
        }

        var now = dateTimeProvider.UtcNow;

        await unitOfWork.BeginTransactionAsync(cancellationToken);
        try
        {
            var alreadyWishlisted = await dbContext.WishlistEntries
                .AnyAsync(e => e.UserId == request.UserId && e.Sku == request.Sku, cancellationToken);
            if (alreadyWishlisted)
            {
                await unitOfWork.RollbackTransactionAsync(cancellationToken);
                return Result.Failure<WishlistEntryResponse>(
                    Error.Custom("sku_already_wishlisted", $"'{request.Sku}' is already on this wishlist."));
            }

            // ddd-model.md invariant: a user may hold at most 500 active entries. The lock held by
            // CountActiveEntriesWithLockAsync (Postgres: SELECT ... FOR UPDATE) is what makes this
            // check-then-insert safe under concurrent AddWishlistEntry requests for the same user —
            // see IUnitOfWork's own remarks.
            var activeCount = await unitOfWork.CountActiveEntriesWithLockAsync(request.UserId, cancellationToken);
            if (activeCount >= WishlistEntry.MaxActiveEntriesPerUser)
            {
                await unitOfWork.RollbackTransactionAsync(cancellationToken);
                return Result.Failure<WishlistEntryResponse>(
                    Error.Custom("wishlist_size_limit_exceeded", $"Wishlist is already at its {WishlistEntry.MaxActiveEntriesPerUser}-entry limit."));
            }

            var entry = WishlistEntry.Create(request.UserId, request.Sku, product.Price, now, request.ActingPrincipalId);
            dbContext.WishlistEntries.Add(entry);
            dbContext.WishlistOutboxEvents.Add(WishlistOutboxEvent.CreateMutationMarker(request.UserId, request.Sku, now, request.ActingPrincipalId));

            await unitOfWork.SaveChangesAsync(cancellationToken);
            await unitOfWork.CommitTransactionAsync(cancellationToken);

            return Result.Success(WishlistEntryMapper.ToResponse(entry));
        }
        catch (DuplicateKeyException)
        {
            await unitOfWork.RollbackTransactionAsync(cancellationToken);
            return Result.Failure<WishlistEntryResponse>(
                Error.Custom("sku_already_wishlisted", $"'{request.Sku}' is already on this wishlist."));
        }
        catch
        {
            await unitOfWork.RollbackTransactionAsync(cancellationToken);
            throw;
        }
    }
}
