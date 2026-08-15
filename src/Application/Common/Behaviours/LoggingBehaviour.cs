using System.Diagnostics;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Kart.Wishlist.Application.Common.Behaviours;

/// <summary>
/// Every command/query gets a structured Information log on completion, tagged with its own name
/// and duration — the generic backbone that gives every MediatR request log coverage regardless
/// of whether its handler adds its own business-milestone log (kart-identity-service/kart-cart-service
/// precedent). Deliberately logs only the request's type name, never its field values. Exceptions
/// are left unlogged here and rethrown as-is — logged once, at the true boundary
/// (<c>Kart.Shared.ErrorHandling</c>'s <c>KartExceptionHandler</c>), not duplicated per pipeline layer.
/// </summary>
public sealed class LoggingBehaviour<TRequest, TResponse>(ILogger<LoggingBehaviour<TRequest, TResponse>> logger)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        var requestName = typeof(TRequest).Name;
        var stopwatch = Stopwatch.StartNew();

        // Checkpoint-logging taxonomy stage 3 ("<Command>HandlerStarted", first line inside
        // Handle()) generalized here rather than duplicated in every handler — see
        // checkpoint-logging-standard.md.
        logger.LogInformation(
            "Stage {Stage}: {RequestName} handler started",
            $"{requestName}HandlerStarted",
            requestName);

        var response = await next();

        logger.LogInformation(
            "Stage {Stage}: {RequestName} completed in {ElapsedMilliseconds}ms",
            $"{requestName}Completed",
            requestName,
            stopwatch.ElapsedMilliseconds);

        return response;
    }
}
