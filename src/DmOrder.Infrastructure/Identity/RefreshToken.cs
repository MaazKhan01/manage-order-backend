namespace DmOrder.Infrastructure.Identity;

/// <summary>
/// A refresh token, stored hashed and rotated on every use.
///
/// Rotation plus <see cref="FamilyId"/> is what makes theft detectable: presenting a token that has
/// already been rotated means two parties hold it, so the whole family is revoked and both are forced
/// to log in again. Without that, a stolen refresh token is a permanent session.
/// </summary>
public sealed class RefreshToken
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid UserId { get; set; }

    public ApplicationUser User { get; set; } = null!;

    /// <summary>SHA-256 of the token. The raw value exists only in the response and the client's cookie.</summary>
    public required string TokenHash { get; set; }

    /// <summary>Groups every token descended from one login, so a whole chain can be revoked at once.</summary>
    public Guid FamilyId { get; set; }

    public DateTimeOffset ExpiresAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? RevokedAt { get; set; }

    public string? RevokedReason { get; set; }

    /// <summary>Set when this token was rotated, pointing at its replacement.</summary>
    public Guid? ReplacedByTokenId { get; set; }

    public bool IsActive(DateTimeOffset now) => RevokedAt is null && ExpiresAt > now;
}

public static class RefreshTokenRevocationReasons
{
    public const string Rotated = "rotated";
    public const string Logout = "logout";
    public const string ReuseDetected = "reuse-detected";
    public const string UserDeactivated = "user-deactivated";
}
