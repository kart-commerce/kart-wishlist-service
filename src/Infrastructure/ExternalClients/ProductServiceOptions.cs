using Kart.Shared.Configuration;

namespace Kart.Wishlist.Infrastructure.ExternalClients;

/// <summary>architecture.md: Wishlist's one synchronous outbound dependency, Product Service's
/// own <c>GET /v1/products/{sku}</c> (BRD §5.4).</summary>
public sealed class ProductServiceOptions
{
    public const string SectionName = "ProductService";

    /// <summary>Defaults to <see cref="KartServiceEndpoints.ProductLocalBaseUrl"/> — the
    /// platform's single source of truth for this port (mirrors kart-devops/ports.env) —
    /// rather than a duplicated literal.</summary>
    public string BaseUrl { get; set; } = KartServiceEndpoints.ProductLocalBaseUrl;
}
