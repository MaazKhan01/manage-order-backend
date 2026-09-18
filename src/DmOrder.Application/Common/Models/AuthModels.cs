namespace DmOrder.Application.Common.Models;

/// <summary>
/// Who the caller is, as far as the application is concerned. Deliberately not an Identity type —
/// use cases should not know that ASP.NET Core Identity exists.
/// </summary>
public sealed record AuthenticatedUser(
    Guid UserId,
    string Email,
    string DisplayName,
    IReadOnlyList<string> Roles,
    Guid? StoreId);

/// <summary>
/// A freshly issued token pair. The raw refresh token exists here and in the client's cookie only —
/// the database stores a hash of it.
/// </summary>
public sealed record AuthTokens(
    string AccessToken,
    DateTimeOffset AccessTokenExpiresAt,
    string RefreshToken,
    DateTimeOffset RefreshTokenExpiresAt);

public sealed record AuthResult(AuthenticatedUser User, AuthTokens Tokens);

/// <summary>
/// Why a registration attempt failed, without leaking which. The API turns every failure into the
/// same generic response so the endpoint cannot be used to enumerate registered email addresses.
/// </summary>
public enum RegistrationOutcome
{
    Success,
    EmailAlreadyRegistered,
    PasswordRejected,
}

public sealed record RegistrationResult(
    RegistrationOutcome Outcome,
    AuthenticatedUser? User,
    IReadOnlyList<string> Errors)
{
    public static RegistrationResult Succeeded(AuthenticatedUser user) =>
        new(RegistrationOutcome.Success, user, []);

    public static RegistrationResult Failed(RegistrationOutcome outcome, params string[] errors) =>
        new(outcome, null, errors);
}
