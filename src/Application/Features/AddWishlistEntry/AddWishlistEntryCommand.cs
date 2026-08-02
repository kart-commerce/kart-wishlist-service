using Kart.Wishlist.Application.Common.Models;
using Kart.Shared.Domain;
using MediatR;

namespace Kart.Wishlist.Application.Features.AddWishlistEntry;

/// <summary>
/// WL-2. api-contract.yaml <c>POST /wishlist</c>. Creates a <c>WishlistEntry</c> with
/// <c>ReferencePrice</c> set to the price observed right now (no retroactive alert for an
/// already-discounted product — edge-cases.md's "Wishlist Entry Added After the Price Drop
/// Already Happened" decision). Rejects if the (userId, sku) pair already exists, the sku does
/// not resolve to an active Product Service Variant, or the caller is already at the
/// 500-active-entry cap (ddd-model.md invariant).
/// </summary>
public sealed record AddWishlistEntryCommand(Guid UserId, string Sku, string ActingPrincipalId)
    : IRequest<Result<WishlistEntryResponse>>;
