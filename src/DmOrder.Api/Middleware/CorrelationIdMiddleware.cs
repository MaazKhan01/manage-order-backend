using Serilog.Context;

namespace DmOrder.Api.Middleware;

/// <summary>
/// Gives every request a correlation id, echoes it back on the response and pushes it into the log
/// context so that all logs for one request can be found from a single id reported by a user.
/// </summary>
public sealed class CorrelationIdMiddleware(RequestDelegate next)
{
    public const string HeaderName = "X-Correlation-Id";

    public async Task InvokeAsync(HttpContext context)
    {
        var correlationId = context.Request.Headers.TryGetValue(HeaderName, out var incoming)
            ? Sanitise(incoming.ToString()) ?? context.TraceIdentifier
            : context.TraceIdentifier;

        context.Response.Headers[HeaderName] = correlationId;

        using (LogContext.PushProperty("CorrelationId", correlationId))
        {
            await next(context);
        }
    }

    /// <summary>
    /// The inbound value is attacker-controlled and ends up in both a response header and the logs,
    /// so it is capped and restricted to characters that cannot forge a log line or a header.
    /// </summary>
    private static string? Sanitise(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 128)
        {
            return null;
        }

        return value.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.' or ':')
            ? value
            : null;
    }
}
