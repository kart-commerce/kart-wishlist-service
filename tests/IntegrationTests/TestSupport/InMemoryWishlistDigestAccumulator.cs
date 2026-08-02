using System.Collections.Concurrent;
using Kart.Wishlist.Application.Common.Interfaces;

namespace Kart.Wishlist.IntegrationTests.TestSupport;

/// <summary>Test double for <see cref="IWishlistDigestAccumulator"/> — no real Redis instance in
/// this test environment. The HTTP-pipeline tests exercise add/list/remove only; the
/// consumer/scheduled-job paths that would use this are covered by unit tests instead
/// (<c>Kart.Wishlist.UnitTests</c>).</summary>
public sealed class InMemoryWishlistDigestAccumulator : IWishlistDigestAccumulator
{
    private readonly ConcurrentDictionary<Guid, ConcurrentDictionary<string, (decimal OldPrice, decimal NewPrice)>> _items = new();
    private readonly ConcurrentDictionary<Guid, byte> _flushLocks = new();

    public Task EnqueueAsync(Guid userId, string sku, decimal oldPrice, decimal newPrice, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var bucket = _items.GetOrAdd(userId, _ => new ConcurrentDictionary<string, (decimal, decimal)>());
        bucket[sku] = (oldPrice, newPrice);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<Guid>> GetPendingUserIdsAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<Guid>>(_items.Keys.ToList());

    public Task<bool> ShouldFlushAsync(Guid userId, DateTimeOffset now, CancellationToken cancellationToken) => Task.FromResult(true);

    public Task<bool> TryAcquireFlushLockAsync(Guid userId, CancellationToken cancellationToken) =>
        Task.FromResult(_flushLocks.TryAdd(userId, 1));

    public Task ReleaseFlushLockAsync(Guid userId, CancellationToken cancellationToken)
    {
        _flushLocks.TryRemove(userId, out _);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<Kart.Wishlist.Application.Common.Interfaces.PendingDigestItem>> DequeueAllAsync(Guid userId, CancellationToken cancellationToken)
    {
        if (!_items.TryRemove(userId, out var bucket))
        {
            return Task.FromResult<IReadOnlyList<PendingDigestItem>>([]);
        }

        IReadOnlyList<PendingDigestItem> items = bucket.Select(kv => new PendingDigestItem(kv.Key, kv.Value.OldPrice, kv.Value.NewPrice)).ToList();
        return Task.FromResult(items);
    }

    public Task RemoveUserAsync(Guid userId, CancellationToken cancellationToken)
    {
        _items.TryRemove(userId, out _);
        _flushLocks.TryRemove(userId, out _);
        return Task.CompletedTask;
    }
}
