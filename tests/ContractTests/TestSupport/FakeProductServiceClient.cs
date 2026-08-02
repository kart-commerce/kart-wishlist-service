using System.Collections.Concurrent;
using Kart.Wishlist.Application.Common.Interfaces;
using Kart.Wishlist.Application.Common.Models;

namespace Kart.Wishlist.ContractTests.TestSupport;

/// <summary>Test double for <see cref="IProductServiceClient"/> — no real Product Service in this
/// test environment. Tests seed known SKUs via <see cref="Seed"/> before exercising an endpoint.</summary>
public sealed class FakeProductServiceClient : IProductServiceClient
{
    private readonly ConcurrentDictionary<string, ProductInfo> _products = new();

    public void Seed(string sku, decimal price, bool isActive = true) => _products[sku] = new ProductInfo(sku, price, isActive);

    public Task<ProductInfo?> GetProductAsync(string sku, CancellationToken cancellationToken) =>
        Task.FromResult(_products.TryGetValue(sku, out var product) ? product : null);
}
