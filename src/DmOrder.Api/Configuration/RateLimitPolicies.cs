using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

namespace DmOrder.Api.Endpoints;

/// <summary>
/// In-memory rate limiting. Correct for the single API instance V1 deploys; see
/// docs/ADR/0005-in-memory-rate-limiting.md for the horizontal-scaling cliff this has.
/// </summary>
public static class RateLimitPolicies
{
    /// <summary>Login, register and refresh — the credential-stuffing surface.</summary>
    public const string Auth = "auth";

    /// <summary>Anonymous order submission from a storefront. Applied in Phase 5.</summary>
    public const string PublicWrite = "public-write";
}

public static class RateLimitingSetup
{
    public static IServiceCollection AddApiRateLimiting(this IServiceCollection services)
    {
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            options.AddPolicy(RateLimitPolicies.Auth, context =>
                RateLimitPartition.GetFixedWindowLimiter(
                    ClientKey(context),
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 10,
                        Window = TimeSpan.FromMinutes(1),
                        QueueLimit = 0,
                    }));

            options.AddPolicy(RateLimitPolicies.PublicWrite, context =>
                RateLimitPartition.GetFixedWindowLimiter(
                    ClientKey(context),
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 20,
                        Window = TimeSpan.FromMinutes(5),
                        QueueLimit = 0,
                    }));
        });

        return services;
    }

    /// <summary>
    /// Partition by remote IP. Behind a proxy this needs ForwardedHeaders configured, otherwise every
    /// request looks like it came from the proxy and the limit becomes global — noted in the ADR.
    /// </summary>
    private static string ClientKey(HttpContext context) =>
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
}
