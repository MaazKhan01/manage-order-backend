using DmOrder.Application.Common.Interfaces;
using DmOrder.Application.Common.Models;
using DmOrder.Domain.Identity;
using DmOrder.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace DmOrder.Infrastructure.Identity;

/// <summary>
/// Wraps ASP.NET Core Identity so the Application layer never sees UserManager, a password hash, or an
/// IdentityResult.
/// </summary>
public sealed class UserAccountService(
    UserManager<ApplicationUser> userManager,
    AppDbContext db,
    IDateTimeProvider clock) : IUserAccountService
{
    public async Task<RegistrationResult> RegisterSellerAsync(
        string email,
        string password,
        string displayName,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (await userManager.FindByEmailAsync(email) is not null)
        {
            return RegistrationResult.Failed(RegistrationOutcome.EmailAlreadyRegistered);
        }

        var now = clock.UtcNow;
        var user = new ApplicationUser
        {
            Id = Guid.CreateVersion7(),
            UserName = email,
            Email = email,
            DisplayName = displayName,
            IsActive = true,
            CreatedAt = now,
            UpdatedAt = now,
        };

        var created = await userManager.CreateAsync(user, password);
        if (!created.Succeeded)
        {
            // A duplicate can still surface here if two registrations race past the check above.
            var duplicate = created.Errors.Any(e => e.Code.Contains("Duplicate", StringComparison.Ordinal));
            return RegistrationResult.Failed(
                duplicate ? RegistrationOutcome.EmailAlreadyRegistered : RegistrationOutcome.PasswordRejected,
                created.Errors.Select(e => e.Description).ToArray());
        }

        var roleAssigned = await userManager.AddToRoleAsync(user, ApplicationRoles.Seller);
        if (!roleAssigned.Succeeded)
        {
            // A user without a role can do nothing and would be confusing to debug later. Roll back.
            await userManager.DeleteAsync(user);
            return RegistrationResult.Failed(
                RegistrationOutcome.PasswordRejected,
                "The account could not be created.");
        }

        return RegistrationResult.Succeeded(
            new AuthenticatedUser(user.Id, user.Email!, user.DisplayName, [ApplicationRoles.Seller], StoreId: null));
    }

    public async Task<AuthenticatedUser?> ValidateCredentialsAsync(
        string email,
        string password,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var user = await userManager.FindByEmailAsync(email);

        if (user is null)
        {
            // Hash the supplied password against nothing so that a missing account costs roughly the
            // same as a wrong password. Without this, response time reveals which emails are registered.
            userManager.PasswordHasher.HashPassword(
                new ApplicationUser { DisplayName = string.Empty },
                password);
            return null;
        }

        // Lockout and deactivation are checked before the password, so a locked or disabled account
        // stops costing password verifications — which is the entire point of a lockout.
        if (!user.IsActive || await userManager.IsLockedOutAsync(user))
        {
            return null;
        }

        if (!await userManager.CheckPasswordAsync(user, password))
        {
            // UserManager does not track failures on its own; this is what drives lockout.
            await userManager.AccessFailedAsync(user);
            return null;
        }

        await userManager.ResetAccessFailedCountAsync(user);

        return await ToAuthenticatedUserAsync(user);
    }

    public async Task<AuthenticatedUser?> FindActiveByIdAsync(Guid userId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var user = await userManager.FindByIdAsync(userId.ToString());
        return user is null || !user.IsActive ? null : await ToAuthenticatedUserAsync(user);
    }

    private async Task<AuthenticatedUser> ToAuthenticatedUserAsync(ApplicationUser user)
    {
        var roles = await userManager.GetRolesAsync(user);

        // The tenant is resolved from the database by owner id — never from anything the client sends.
        // Because this runs on every login and every refresh, a seller who has just created their store
        // picks up the claim on their next token rather than having to sign out and back in.
        var storeId = await db.Stores
            .Where(s => s.OwnerUserId == user.Id)
            .Select(s => (Guid?)s.Id)
            .FirstOrDefaultAsync();

        return new AuthenticatedUser(user.Id, user.Email!, user.DisplayName, [.. roles], storeId);
    }
}
