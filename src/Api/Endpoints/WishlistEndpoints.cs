using Kart.Wishlist.Application.Common.Models;
using Kart.Wishlist.Application.Features.AddWishlistEntry;
using Kart.Wishlist.Application.Features.ListWishlist;
using Kart.Wishlist.Application.Features.RemoveWishlistEntry;
using Kart.Shared.Domain;
using Kart.Shared.ErrorHandling;
using MediatR;

namespace Kart.Wishlist.Api.Endpoints;

/// <summary>
/// api-contract.yaml's client-facing surface: <c>/v1/wishlist</c>. Every endpoint requires a
/// Customer-scoped bearer JWT and is implicitly scoped to the caller's own <c>sub</c> claim —
/// never a userId path/query parameter (there is no "view another user's wishlist" operation in
/// this contract). Auth is resolved manually (not <c>.RequireAuthorization()</c>) so a missing/
/// invalid token returns this API's own consistent <c>Problem</c>-shaped 401 rather than ASP.NET
/// Core's bare default challenge response (kart-cart-service's <c>CartEndpoints</c> precedent —
/// "every error response, auth included, uses the same envelope").
/// </summary>
public static class WishlistEndpoints
{
    public static IEndpointRouteBuilder MapWishlistEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/v1/wishlist");

        group.MapGet("/", ListWishlist)
            .WithName("listWishlist")
            .Produces<WishlistPageResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        group.MapPost("/", AddWishlistEntry)
            .WithName("addWishlistEntry")
            .Produces<WishlistEntryResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapDelete("/{sku}", RemoveWishlistEntry)
            .WithName("removeWishlistEntry")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        return app;
    }

    private static async Task<IResult> ListWishlist(
        HttpContext httpContext,
        ISender sender,
        bool? includeStale,
        string? cursor,
        int? limit,
        CancellationToken cancellationToken)
    {
        if (!TryResolveUserId(httpContext, out var userId))
        {
            return Unauthorized(httpContext);
        }

        var clampedLimit = Math.Clamp(limit ?? 50, 1, 100);
        var result = await sender.Send(new ListWishlistQuery(userId, includeStale ?? false, cursor, clampedLimit), cancellationToken);
        return Results.Ok(result.Value);
    }

    private static async Task<IResult> AddWishlistEntry(
        HttpContext httpContext, AddWishlistEntryRequest request, ISender sender, CancellationToken cancellationToken)
    {
        if (!TryResolveUserId(httpContext, out var userId))
        {
            return Unauthorized(httpContext);
        }

        var command = new AddWishlistEntryCommand(userId, request.Sku, userId.ToString());
        var result = await sender.Send(command, cancellationToken);
        return result.IsSuccess ? Results.Created($"/v1/wishlist/{request.Sku}", result.Value) : Problem(httpContext, result.Error);
    }

    private static async Task<IResult> RemoveWishlistEntry(string sku, HttpContext httpContext, ISender sender, CancellationToken cancellationToken)
    {
        if (!TryResolveUserId(httpContext, out var userId))
        {
            return Unauthorized(httpContext);
        }

        var command = new RemoveWishlistEntryCommand(userId, sku, userId.ToString());
        await sender.Send(command, cancellationToken);
        return Results.NoContent();
    }

    private static bool TryResolveUserId(HttpContext httpContext, out Guid userId)
    {
        userId = Guid.Empty;

        if (httpContext.User.Identity?.IsAuthenticated != true)
        {
            return false;
        }

        var sub = httpContext.User.FindFirst("sub")?.Value;
        return sub is not null && Guid.TryParse(sub, out userId);
    }

    private static IResult Unauthorized(HttpContext httpContext) =>
        AsProblem(httpContext, StatusCodes.Status401Unauthorized, "unauthorized", "Missing or invalid bearer token.");

    private static IResult Problem(HttpContext httpContext, Error error) =>
        AsProblem(httpContext, StatusCodeFor(error.Code), error.Code, error.Message);

    private static int StatusCodeFor(string errorCode) => errorCode switch
    {
        "sku_not_found" or "validation_error" => StatusCodes.Status400BadRequest,
        "unauthorized" => StatusCodes.Status401Unauthorized,
        "sku_already_wishlisted" or "wishlist_size_limit_exceeded" or "conflict" => StatusCodes.Status409Conflict,
        _ => StatusCodes.Status500InternalServerError,
    };

    private static IResult AsProblem(HttpContext httpContext, int statusCode, string errorCode, string detail)
    {
        var problem = KartProblemDetailsFactory.Create(httpContext, statusCode, errorCode, detail);
        return Results.Json(problem, statusCode: statusCode, contentType: "application/problem+json");
    }

    private sealed record AddWishlistEntryRequest(string Sku);
}
