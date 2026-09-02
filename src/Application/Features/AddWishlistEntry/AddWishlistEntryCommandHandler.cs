using Kart.Wishlist.Application.Common.Exceptions;
using Kart.Wishlist.Application.Common.Interfaces;
using Kart.Wishlist.Application.Common.Mapping;
using Kart.Wishlist.Application.Common.Models;
using Kart.Wishlist.Domain.Entities;
using Kart.Wishlist.Domain.Outbox;
using Kart.Shared.Domain;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Kart.Wishlist.Application.Features.AddWishlistEntry;

public sealed class AddWishlistEntryCommandHandler(
    IWishlistDbContext dbContext,
    IUnitOfWork unitOfWork,
    IProductServiceClient productServiceClient,
    IDateTimeProvider dateTimeProvider,
    ILogger<AddWishlistEntryCommandHandler> logger)
    : IRequestHandler<AddWishlistEntryCommand, Result<WishlistEntryResponse>>
{
    public async Task<Result<WishlistEntryResponse>> Handle(AddWishlistEntryCommand request, CancellationToken cancellationToken)
    {
        var product = await productServiceClient.GetProductAsync(request.Sku, cancellationToken);
        if (product is null || !product.IsActive)
        {
            logger.LogWarning("Stage {Stage}: add-to-wishlist rejected, sku {Sku} does not resolve to an active product", "SkuNotFoundValidationFailed", request.Sku);
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
                logger.LogWarning("Stage {Stage}: add-to-wishlist no-op, sku {Sku} is already on user {UserId}'s wishlist", "SkuAlreadyWishlistedNoOp", request.Sku, request.UserId);
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
                logger.LogWarning("Stage {Stage}: add-to-wishlist rejected, user {UserId} is at the {Limit}-entry limit", "WishlistSizeLimitExceededValidationFailed", request.UserId, WishlistEntry.MaxActiveEntriesPerUser);
                return Result.Failure<WishlistEntryResponse>(
                    Error.Custom("wishlist_size_limit_exceeded", $"Wishlist is already at its {WishlistEntry.MaxActiveEntriesPerUser}-entry limit."));
            }

            var entry = WishlistEntry.Create(request.UserId, request.Sku, product.Price, now, request.ActingPrincipalId);
            dbContext.WishlistEntries.Add(entry);
            var mutationMarker = WishlistOutboxEvent.CreateMutationMarker(request.UserId, request.Sku, now, request.ActingPrincipalId);
            dbContext.WishlistOutboxEvents.Add(mutationMarker);

            await unitOfWork.SaveChangesAsync(cancellationToken);
            await unitOfWork.CommitTransactionAsync(cancellationToken);

            logger.LogInformation(
                "Stage {Stage}: sku {Sku} added to user {UserId}'s wishlist, entry {EntryId} persisted, outbox event {OutboxId} ({EventType}) enqueued",
                "AddWishlistEntryCompleted",
                entry.Sku,
                request.UserId,
                entry.EntryId,
                mutationMarker.OutboxId,
                mutationMarker.EventType);

            return Result.Success(WishlistEntryMapper.ToResponse(entry));
        }
        catch (DuplicateKeyException)
        {
            await unitOfWork.RollbackTransactionAsync(cancellationToken);
            logger.LogWarning("Stage {Stage}: add-to-wishlist no-op, sku {Sku} lost the uq_wishlist_entries race for user {UserId}", "SkuAlreadyWishlistedNoOp", request.Sku, request.UserId);
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
