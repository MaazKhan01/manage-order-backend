using DmOrder.Application.Common.Exceptions;
using DmOrder.Application.Features.Billing;
using DmOrder.Domain.Exceptions;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace DmOrder.Api.Common;

/// <summary>
/// Single place where an exception becomes an HTTP response, so every error the API returns has the
/// same RFC 7807 shape. Unexpected exceptions are logged in full and reported to the client as a bare
/// 500 — no stack traces, no database messages, no internal detail, in any environment.
/// </summary>
public sealed class GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
    /// <summary>Nginx's "client closed request"; ASP.NET Core does not define a constant for it.</summary>
    private const int ClientClosedRequest = 499;


    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        var problem = Map(exception, httpContext);

        if (problem.Status >= StatusCodes.Status500InternalServerError)
        {
            logger.LogError(exception, "Unhandled exception while processing {Method} {Path}",
                httpContext.Request.Method, httpContext.Request.Path);
        }
        else
        {
            logger.LogInformation("Request failed with {StatusCode}: {Detail}", problem.Status, problem.Detail);
        }

        problem.Extensions["traceId"] = httpContext.TraceIdentifier;

        httpContext.Response.StatusCode = problem.Status ?? StatusCodes.Status500InternalServerError;

        // Serialised against the runtime type, not the static one. Passing a ValidationProblemDetails
        // as ProblemDetails silently drops the `errors` map, and the client gets a 400 with nothing to
        // attach to a field.
        await httpContext.Response.WriteAsJsonAsync(
            problem,
            problem.GetType(),
            options: null,
            contentType: "application/problem+json",
            cancellationToken);

        return true;
    }

    private static ProblemDetails Map(Exception exception, HttpContext httpContext) => exception switch
    {
        RequestValidationException validation => new ValidationProblemDetails(validation.Errors)
        {
            Status = StatusCodes.Status400BadRequest,
            Title = "One or more validation errors occurred.",
            Instance = httpContext.Request.Path,
        },

        NotFoundException notFound => Problem(
            StatusCodes.Status404NotFound, "Resource not found.", notFound.Message, httpContext),

        ConflictException conflict => Problem(
            StatusCodes.Status409Conflict, "Conflict.", conflict.Message, httpContext),

        BusinessRuleException rule => Problem(
            StatusCodes.Status422UnprocessableEntity, "The request could not be processed.", rule.Message, httpContext),

        ForbiddenException forbidden => Problem(
            StatusCodes.Status403Forbidden, "Forbidden.", forbidden.Message, httpContext),

        // 402 rather than 403: "you need a subscription" and "you are not allowed" are different
        // answers with different remedies, and the dashboard has to tell them apart to show the
        // upgrade screen instead of an error. The reason code is what it branches on - never the
        // prose, which is written for people.
        PaymentRequiredException payment => WithReason(
            Problem(
                StatusCodes.Status402PaymentRequired,
                "A subscription is required.",
                payment.Message,
                httpContext),
            payment.Reason),

        UnauthorizedAccessException => Problem(
            StatusCodes.Status401Unauthorized, "Unauthorized.", "Authentication is required.", httpContext),

        // A body the model binder cannot read is the client's mistake, not ours. Left unhandled it
        // surfaced as a 500 with an alarming "Unhandled exception" log line for what is really a 400.
        // The detail is deliberately generic — parser messages quote the payload back.
        BadHttpRequestException => Problem(
            StatusCodes.Status400BadRequest,
            "Malformed request.",
            "The request body could not be read. Check the field types.",
            httpContext),

        OperationCanceledException => Problem(
            ClientClosedRequest, "Request cancelled.", "The request was cancelled.", httpContext),

        // Deliberately generic: the real cause is in the logs, keyed by traceId.
        _ => Problem(
            StatusCodes.Status500InternalServerError,
            "An unexpected error occurred.",
            "The request could not be completed. Quote the traceId when reporting this.",
            httpContext),
    };

    /// <summary>Attaches the machine-readable reason a client branches on.</summary>
    private static ProblemDetails WithReason(ProblemDetails problem, string reason)
    {
        problem.Extensions["reason"] = reason;
        return problem;
    }

    private static ProblemDetails Problem(int status, string title, string detail, HttpContext httpContext) =>
        new()
        {
            Status = status,
            Title = title,
            Detail = detail,
            Instance = httpContext.Request.Path,
        };
}
