using DmOrder.Application.Common.Models;

namespace DmOrder.Application.Features.Auth;

internal static class AuthMappings
{
    public static CurrentUserResponse ToResponse(this AuthenticatedUser user) =>
        new(user.UserId, user.Email, user.DisplayName, user.Roles, user.StoreId);

    public static AuthResponse ToResponse(this AuthResult result) =>
        new(
            result.User.ToResponse(),
            result.Tokens.AccessToken,
            result.Tokens.AccessTokenExpiresAt,
            result.Tokens.RefreshToken,
            result.Tokens.RefreshTokenExpiresAt);
}
