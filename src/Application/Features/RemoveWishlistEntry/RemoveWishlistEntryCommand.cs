using Kart.Shared.Domain;
using MediatR;

namespace Kart.Wishlist.Application.Features.RemoveWishlistEntry;

/// <summary>
/// WL-3. api-contract.yaml <c>DELETE /wishlist/{sku}</c>. Idempotent by standard REST DELETE
/// semantics — removing an sku already absent from the caller's wishlist (or never present) is a
/// no-op success, so a client retry after a dropped response is always safe.
/// </summary>
public sealed record RemoveWishlistEntryCommand(Guid UserId, string Sku, string ActingPrincipalId) : IRequest<Result>;
