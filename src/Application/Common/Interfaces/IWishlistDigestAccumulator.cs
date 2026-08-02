namespace Kart.Wishlist.Application.Common.Interfaces;

/// <summary>One pending, not-yet-sent price-drop trigger inside a user's digest window
/// (design-decisions.md's "State-Store Mechanism for the Per-User Alert Batching/Digest Window"
/// decision). A repeat trigger for the same <c>sku</c> before the window flushes overwrites the
/// prior entry rather than appending a second one — the digest reports the latest qualifying
/// state per sku, not a full history of every intermediate qualifying drop.</summary>
public sealed record PendingDigestItem(string Sku, decimal OldPrice, decimal NewPrice);

/// <summary>
/// The Redis-backed per-user alert-batching/digest accumulator (design-decisions.md; edge-cases.md's
/// "Alert Storm on Sitewide Price Drop" decision — 15-minute rolling quiet window, 60-minute hard
/// cap). Not a CQRS read model — a publish-cadence buffer (database-design.md's Ephemeral State
/// section). Application owns the interface; Infrastructure implements it against
/// StackExchange.Redis.
/// </summary>
public interface IWishlistDigestAccumulator
{
    /// <summary>Records a qualifying trigger for <paramref name="userId"/>/<paramref name="sku"/>,
    /// opens the 60-minute hard-cap window on first enqueue (idempotent — a later enqueue for the
    /// same user does not push the hard-cap deadline out further), and resets the 15-minute quiet
    /// window on every call.</summary>
    Task EnqueueAsync(Guid userId, string sku, decimal oldPrice, decimal newPrice, DateTimeOffset now, CancellationToken cancellationToken);

    /// <summary>Every <c>userId</c> with a currently-open (non-empty) digest accumulator —
    /// candidates the scheduled flush sweep evaluates each tick.</summary>
    Task<IReadOnlyList<Guid>> GetPendingUserIdsAsync(CancellationToken cancellationToken);

    /// <summary>True if this user's window should flush now: the 15-minute quiet period has
    /// elapsed with no new arrival, or the 60-minute hard cap has been reached, whichever comes
    /// first (edge-cases.md's Alert Storm decision).</summary>
    Task<bool> ShouldFlushAsync(Guid userId, DateTimeOffset now, CancellationToken cancellationToken);

    /// <summary>
    /// Attempts to acquire a short-lived per-user flush lock so two overlapping sweep ticks (or a
    /// sweep tick racing a slow prior flush) can never process the same user's digest twice —
    /// returns false if another flush for this user is already in flight.
    /// </summary>
    Task<bool> TryAcquireFlushLockAsync(Guid userId, CancellationToken cancellationToken);

    Task ReleaseFlushLockAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>Atomically reads and clears every pending item for this user (the accumulator,
    /// the quiet-window marker, and the hard-cap marker) — called once per flush, inside the
    /// flush lock acquired above.</summary>
    Task<IReadOnlyList<PendingDigestItem>> DequeueAllAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>Deletes every key for <paramref name="userId"/> outright — used by the
    /// <c>UserDataErased</c> handler (ADR-0016), never left to expire on its own TTL
    /// (edge-cases.md's "Residual Wishlist State" decision: "a delayed erasure is a compliance
    /// failure, not a tolerable staleness window").</summary>
    Task RemoveUserAsync(Guid userId, CancellationToken cancellationToken);
}
