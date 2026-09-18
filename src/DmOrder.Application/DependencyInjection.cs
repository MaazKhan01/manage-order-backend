using System.Reflection;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;

namespace DmOrder.Application;

public static class DependencyInjection
{
    /// <summary>
    /// Registers use-case handlers and validators. Handlers are plain classes resolved by DI —
    /// there is no mediator, no pipeline behaviours and no reflection-based dispatch.
    /// See docs/ADR/0001-no-mediator.md.
    /// </summary>
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        var assembly = Assembly.GetExecutingAssembly();

        services.AddValidatorsFromAssembly(assembly, includeInternalTypes: true);

        // Use-case handlers follow the *Handler naming convention and are registered as themselves.
        var handlerTypes = assembly.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false } && t.Name.EndsWith("Handler", StringComparison.Ordinal));

        foreach (var handlerType in handlerTypes)
        {
            services.AddScoped(handlerType);
        }

        return services;
    }
}
