namespace DmOrder.Application.Features.Auth;

public sealed record RegisterRequest(string Email, string Password, string DisplayName);

public sealed record LoginRequest(string Email, string Password);

/// <summary>
/// What the BFF receives. The refresh token is returned so the Next.js route handler can put it in an
/// httpOnly cookie; it is never exposed to browser JavaScript.
/// </summary>
public sealed record AuthResponse(
    CurrentUserResponse User,
    string AccessToken,
    DateTimeOffset AccessTokenExpiresAt,
    string RefreshToken,
    DateTimeOffset RefreshTokenExpiresAt);

public sealed record CurrentUserResponse(
    Guid Id,
    string Email,
    string DisplayName,
    IReadOnlyList<string> Roles,
    Guid? StoreId)
{
    public bool HasStore => StoreId is not null;
}
