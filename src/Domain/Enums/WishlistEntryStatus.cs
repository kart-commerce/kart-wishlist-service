namespace Kart.Wishlist.Domain.Enums;

/// <summary>
/// database-design.md's <c>wishlist_entries.status</c> CHECK constraint domain
/// (<c>'active' | 'stale'</c>). <see cref="Stale"/> is set by either the
/// <c>ProductDiscontinued</c> consumer or the hourly reconciliation job (requirement-spec §2, §4)
/// — non-destructive, the row still exists for the client to see and remove
/// (ddd-model.md: "a stale entry is excluded from ProductPriceChanged evaluation and surfaced to
/// the client as no-longer-purchasable, but is not itself deleted").
/// </summary>
public enum WishlistEntryStatus
{
    Active,
    Stale,
}
