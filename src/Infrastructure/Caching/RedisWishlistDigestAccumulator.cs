using System.Text.Json;
using Kart.Wishlist.Application.Common.Interfaces;
using StackExchange.Redis;

namespace Kart.Wishlist.Infrastructure.Caching;

/// <summary>
/// design-decisions.md's "State-Store Mechanism for the Per-User Alert Batching/Digest Window"
/// decision — a Redis-backed per-user accumulator, TTL/marker keys matched to the 60-minute hard
/// cap, flushed by a scheduled sweep independent of the triggering consumer's own lifecycle.
/// Not a CQRS read model — a publish-cadence buffer (database-design.md's Ephemeral State
/// section). Every key is namespaced <c>wishlist:digest:{userId}:*</c>.
/// </summary>
public sealed class RedisWishlistDigestAccumulator(IConnectionMultiplexer connectionMultiplexer) : IWishlistDigestAccumulator
{
    private const string PendingUsersSetKey = "wishlist:digest:pending-users";
    private static readonly TimeSpan QuietWindow = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan HardCapWindow = TimeSpan.FromMinutes(60);
    private static readonly TimeSpan FlushLockTtl = TimeSpan.FromMinutes(5);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task EnqueueAsync(Guid userId, string sku, decimal oldPrice, decimal newPrice, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var database = connectionMultiplexer.GetDatabase();
        var json = JsonSerializer.Serialize(new PendingDigestItemDto(oldPrice, newPrice), JsonOptions);

        // A repeat trigger for the same sku before flush overwrites the prior entry rather than
        // appending a second one (IWishlistDigestAccumulator's own remarks).
        await database.HashSetAsync(ItemsKey(userId), sku, json);

        // Opens the 60-minute hard-cap window on first enqueue only (NX) — a later enqueue for
        // the same user must not push the hard-cap deadline out further.
        await database.StringSetAsync(OpenedAtKey(userId), now.ToUnixTimeSeconds(), when: When.NotExists);

        // Resets the 15-minute rolling quiet window on every call.
        await database.StringSetAsync(QuietKey(userId), 1, QuietWindow);

        await database.SetAddAsync(PendingUsersSetKey, userId.ToString());
    }

    public async Task<IReadOnlyList<Guid>> GetPendingUserIdsAsync(CancellationToken cancellationToken)
    {
        var database = connectionMultiplexer.GetDatabase();
        var members = await database.SetMembersAsync(PendingUsersSetKey);
        return members
            .Select(m => Guid.TryParse(m.ToString(), out var id) ? id : (Guid?)null)
            .Where(id => id is not null)
            .Select(id => id!.Value)
            .ToList();
    }

    public async Task<bool> ShouldFlushAsync(Guid userId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var database = connectionMultiplexer.GetDatabase();

        // The 15-minute quiet key expiring naturally (no longer present) means no new qualifying
        // trigger has arrived for 15 minutes — flush now.
        var quietStillActive = await database.KeyExistsAsync(QuietKey(userId));
        if (!quietStillActive)
        {
            return true;
        }

        var openedAtRaw = await database.StringGetAsync(OpenedAtKey(userId));
        if (openedAtRaw.IsNullOrEmpty)
        {
            return false;
        }

        var openedAt = DateTimeOffset.FromUnixTimeSeconds((long)openedAtRaw);
        return now - openedAt >= HardCapWindow;
    }

    public async Task<bool> TryAcquireFlushLockAsync(Guid userId, CancellationToken cancellationToken)
    {
        var database = connectionMultiplexer.GetDatabase();
        return await database.StringSetAsync(FlushLockKey(userId), 1, FlushLockTtl, when: When.NotExists);
    }

    public async Task ReleaseFlushLockAsync(Guid userId, CancellationToken cancellationToken)
    {
        var database = connectionMultiplexer.GetDatabase();
        await database.KeyDeleteAsync(FlushLockKey(userId));
    }

    public async Task<IReadOnlyList<PendingDigestItem>> DequeueAllAsync(Guid userId, CancellationToken cancellationToken)
    {
        var database = connectionMultiplexer.GetDatabase();
        var hashEntries = await database.HashGetAllAsync(ItemsKey(userId));

        var items = hashEntries
            .Select(entry =>
            {
                var dto = JsonSerializer.Deserialize<PendingDigestItemDto>(entry.Value!, JsonOptions)!;
                return new PendingDigestItem(entry.Name!, dto.OldPrice, dto.NewPrice);
            })
            .ToList();

        await database.KeyDeleteAsync(ItemsKey(userId));
        await database.KeyDeleteAsync(OpenedAtKey(userId));
        await database.KeyDeleteAsync(QuietKey(userId));
        await database.SetRemoveAsync(PendingUsersSetKey, userId.ToString());

        return items;
    }

    public async Task RemoveUserAsync(Guid userId, CancellationToken cancellationToken)
    {
        var database = connectionMultiplexer.GetDatabase();
        await database.KeyDeleteAsync(ItemsKey(userId));
        await database.KeyDeleteAsync(OpenedAtKey(userId));
        await database.KeyDeleteAsync(QuietKey(userId));
        await database.KeyDeleteAsync(FlushLockKey(userId));
        await database.SetRemoveAsync(PendingUsersSetKey, userId.ToString());
    }

    private static string ItemsKey(Guid userId) => $"wishlist:digest:{userId}:items";

    private static string OpenedAtKey(Guid userId) => $"wishlist:digest:{userId}:opened-at";

    private static string QuietKey(Guid userId) => $"wishlist:digest:{userId}:quiet";

    private static string FlushLockKey(Guid userId) => $"wishlist:digest:{userId}:flushing";

    private sealed record PendingDigestItemDto(decimal OldPrice, decimal NewPrice);
}
