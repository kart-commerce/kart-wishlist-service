namespace Kart.Wishlist.Application.Common.Models;

/// <summary>The subset of Product Service's own <c>GET /v1/products/{sku}</c> response this
/// service actually needs (ddd-model.md's Anti-Corruption Layer rule — Wishlist never caches
/// Product's catalog facts beyond what its own <c>ReferencePrice</c> evaluation requires).
/// <see cref="IsActive"/> is false for a discontinued/unavailable product — used both by
/// <c>AddWishlistEntryCommandHandler</c>'s add-time validation and the hourly reconciliation job.</summary>
public sealed record ProductInfo(string Sku, decimal Price, bool IsActive);
