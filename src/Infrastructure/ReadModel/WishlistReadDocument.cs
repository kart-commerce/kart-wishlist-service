using MongoDB.Bson.Serialization.Attributes;

namespace Kart.Wishlist.Infrastructure.ReadModel;

/// <summary>
/// database-design.md's <c>wishlist_read</c> document shape — one document per <c>userId</c>,
/// projected from <c>wishlist_entries</c> in full on every mutation ("the same 'denormalize the
/// whole owned collection per owner' shape kart-cart-service's own Cart read model uses,
/// appropriate here since a user's wishlist is bounded [max 500 entries]"). Kept as its own
/// BSON-attributed type (rather than reusing Application's <c>WishlistEntryResponse</c> POCO
/// directly) so Application has no dependency on MongoDB.Driver.
/// </summary>
public sealed class WishlistReadDocument
{
    [BsonId]
    public Guid Id { get; set; }

    [BsonElement("entries")]
    public List<WishlistReadEntryDocument> Entries { get; set; } = [];

    [BsonElement("updatedAt")]
    public DateTime UpdatedAt { get; set; }
}

public sealed class WishlistReadEntryDocument
{
    [BsonElement("sku")]
    public string Sku { get; set; } = string.Empty;

    [BsonElement("referencePrice")]
    public decimal ReferencePrice { get; set; }

    [BsonElement("status")]
    public string Status { get; set; } = string.Empty;

    [BsonElement("addedAt")]
    public DateTime AddedAt { get; set; }
}
