using FluentValidation;
using MediatR;

namespace Kart.Wishlist.Application.Common.Behaviours;

/// <summary>
/// Runs every registered FluentValidation validator for the incoming request before its handler
/// executes, aggregating all failures into a single <see cref="ValidationException"/> — handled
/// once, platform-wide, by <c>Kart.Shared.ErrorHandling</c>'s <c>KartExceptionHandler</c> (400,
/// grouped by property).
/// </summary>
public sealed class ValidationBehaviour<TRequest, TResponse>(IEnumerable<IValidator<TRequest>> validators)
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
            throw new ValidationException(failures);
        }

        return await next();
    }
}
