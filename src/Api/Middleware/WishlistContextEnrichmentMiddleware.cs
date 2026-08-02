using Serilog.Context;

namespace Kart.Wishlist.Api.Middleware;

/// <summary>
/// Pushes this service's own primary correlation field — the caller's <c>userId</c> — onto
/// Serilog's <see cref="LogContext"/> for every authenticated request (requirement-spec §3's
/// Observability row: "structured logs/traces/metrics exemplars carry userId alongside the
/// mandatory traceId/service/level fields, since WishlistEntry is keyed on the (userId, sku) pair
/// rather than a single wishlist-level id"), alongside the mandatory fields
/// <c>Kart.Shared.Observability</c> already enriches every log line with.
/// </summary>
public sealed class WishlistContextEnrichmentMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var userId = context.User.Identity?.IsAuthenticated == true
            ? context.User.FindFirst("sub")?.Value
            : null;

        if (userId is null)
        {
            await next(context);
            return;
        }

        using (LogContext.PushProperty("userId", userId))
        {
            await next(context);
        }
    }
}
