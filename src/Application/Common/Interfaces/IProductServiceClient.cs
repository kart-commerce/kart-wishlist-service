using Kart.Wishlist.Application.Common.Models;

namespace Kart.Wishlist.Application.Common.Interfaces;

/// <summary>
/// The one synchronous outbound dependency this service has (architecture.md's Boundary
/// Rationale) — Product Service's <c>GET /v1/products/{sku}</c> (BRD §5.4), used only by:
/// (1) <c>AddWishlistEntryCommandHandler</c>'s add-time existence/active check, (2) the hourly
/// reconciliation job (<c>ReconcileStaleWishlistEntriesCommandHandler</c>), and (3) the
/// digest-flush price re-check (<c>FlushAlertDigestCommandHandler</c>) — never the client-facing
/// <c>/wishlist</c> read path or the <c>ProductPriceChanged</c> alert evaluation, both of which
/// stay entirely on Wishlist's own local data (architecture.md's Distributed-Monolith Risk
/// section). Returns null if the product does not exist at all; a discontinued-but-still-known
/// product is returned with <see cref="ProductInfo.IsActive"/> false, not null.
/// </summary>
public interface IProductServiceClient
{
    Task<ProductInfo?> GetProductAsync(string sku, CancellationToken cancellationToken);
}
