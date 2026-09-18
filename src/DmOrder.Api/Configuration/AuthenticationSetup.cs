using System.Text;
using DmOrder.Domain.Identity;
using DmOrder.Api.Endpoints;
using DmOrder.Infrastructure.Identity;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;

namespace DmOrder.Api.Configuration;

public static class AuthenticationSetup
{
    public static IServiceCollection AddApiAuthentication(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var jwt = configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>()
            ?? throw new InvalidOperationException("Jwt configuration is missing. Set JWT_SECRET.");

        if (string.IsNullOrWhiteSpace(jwt.Secret) || jwt.Secret.Length < 32)
        {
            throw new InvalidOperationException(
                "JWT_SECRET must be set and at least 32 characters. Generate one per environment.");
        }

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = jwt.Issuer,
                    ValidateAudience = true,
                    ValidAudience = jwt.Audience,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.Secret)),
                    ValidateLifetime = true,

                    // The default is five minutes, which silently extends every token's life. Tokens are
                    // short by design, so the tolerance should be small too.
                    ClockSkew = TimeSpan.FromSeconds(30),
                };

                // Tokens arrive as bearer headers from the Next.js BFF, never from browser storage.
                // Inbound claim mapping is off so the claims read here are exactly the ones issued.
                options.MapInboundClaims = false;
                options.TokenValidationParameters.NameClaimType = AppClaimTypes.UserId;
                options.TokenValidationParameters.RoleClaimType = AppClaimTypes.Role;
            });

        services.AddAuthorizationBuilder()
            .AddPolicy(AuthorizationPolicies.Seller, policy => policy.RequireRole(ApplicationRoles.Seller))
            .AddPolicy(AuthorizationPolicies.Admin, policy => policy.RequireRole(ApplicationRoles.Admin));

        return services;
    }
}
