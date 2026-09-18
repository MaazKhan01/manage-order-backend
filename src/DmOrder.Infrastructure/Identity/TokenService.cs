using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using DmOrder.Application.Common.Interfaces;
using DmOrder.Application.Common.Models;
using DmOrder.Domain.Identity;
using DmOrder.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace DmOrder.Infrastructure.Identity;

public sealed class TokenService(
    AppDbContext db,
    IUserAccountService accounts,
    IDateTimeProvider clock,
    IOptions<JwtOptions> options,
    ILogger<TokenService> logger) : ITokenService
{
    private readonly JwtOptions _options = options.Value;

    public async Task<AuthTokens> IssueAsync(AuthenticatedUser user, CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        var (rawRefreshToken, entity) = CreateRefreshToken(user.UserId, Guid.CreateVersion7(), now);

        db.RefreshTokens.Add(entity);
        await db.SaveChangesAsync(cancellationToken);

        return BuildTokens(user, rawRefreshToken, entity.ExpiresAt, now);
    }

    public async Task<AuthResult?> RefreshAsync(string refreshToken, CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        var hash = Hash(refreshToken);

        var existing = await db.RefreshTokens
            .FirstOrDefaultAsync(t => t.TokenHash == hash, cancellationToken);

        if (existing is null)
        {
            return null;
        }

        // The token exists but is already revoked. Either this is a replay of a rotated token — meaning
        // two parties hold it — or a logged-out session. Both warrant killing the whole family: a
        // legitimate user just signs in again, an attacker loses the stolen chain.
        if (existing.RevokedAt is not null)
        {
            logger.LogWarning(
                "Refresh token reuse detected for user {UserId}; revoking family {FamilyId}",
                existing.UserId, existing.FamilyId);

            await RevokeFamilyAsync(existing.FamilyId, RefreshTokenRevocationReasons.ReuseDetected, now, cancellationToken);
            return null;
        }

        if (existing.ExpiresAt <= now)
        {
            return null;
        }

        // Re-read the user so a deactivation takes effect at the next refresh rather than whenever the
        // access token happens to expire.
        var user = await accounts.FindActiveByIdAsync(existing.UserId, cancellationToken);
        if (user is null)
        {
            await RevokeFamilyAsync(existing.FamilyId, RefreshTokenRevocationReasons.UserDeactivated, now, cancellationToken);
            return null;
        }

        var (rawReplacement, replacement) = CreateRefreshToken(existing.UserId, existing.FamilyId, now);

        existing.RevokedAt = now;
        existing.RevokedReason = RefreshTokenRevocationReasons.Rotated;
        existing.ReplacedByTokenId = replacement.Id;

        db.RefreshTokens.Add(replacement);
        await db.SaveChangesAsync(cancellationToken);

        return new AuthResult(user, BuildTokens(user, rawReplacement, replacement.ExpiresAt, now));
    }

    public async Task RevokeAsync(string refreshToken, CancellationToken cancellationToken)
    {
        var hash = Hash(refreshToken);

        var existing = await db.RefreshTokens
            .FirstOrDefaultAsync(t => t.TokenHash == hash && t.RevokedAt == null, cancellationToken);

        if (existing is null)
        {
            // Logout is idempotent. Reporting "unknown token" would be an oracle, and re-logging-out is
            // a normal thing for a browser to do.
            return;
        }

        existing.RevokedAt = clock.UtcNow;
        existing.RevokedReason = RefreshTokenRevocationReasons.Logout;
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task RevokeAllForUserAsync(Guid userId, string reason, CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;

        await db.RefreshTokens
            .Where(t => t.UserId == userId && t.RevokedAt == null)
            .ExecuteUpdateAsync(
                set => set.SetProperty(t => t.RevokedAt, now).SetProperty(t => t.RevokedReason, reason),
                cancellationToken);
    }

    private async Task RevokeFamilyAsync(
        Guid familyId,
        string reason,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await db.RefreshTokens
            .Where(t => t.FamilyId == familyId && t.RevokedAt == null)
            .ExecuteUpdateAsync(
                set => set.SetProperty(t => t.RevokedAt, now).SetProperty(t => t.RevokedReason, reason),
                cancellationToken);
    }

    private (string Raw, RefreshToken Entity) CreateRefreshToken(Guid userId, Guid familyId, DateTimeOffset now)
    {
        // 256 bits of entropy. The raw value leaves the server once; only its hash is persisted, so a
        // database leak does not hand over working sessions.
        var raw = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

        return (raw, new RefreshToken
        {
            UserId = userId,
            FamilyId = familyId,
            TokenHash = Hash(raw),
            CreatedAt = now,
            ExpiresAt = now.AddDays(_options.RefreshTokenDays),
        });
    }

    private AuthTokens BuildTokens(
        AuthenticatedUser user,
        string rawRefreshToken,
        DateTimeOffset refreshExpiresAt,
        DateTimeOffset now)
    {
        var accessExpiresAt = now.AddMinutes(_options.AccessTokenMinutes);

        var claims = new List<Claim>
        {
            new(AppClaimTypes.UserId, user.UserId.ToString()),
            new(AppClaimTypes.Email, user.Email),
            new(JwtRegisteredClaimNames.Jti, Guid.CreateVersion7().ToString()),
        };

        claims.AddRange(user.Roles.Select(role => new Claim(AppClaimTypes.Role, role)));

        if (user.StoreId is { } storeId)
        {
            claims.Add(new Claim(AppClaimTypes.StoreId, storeId.ToString()));
        }

        var credentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.Secret)),
            SecurityAlgorithms.HmacSha256);

        var descriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claims),
            Issuer = _options.Issuer,
            Audience = _options.Audience,
            IssuedAt = now.UtcDateTime,
            NotBefore = now.UtcDateTime,
            Expires = accessExpiresAt.UtcDateTime,
            SigningCredentials = credentials,
        };

        var accessToken = new JsonWebTokenHandler().CreateToken(descriptor);

        return new AuthTokens(accessToken, accessExpiresAt, rawRefreshToken, refreshExpiresAt);
    }

    /// <summary>
    /// SHA-256 is correct here, not a password hash: the token already has full entropy, so slow
    /// hashing would only slow down every authenticated request for no security gain.
    /// </summary>
    private static string Hash(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}
