using System.Collections.Concurrent;
using Kart.Wishlist.Application.Common.Interfaces;
using Kart.Wishlist.Application.Common.Models;

namespace Kart.Wishlist.IntegrationTests.TestSupport;

/// <summary>Test double for <see cref="IWishlistReadModelRepository"/> — no real MongoDB instance
/// in this test environment; the HTTP-pipeline tests exercise the write side and the
/// Postgres-fallback read path directly (kart-cart-service's <c>InMemoryCartReadModelRepository</c>
/// precedent).</summary>
public sealed class InMemoryWishlistReadModelRepository : IWishlistReadModelRepository
{
    private readonly ConcurrentDictionary<Guid, IReadOnlyList<WishlistEntryResponse>> _documents = new();

    public Task<IReadOnlyList<WishlistEntryResponse>?> GetByUserIdAsync(Guid userId, CancellationToken cancellationToken) =>
        Task.FromResult(_documents.TryGetValue(userId, out var entries) ? entries : null);

    public Task UpsertUserDocumentAsync(Guid userId, IReadOnlyList<WishlistEntryResponse> entries, DateTimeOffset updatedAt, CancellationToken cancellationToken)
    {
        _documents[userId] = entries;
        return Task.CompletedTask;
    }

    public Task DeleteByUserIdAsync(Guid userId, CancellationToken cancellationToken)
    {
        _documents.TryRemove(userId, out _);
        return Task.CompletedTask;
    }
}
