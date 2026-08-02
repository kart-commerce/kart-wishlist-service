using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Kart.Wishlist.Application.Common.Interfaces;
using Kart.Wishlist.Application.Common.Models;

namespace Kart.Wishlist.Infrastructure.ExternalClients;

/// <summary>
/// HTTP client for Product Service's <c>GET /v1/products/{sku}</c>. Resilience (timeout + retry +
/// circuit breaker) is configured once, declaratively, via
/// <c>Microsoft.Extensions.Http.Resilience</c>'s standard handler at DI registration time
/// (<c>Infrastructure/DependencyInjection.cs</c>) rather than hand-rolled Polly policies here —
/// this is what design-decisions.md's "fail-safe: bounded timeout and circuit breaker" resilience
/// pattern (the digest-send-time re-check) and the reconciliation job's own bulkhead both build on.
/// </summary>
public sealed class ProductServiceClient(HttpClient httpClient) : IProductServiceClient
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    public async Task<ProductInfo?> GetProductAsync(string sku, CancellationToken cancellationToken)
    {
        using var response = await httpClient.GetAsync($"/v1/products/{Uri.EscapeDataString(sku)}", cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadFromJsonAsync<ProductServiceResponse>(SerializerOptions, cancellationToken)
            ?? throw new InvalidOperationException($"Product Service response for '{sku}' deserialized to null.");

        return new ProductInfo(
            body.Sku,
            body.Price.Amount,
            !string.Equals(body.Status, "Discontinued", StringComparison.OrdinalIgnoreCase));
    }

    private sealed record ProductServiceResponse(
        [property: JsonPropertyName("sku")] string Sku,
        [property: JsonPropertyName("price")] ProductServicePrice Price,
        [property: JsonPropertyName("status")] string Status);

    private sealed record ProductServicePrice(
        [property: JsonPropertyName("amount")] decimal Amount,
        [property: JsonPropertyName("currency")] string Currency);
}
