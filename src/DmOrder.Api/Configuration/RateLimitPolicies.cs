using System.ComponentModel.DataAnnotations;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

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

public sealed class RateLimitingOptions
{
    public const string SectionName = "RateLimiting";

    [Range(1, 10_000)]
    public int AuthPermitLimit { get; set; } = 10;

    [Range(1, 3600)]
    public int AuthWindowSeconds { get; set; } = 60;

    [Range(1, 10_000)]
    public int PublicWritePermitLimit { get; set; } = 20;

    [Range(1, 3600)]
    public int PublicWriteWindowSeconds { get; set; } = 300;
}

public static class RateLimitingSetup
{
    public static IServiceCollection AddApiRateLimiting(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOptions<RateLimitingOptions>()
            .Bind(configuration.GetSection(RateLimitingOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            options.AddPolicy(RateLimitPolicies.Auth, context =>
            {
                var limits = context.RequestServices
                    .GetRequiredService<IOptions<RateLimitingOptions>>().Value;

                return RateLimitPartition.GetFixedWindowLimiter(
                    ClientKey(context),
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = limits.AuthPermitLimit,
                        Window = TimeSpan.FromSeconds(limits.AuthWindowSeconds),
                        QueueLimit = 0,
                    });
            });

            options.AddPolicy(RateLimitPolicies.PublicWrite, context =>
            {
                var limits = context.RequestServices
                    .GetRequiredService<IOptions<RateLimitingOptions>>().Value;

                return RateLimitPartition.GetFixedWindowLimiter(
                    ClientKey(context),
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = limits.PublicWritePermitLimit,
                        Window = TimeSpan.FromSeconds(limits.PublicWriteWindowSeconds),
                        QueueLimit = 0,
                    });
            });
        });

        return services;
    }

    /// <summary>
    /// Partition by client IP.
    ///
    /// Behind a reverse proxy this is the proxy's address unless forwarded headers are honoured, which
    /// would collapse every visitor into one bucket and turn a per-client limit into a global one. That
    /// is why <c>TRUST_FORWARDED_HEADERS</c> exists — see Program.cs.
    /// </summary>
    private static string ClientKey(HttpContext context) =>
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
}
