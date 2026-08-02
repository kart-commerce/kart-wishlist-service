using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Kart.Wishlist.Infrastructure.Persistence;

/// <summary>Design-time-only factory <c>dotnet ef migrations add</c>/<c>database update</c> use to
/// build <see cref="WishlistDbContext"/> without spinning up the full Api host (matches
/// kart-identity-service's/kart-cart-service's own design-time factory pattern exactly). Never
/// used at runtime — <c>Infrastructure/DependencyInjection.cs</c> takes over there.</summary>
public sealed class WishlistDbContextFactory : IDesignTimeDbContextFactory<WishlistDbContext>
{
    public WishlistDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("WISHLIST_DB_CONNECTION_STRING")
            ?? "Host=localhost;Port=5432;Database=kart_wishlist;Username=postgres;Password=postgres";

        var optionsBuilder = new DbContextOptionsBuilder<WishlistDbContext>();
        optionsBuilder.UseNpgsql(connectionString);

        return new WishlistDbContext(optionsBuilder.Options);
    }
}
