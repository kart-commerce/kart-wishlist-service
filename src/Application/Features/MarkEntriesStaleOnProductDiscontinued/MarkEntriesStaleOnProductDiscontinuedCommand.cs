using MediatR;

namespace Kart.Wishlist.Application.Features.MarkEntriesStaleOnProductDiscontinued;

/// <summary>
/// WL-6. Triggered by the <c>ProductDiscontinued</c> consumer (event-contract.md) — the
/// event-driven stale-entry invalidation path, alongside (not instead of) WL-7's hourly
/// reconciliation job (requirement-spec §2, §4, §6 item 7).
/// </summary>
public sealed record MarkEntriesStaleOnProductDiscontinuedCommand(string Sku, DateTimeOffset DiscontinuedAt) : IRequest;
