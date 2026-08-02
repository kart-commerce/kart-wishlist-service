namespace Kart.Wishlist.Application.Common.Models;

/// <summary>api-contract.yaml's <c>WishlistEntry</c> schema — also the exact shape of one entry
/// inside the <c>wishlist_read</c> MongoDB document (database-design.md), so this one type serves
/// both the storage shape and the HTTP response shape rather than duplicating it as parallel DTOs
/// (kart-cart-service's <c>CartResponse</c> precedent).</summary>
public sealed record WishlistEntryResponse(
    string Sku,
    decimal ReferencePrice,
    string Status,
    DateTimeOffset AddedAt);

/// <summary>api-contract.yaml's <c>GET /wishlist</c> response envelope — cursor-based pagination
/// (requirement-spec §6 item 5's API Design Agent resolution).</summary>
public sealed record WishlistPageResponse(IReadOnlyList<WishlistEntryResponse> Items, string? NextCursor);
