using Kart.Wishlist.Domain.Entities;
using Kart.Wishlist.Domain.Outbox;
using Microsoft.EntityFrameworkCore;

namespace Kart.Wishlist.Application.Common.Interfaces;

/// <summary>Application owns the interface, Infrastructure implements it (matches
/// kart-cart-service's <c>ICartDbContext</c> pattern) — keeps Application/Domain free of any EF
/// Core provider dependency beyond the abstractions package.</summary>
public interface IWishlistDbContext
{
    DbSet<WishlistEntry> WishlistEntries { get; }

    DbSet<WishlistAlertDedup> WishlistAlertDedups { get; }

    DbSet<WishlistOutboxEvent> WishlistOutboxEvents { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken);
}
