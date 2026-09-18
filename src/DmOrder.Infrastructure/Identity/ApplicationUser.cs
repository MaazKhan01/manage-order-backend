using Microsoft.AspNetCore.Identity;

namespace DmOrder.Infrastructure.Identity;

/// <summary>
/// The platform user, backed by ASP.NET Core Identity.
///
/// This lives in Infrastructure, not Domain, because identity is an infrastructure concern: password
/// hashing, normalised email, lockout and concurrency stamps are Identity's job. The domain only ever
/// needs a user's <see cref="Guid"/> — `Store.OwnerUserId` is a plain id, not a navigation property —
/// so Domain stays free of framework references.
/// </summary>
public sealed class ApplicationUser : IdentityUser<Guid>
{
    public required string DisplayName { get; set; }

    /// <summary>
    /// The platform admin's switch. A deactivated user can still present valid credentials, but login
    /// refuses to issue tokens and refresh stops working.
    /// </summary>
    public bool IsActive { get; set; } = true;

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public ICollection<RefreshToken> RefreshTokens { get; set; } = [];
}

public sealed class ApplicationRole : IdentityRole<Guid>
{
    public ApplicationRole() { }

    public ApplicationRole(string name) : base(name) { }
}
