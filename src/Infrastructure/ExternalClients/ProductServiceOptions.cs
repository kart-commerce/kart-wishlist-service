namespace Kart.Wishlist.Infrastructure.ExternalClients;

/// <summary>architecture.md: Wishlist's one synchronous outbound dependency, Product Service's
/// own <c>GET /v1/products/{sku}</c> (BRD §5.4).</summary>
public sealed class ProductServiceOptions
{
    public const string SectionName = "ProductService";

    public string BaseUrl { get; set; } = "http://localhost:6000";
}
