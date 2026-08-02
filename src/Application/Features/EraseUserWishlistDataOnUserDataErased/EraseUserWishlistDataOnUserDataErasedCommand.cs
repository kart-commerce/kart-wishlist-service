using MediatR;

namespace Kart.Wishlist.Application.Features.EraseUserWishlistDataOnUserDataErased;

/// <summary>
/// WL-8. Consumes <c>UserDataErased</c> (ADR-0016, event-contract.md, compliance-critical tier).
/// Hard-deletes every <c>WishlistEntry</c> and <c>wishlist_alert_dedup</c> row for
/// <see cref="UserId"/>, plus the Redis digest accumulator and the MongoDB read-model document —
/// synchronously, in one handler (design-decisions.md's "Erasure Mechanism for UserDataErased"
/// decision: option 3, not a tombstone, not deferred to a batch job). Idempotent: a redelivered
/// event for an already-erased user finds nothing to delete in any store and is a no-op.
/// </summary>
public sealed record EraseUserWishlistDataOnUserDataErasedCommand(Guid UserId) : IRequest;
