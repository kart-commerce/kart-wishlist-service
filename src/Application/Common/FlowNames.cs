namespace Kart.Wishlist.Application.Common;

/// <summary>
/// Business-flow tags for KartFlowContext.Push, per kart-conventions.md's per-flow tracing/logging
/// standard (checkpoint-logging-standard.md). This service's participation is business-flows.md
/// flow #13, "Wishlist & Saved Items". kart-notification-service's own FlowNames.cs already
/// committed to this exact literal for its own consumer of this service's
/// WishlistPriceAlertTriggered event (no upstream precedent existed at that time) — reused here
/// verbatim rather than renamed, so both sides of that event's causal chain share one Flow tag.
/// </summary>
public static class FlowNames
{
    public const string WishlistSavedItems = "WishlistSavedItems";
}
