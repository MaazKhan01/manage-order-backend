using DmOrder.Application.Common.Models;

namespace DmOrder.Application.Common.Interfaces;

/// <summary>
/// Issues and rotates the access/refresh token pair. The concrete scheme (JWT, expiry, signing) is an
/// Infrastructure concern; use cases only ever see <see cref="AuthTokens"/>.
/// </summary>
public interface ITokenService
{
    Task<AuthTokens> IssueAsync(AuthenticatedUser user, CancellationToken cancellationToken);

    /// <summary>
    /// Validates a refresh token and rotates it. Returns null when the token is unknown, expired or
    /// already revoked.
    ///
    /// Implementations must treat presentation of an *already rotated* token as theft and revoke the
    /// entire token family, because two parties holding one token means one of them is not the user.
    /// </summary>
    Task<AuthResult?> RefreshAsync(string refreshToken, CancellationToken cancellationToken);

    /// <summary>Revokes a single refresh token. Unknown tokens are ignored — logout is idempotent.</summary>
    Task RevokeAsync(string refreshToken, CancellationToken cancellationToken);

    Task RevokeAllForUserAsync(Guid userId, string reason, CancellationToken cancellationToken);
}
