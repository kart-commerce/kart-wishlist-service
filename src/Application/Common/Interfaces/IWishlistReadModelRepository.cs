using Kart.Wishlist.Application.Common.Models;

namespace Kart.Wishlist.Application.Common.Interfaces;

/// <summary>
/// The MongoDB <c>wishlist_read</c> collection (database-design.md) — the eventually-consistent
/// CQRS query side, one document per <c>userId</c>, sharded on <c>_id</c> (hashed, since
/// <c>userId</c> is a high-cardinality opaque identifier with no natural range-locality the way
/// Product's <c>category.id</c> has). Application owns the interface; Infrastructure implements it
/// against the MongoDB driver (kart-cart-service's <c>ICartReadModelRepository</c> pattern).
/// </summary>
public interface IWishlistReadModelRepository
{
    Task<IReadOnlyList<WishlistEntryResponse>?> GetByUserIdAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>Whole-document upsert keyed by <c>userId</c> — idempotent by construction, the
    /// read-model projector's own re-projection mechanism (database-design.md: "every write to
    /// wishlist_entries... re-projects that user's document in full").</summary>
    Task UpsertUserDocumentAsync(Guid userId, IReadOnlyList<WishlistEntryResponse> entries, DateTimeOffset updatedAt, CancellationToken cancellationToken);

    /// <summary>Used by the <c>UserDataErased</c> handler (ADR-0016) — "an erased user has no
    /// wishlist to read, not an empty one lingering as a stale projection" (database-design.md).</summary>
    Task DeleteByUserIdAsync(Guid userId, CancellationToken cancellationToken);
}
