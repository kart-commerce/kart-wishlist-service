using MediatR;

namespace Kart.Wishlist.Application.Features.FlushAlertDigest;

/// <summary>
/// WL-5. Triggered by the scheduled digest-flush sweep (design-decisions.md's Redis-backed
/// batching decision: 15-minute rolling quiet window, 60-minute hard cap). Re-checks each queued
/// item's current price immediately before publishing (edge-cases.md's "Price Rebound During the
/// Batching/Digest Window" decision — fail-safe: suppress an item whose re-check fails rather than
/// send an unverified price) and writes one <c>WishlistPriceAlertTriggered</c> Outbox row per
/// surviving (user, sku) pair.
/// </summary>
public sealed record FlushAlertDigestCommand(Guid UserId) : IRequest;
