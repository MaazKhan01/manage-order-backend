using DmOrder.Application.Common.Exceptions;
using FluentValidation;

namespace DmOrder.Api.Common;

/// <summary>
/// Runs the FluentValidation validator for a request DTO before the handler sees it.
///
/// This is the whole "validation pipeline" — no mediator behaviours needed. Endpoints opt in with
/// <c>.WithValidation&lt;TRequest&gt;()</c>, which keeps it visible at the route rather than hidden in
/// global configuration.
/// </summary>
public sealed class ValidationFilter<TRequest>(IValidator<TRequest> validator) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(
        EndpointFilterInvocationContext context,
        EndpointFilterDelegate next)
    {
        var request = context.Arguments.OfType<TRequest>().FirstOrDefault();

        if (request is null)
        {
            // The endpoint asked for validation of a type it does not actually accept. That is a wiring
            // mistake, and failing loudly beats silently skipping validation.
            throw new InvalidOperationException(
                $"Endpoint declares validation for {typeof(TRequest).Name} but no such argument was bound.");
        }

        var result = await validator.ValidateAsync(request, context.HttpContext.RequestAborted);

        if (!result.IsValid)
        {
            throw new RequestValidationException(
                result.Errors
                    .GroupBy(e => ToCamelCase(e.PropertyName))
                    .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).ToArray()));
        }

        return await next(context);
    }

    /// <summary>Property names are camel-cased to match the JSON the client sent, so the frontend can
    /// map each message straight onto its input.</summary>
    private static string ToCamelCase(string propertyName) =>
        string.IsNullOrEmpty(propertyName) || char.IsLower(propertyName[0])
            ? propertyName
            : char.ToLowerInvariant(propertyName[0]) + propertyName[1..];
}

public static class ValidationFilterExtensions
{
    public static RouteHandlerBuilder WithValidation<TRequest>(this RouteHandlerBuilder builder) =>
        builder.AddEndpointFilter<ValidationFilter<TRequest>>()
            .ProducesValidationProblem();
}
