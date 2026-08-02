using System.Text;
using Kart.Wishlist.Application.Common.Interfaces;
using Kart.Wishlist.Application.Common.Mapping;
using Kart.Wishlist.Application.Common.Models;
using Kart.Shared.Domain;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Kart.Wishlist.Application.Features.ListWishlist;

/// <summary>
/// Read path: MongoDB read model first -&gt; PostgreSQL fallback on a cold-start/projection-lag
/// miss (database-design.md's Read Model section states this is a whole-document reflection,
/// rebuildable from the write side at any time — the same "read model, then the always-authoritative
/// write side" fallback shape kart-cart-service's own <c>GetCurrentCartQueryHandler</c> uses).
/// Cursor is an opaque base64-encoded skip-offset into the ordered (by sku) entry list — the
/// per-user entry set is bounded to 500 rows (ddd-model.md), so an offset cursor over an
/// in-memory-paged list is simple and sufficiently efficient at this scale, unlike Product's/
/// Search's unbounded catalog which would need a real keyset cursor.
/// </summary>
public sealed class ListWishlistQueryHandler(
    IWishlistReadModelRepository readModel,
    IWishlistDbContext dbContext)
    : IRequestHandler<ListWishlistQuery, Result<WishlistPageResponse>>
{
    public async Task<Result<WishlistPageResponse>> Handle(ListWishlistQuery request, CancellationToken cancellationToken)
    {
        var entries = await readModel.GetByUserIdAsync(request.UserId, cancellationToken);

        // Cold-start/projection-lag fallback: the read side hasn't caught up yet (or this user has
        // simply never had a document projected) - PostgreSQL is always authoritative, so fall
        // back to it directly rather than surfacing a false "empty wishlist."
        entries ??= await dbContext.WishlistEntries
            .Where(e => e.UserId == request.UserId)
            .OrderBy(e => e.Sku)
            .Select(e => new WishlistEntryResponse(e.Sku, e.ReferencePrice, e.Status.ToString().ToLower(), e.AddedAt))
            .ToListAsync(cancellationToken);

        var filtered = (request.IncludeStale ? entries : entries.Where(e => e.Status == "active"))
            .OrderBy(e => e.Sku)
            .ToList();

        var offset = DecodeCursor(request.Cursor);
        var page = filtered.Skip(offset).Take(request.Limit).ToList();
        var nextOffset = offset + page.Count;
        var nextCursor = nextOffset < filtered.Count ? EncodeCursor(nextOffset) : null;

        return Result.Success(new WishlistPageResponse(page, nextCursor));
    }

    private static int DecodeCursor(string? cursor)
    {
        if (string.IsNullOrEmpty(cursor))
        {
            return 0;
        }

        try
        {
            var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(cursor));
            return int.TryParse(decoded, out var offset) && offset >= 0 ? offset : 0;
        }
        catch (FormatException)
        {
            return 0;
        }
    }

    private static string EncodeCursor(int offset) => Convert.ToBase64String(Encoding.UTF8.GetBytes(offset.ToString()));
}
