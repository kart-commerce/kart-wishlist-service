using Kart.Wishlist.Application.Common.Interfaces;

namespace Kart.Wishlist.Api;

/// <summary>
/// Resolves the ambient "who is acting" principal (database-design.md's Row-Level Security
/// session-scoped setting) from the current request: the JWT <c>sub</c> claim for a logged-in
/// user, or a well-known <c>system:*</c> id when no HTTP request is in flight at all (every
/// background hosted service — the Outbox relay, the read-model projector, the event consumers,
/// the digest-flush/reconciliation jobs — runs in its own DI scope with no <see cref="HttpContext"/>,
/// kart-cart-service's <c>HttpContextCurrentPrincipalAccessor</c> precedent).
/// </summary>
public sealed class HttpContextCurrentPrincipalAccessor(IHttpContextAccessor httpContextAccessor) : ICurrentPrincipalAccessor
{
    public string PrincipalId => Resolve().Id;

    public string PrincipalKind => Resolve().Kind;

    private (string Id, string Kind) Resolve()
    {
        var httpContext = httpContextAccessor.HttpContext;
        if (httpContext is null)
        {
            return ("system:wishlist-background-worker", "system");
        }

        var userId = httpContext.User.Identity?.IsAuthenticated == true
            ? httpContext.User.FindFirst("sub")?.Value
            : null;

        return userId is not null ? (userId, "user") : ("system:wishlist-anonymous", "system");
    }
}
