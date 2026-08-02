using Kart.Wishlist.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Kart.Wishlist.UnitTests.TestSupport;

/// <summary>A fresh, isolated EF Core InMemory-backed <see cref="WishlistDbContext"/> per test —
/// same pattern kart-identity-service's/kart-cart-service's own unit tests use rather than
/// mocking DbSets directly.</summary>
public static class InMemoryWishlistDbContextFactory
{
    public static WishlistDbContext Create()
    {
        var options = new DbContextOptionsBuilder<WishlistDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new WishlistDbContext(options);
    }
}
