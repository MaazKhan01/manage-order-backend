using DmOrder.Application.Common.Models;

namespace DmOrder.Application.Common.Interfaces;

/// <summary>
/// The seam over the identity store. Implemented in Infrastructure on ASP.NET Core Identity, so use
/// cases never touch UserManager and never see a password hash.
/// </summary>
public interface IUserAccountService
{
    Task<RegistrationResult> RegisterSellerAsync(
        string email,
        string password,
        string displayName,
        CancellationToken cancellationToken);

    /// <summary>
    /// Returns the user when the credentials are valid and the account is active, otherwise null.
    /// Implementations must take the same amount of work whether or not the email exists, so that
    /// response timing does not reveal which accounts are registered.
    /// </summary>
    Task<AuthenticatedUser?> ValidateCredentialsAsync(
        string email,
        string password,
        CancellationToken cancellationToken);

    /// <summary>Returns the user, or null if they no longer exist or have been deactivated.</summary>
    Task<AuthenticatedUser?> FindActiveByIdAsync(Guid userId, CancellationToken cancellationToken);
}
