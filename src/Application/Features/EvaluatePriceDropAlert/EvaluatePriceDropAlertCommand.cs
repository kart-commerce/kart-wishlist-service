using MediatR;

namespace Kart.Wishlist.Application.Features.EvaluatePriceDropAlert;

/// <summary>
/// WL-4. Triggered by the <c>ProductPriceChanged</c> consumer (event-contract.md). Evaluates the
/// 5%-threshold/24h-cooldown invariants (ddd-model.md) for every active <c>WishlistEntry</c>
/// holding <paramref name="Sku"/>, and — for every entry that qualifies and is not a duplicate
/// redelivery (the <c>wishlist_alert_dedup</c> table) — queues a pending trigger into that user's
/// Redis digest accumulator (design-decisions.md's batching decision) for WL-5 to flush.
/// </summary>
public sealed record EvaluatePriceDropAlertCommand(string Sku, decimal OldPrice, decimal NewPrice, DateTimeOffset OccurredAt) : IRequest;
