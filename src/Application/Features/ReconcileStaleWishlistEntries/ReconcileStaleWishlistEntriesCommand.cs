using MediatR;

namespace Kart.Wishlist.Application.Features.ReconcileStaleWishlistEntries;

/// <summary>
/// WL-7. Triggered hourly by the reconciliation scheduler (architecture.md's Sync vs. Async
/// Resolution). Defense-in-depth backstop alongside WL-6's event-driven path — for every distinct
/// SKU still held by an active wishlist entry, checks Product Service and marks stale any entry
/// whose product no longer exists or is no longer active. Never on the client-facing request path
/// (architecture.md's Distributed-Monolith Risk section).
/// </summary>
public sealed record ReconcileStaleWishlistEntriesCommand : IRequest<int>;
