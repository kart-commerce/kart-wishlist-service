namespace Kart.Wishlist.Application.Common.Interfaces;

/// <summary>
/// The ambient "who is acting" accessor — the single source BRD §24.1.4's row-level-security
/// session variable (<c>app.current_principal</c>) reads from (database-design.md's Row-Level
/// Security Policy section; kart-cart-service/kart-user-service precedent). Implemented in the Api
/// layer for interactive requests (resolves the JWT <c>sub</c> claim); a well-known
/// <c>system:*</c> value is used wherever a handler runs with no HTTP request at all (event
/// consumers, the Outbox/projection pollers, the digest-flush/reconciliation background jobs) —
/// the same single implementation branches on <see cref="System.Net.Http.HttpContext"/> being
/// null, matching kart-cart-service's <c>HttpContextCurrentPrincipalAccessor</c>.
/// </summary>
public interface ICurrentPrincipalAccessor
{
    /// <summary>The caller's own <c>userId</c> for a self-service request, or a well-known
    /// <c>system:*</c> id.</summary>
    string PrincipalId { get; }

    /// <summary><c>"user"</c> or <c>"system"</c> — read by the RLS policy alongside
    /// <see cref="PrincipalId"/> (database-design.md's Row-Level Security Policy).</summary>
    string PrincipalKind { get; }
}
