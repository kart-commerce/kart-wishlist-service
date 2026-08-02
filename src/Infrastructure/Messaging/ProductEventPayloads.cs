using System.Text.Json.Serialization;

namespace Kart.Wishlist.Infrastructure.Messaging;

/// <summary>event-contract.md: consumed from <c>kart-product-service</c> via
/// <c>product.price.changed</c>.</summary>
public sealed record ProductPriceChangedEventPayload(
    [property: JsonPropertyName("sku")] string Sku,
    [property: JsonPropertyName("oldPrice")] decimal OldPrice,
    [property: JsonPropertyName("newPrice")] decimal NewPrice,
    [property: JsonPropertyName("occurredAt")] DateTimeOffset OccurredAt);

/// <summary>event-contract.md: consumed from <c>kart-product-service</c> via
/// <c>product.product.discontinued</c>.</summary>
public sealed record ProductDiscontinuedEventPayload(
    [property: JsonPropertyName("sku")] string Sku,
    [property: JsonPropertyName("discontinuedAt")] DateTimeOffset DiscontinuedAt);
