using DmOrder.Api.Common;
using DmOrder.Application.Features.Auth;
using Microsoft.AspNetCore.RateLimiting;

namespace DmOrder.Api.Endpoints;

public static class AuthEndpoints
{
    /// <summary>
    /// Anonymous auth endpoints. These are the platform's most attacked surface, so each one is rate
    /// limited and none of them reveal whether a given email address is registered.
    /// </summary>
    public static RouteGroupBuilder MapPublicAuthEndpoints(this RouteGroupBuilder group)
    {
        var auth = group.MapGroup("/auth")
            .RequireRateLimiting(RateLimitPolicies.Auth)
            .WithTags("Auth");

        // Both sit under the Auth rate-limit policy with login and register. Reset is a credential
        // operation and attracts the same attention: an unlimited forgot-password endpoint is a free
        // way to send mail from our domain to anyone, which is how a sending reputation is lost.
        auth.MapPost("/forgot-password", async (
                ForgotPasswordRequest request,
                ForgotPasswordHandler handler,
                CancellationToken cancellationToken) =>
            {
                await handler.HandleAsync(request, cancellationToken);

                // Deliberately identical whether or not the account exists, and whether or not the
                // mail was actually sent. See ForgotPasswordHandler.
                return Results.Accepted();
            })
            .WithValidation<ForgotPasswordRequest>()
            .WithName("ForgotPassword")
            .WithSummary(
                "Ask for a password reset link. Always reports the same result, so this cannot be "
                + "used to discover which addresses have accounts.");

        auth.MapPost("/reset-password", async (
                ResetPasswordRequest request,
                ResetPasswordHandler handler,
                CancellationToken cancellationToken) =>
            {
                await handler.HandleAsync(request, cancellationToken);
                return Results.NoContent();
            })
            .WithValidation<ResetPasswordRequest>()
            .WithName("ResetPassword")
            .WithSummary(
                "Set a new password from a reset link. Ends every session that was open before it.");

        auth.MapPost("/register", async (
                RegisterRequest request,
                RegisterHandler handler,
                CancellationToken cancellationToken) =>
            {
                var response = await handler.HandleAsync(request, cancellationToken);
                return Results.Ok(response);
            })
            .WithValidation<RegisterRequest>()
            .WithName("Register")
            .WithSummary("Create a seller account and return a token pair.");

        auth.MapPost("/login", async (
                LoginRequest request,
                LoginHandler handler,
                CancellationToken cancellationToken) =>
            {
                var response = await handler.HandleAsync(request, cancellationToken);
                return Results.Ok(response);
            })
            .WithValidation<LoginRequest>()
            .WithName("Login")
            .WithSummary("Exchange credentials for a token pair.");

        auth.MapPost("/refresh", async (
                RefreshRequest request,
                RefreshHandler handler,
                CancellationToken cancellationToken) =>
            {
                var response = await handler.HandleAsync(request, cancellationToken);
                return Results.Ok(response);
            })
            .WithName("Refresh")
            .WithSummary("Rotate a refresh token and issue a new pair.");

        // Logout only needs the refresh token, which the BFF holds. Requiring a valid *access* token
        // would make it impossible to log out of an expired session, which is when people try hardest.
        auth.MapPost("/logout", async (
                RefreshRequest request,
                LogoutHandler handler,
                CancellationToken cancellationToken) =>
            {
                await handler.HandleAsync(request, cancellationToken);
                return Results.NoContent();
            })
            .WithName("Logout")
            .WithSummary("Revoke a refresh token. Idempotent.");

        return group;
    }

    public static RouteGroupBuilder MapSellerAccountEndpoints(this RouteGroupBuilder group)
    {
        group.MapGet("/me", async (GetCurrentUserHandler handler, CancellationToken cancellationToken) =>
            {
                var response = await handler.HandleAsync(cancellationToken);
                return Results.Ok(response);
            })
            .WithName("GetCurrentUser")
            .WithSummary("The signed-in user, read fresh from the database.")
            .WithTags("Auth");

        return group;
    }
}
