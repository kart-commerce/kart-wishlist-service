using System.Reflection;
using FluentValidation;
using Kart.Wishlist.Application.Common.Behaviours;
using Microsoft.Extensions.DependencyInjection;

namespace Kart.Wishlist.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        var assembly = Assembly.GetExecutingAssembly();

        services.AddMediatR(cfg =>
        {
            cfg.RegisterServicesFromAssembly(assembly);
            // Registration order is pipeline order (outermost first) — Logging wraps Validation
            // so a rejected/invalid request is still observed, not just a handler's own success path.
            cfg.AddOpenBehavior(typeof(LoggingBehaviour<,>));
            cfg.AddOpenBehavior(typeof(ValidationBehaviour<,>));
        });
        services.AddValidatorsFromAssembly(assembly);

        return services;
    }
}
