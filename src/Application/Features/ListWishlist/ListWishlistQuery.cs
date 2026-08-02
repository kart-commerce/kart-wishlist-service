using Kart.Wishlist.Application.Common.Models;
using Kart.Shared.Domain;
using MediatR;

namespace Kart.Wishlist.Application.Features.ListWishlist;

/// <summary>
/// WL-1. api-contract.yaml <c>GET /wishlist</c>. Read-only, served from the MongoDB read model
/// (database-design.md) — the P95 &lt; 150ms / P99 &lt; 400ms read-path NFR (requirement-spec §3)
/// applies here. Excludes <c>Stale</c> entries from the default view unless
/// <see cref="IncludeStale"/> is set.
/// </summary>
public sealed record ListWishlistQuery(Guid UserId, bool IncludeStale, string? Cursor, int Limit)
    : IRequest<Result<WishlistPageResponse>>;
