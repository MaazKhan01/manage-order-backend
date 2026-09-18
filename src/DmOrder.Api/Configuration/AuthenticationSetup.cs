using System.Text;
using DmOrder.Api.Endpoints;
using DmOrder.Domain.Identity;
using DmOrder.Infrastructure.Identity;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace DmOrder.Api.Configuration;

public static class AuthenticationSetup
{
    public static IServiceCollection AddApiAuthentication(this IServiceCollection services)
    {
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();

        // Configured from IOptions<JwtOptions> rather than by reading IConfiguration here.
        //
        // Reading configuration eagerly at registration time freezes whatever was loaded *so far*, so
        // any source added later is ignored — which silently desynchronises the key used to validate
        // tokens from the key TokenService signs with. Every token then fails validation with a plain
        // 401 and no clue why.
        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<IOptions<JwtOptions>>((bearer, jwtOptions) =>
            {
                var jwt = jwtOptions.Value;

                bearer.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = jwt.Issuer,
                    ValidateAudience = true,
                    ValidAudience = jwt.Audience,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.Secret)),
                    ValidateLifetime = true,

                    // The default is five minutes, which silently extends every token's life. Tokens
                    // are short by design, so the tolerance should be small too.
                    ClockSkew = TimeSpan.FromSeconds(30),

                    // Inbound claim mapping is off so the claims read here are exactly the ones issued.
                    NameClaimType = AppClaimTypes.UserId,
                    RoleClaimType = AppClaimTypes.Role,
                };

                bearer.MapInboundClaims = false;
            });

        services.AddAuthorizationBuilder()
            .AddPolicy(AuthorizationPolicies.Seller, policy => policy.RequireRole(ApplicationRoles.Seller))
            .AddPolicy(AuthorizationPolicies.Admin, policy => policy.RequireRole(ApplicationRoles.Admin));

        return services;
    }
}
