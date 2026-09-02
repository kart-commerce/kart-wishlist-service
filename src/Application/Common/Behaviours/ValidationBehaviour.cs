using FluentValidation;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Kart.Wishlist.Application.Common.Behaviours;

/// <summary>
/// Runs every registered FluentValidation validator for the incoming request before its handler
/// executes, aggregating all failures into a single <see cref="ValidationException"/> — handled
/// once, platform-wide, by <c>Kart.Shared.ErrorHandling</c>'s <c>KartExceptionHandler</c> (400,
/// grouped by property).
/// </summary>
public sealed class ValidationBehaviour<TRequest, TResponse>(
    IEnumerable<IValidator<TRequest>> validators,
    ILogger<ValidationBehaviour<TRequest, TResponse>> logger)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        if (!validators.Any())
        {
            return await next();
        }

        var failures = (await Task.WhenAll(validators.Select(v => v.ValidateAsync(request, cancellationToken))))
            .SelectMany(result => result.Errors)
            .Where(failure => failure is not null)
            .ToList();

        if (failures.Count != 0)
        {
            var requestName = typeof(TRequest).Name;

            logger.LogWarning(
                "Stage {Stage}: {RequestName} rejected — {Errors}",
                $"{requestName}ValidationFailed",
                requestName,
                string.Join("; ", failures.Select(f => $"{f.PropertyName}: {f.ErrorMessage}")));

            throw new ValidationException(failures);
        }

        return await next();
    }
}
