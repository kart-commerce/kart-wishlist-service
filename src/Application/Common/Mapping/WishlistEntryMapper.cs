using Kart.Wishlist.Application.Common.Models;
using Kart.Wishlist.Domain.Entities;

namespace Kart.Wishlist.Application.Common.Mapping;

public static class WishlistEntryMapper
{
    public static WishlistEntryResponse ToResponse(WishlistEntry entry) => new(
        Sku: entry.Sku,
        ReferencePrice: entry.ReferencePrice,
        Status: entry.Status.ToString().ToLowerInvariant(),
        AddedAt: entry.AddedAt);
}
