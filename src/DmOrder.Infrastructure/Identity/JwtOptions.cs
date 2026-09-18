using System.ComponentModel.DataAnnotations;

namespace DmOrder.Infrastructure.Identity;

public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    /// <summary>HMAC signing key. Minimum 32 bytes; generate one per environment and never reuse.</summary>
    [Required]
    [MinLength(32, ErrorMessage = "JWT_SECRET must be at least 32 characters.")]
    public string Secret { get; set; } = string.Empty;

    [Required]
    public string Issuer { get; set; } = "dmorder-api";

    [Required]
    public string Audience { get; set; } = "dmorder-web";

    /// <summary>
    /// Short by design. The access token is the thing that gets replayed if stolen, and the BFF
    /// refreshes it transparently, so a short life costs the user nothing.
    /// </summary>
    [Range(1, 120)]
    public int AccessTokenMinutes { get; set; } = 15;

    [Range(1, 90)]
    public int RefreshTokenDays { get; set; } = 14;
}
