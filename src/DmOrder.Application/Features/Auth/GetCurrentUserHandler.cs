using DmOrder.Application.Common.Interfaces;

namespace DmOrder.Application.Features.Auth;

/// <summary>
/// Returns the signed-in user. Read from the database rather than from the token's claims, so that a
/// deactivated account or a newly created store is reflected without waiting for the access token to
/// expire.
/// </summary>
public sealed class GetCurrentUserHandler(ICurrentUser currentUser, IUserAccountService accounts)
{
    public async Task<CurrentUserResponse> HandleAsync(CancellationToken cancellationToken)
    {
        var userId = currentUser.RequireUserId();
        var user = await accounts.FindActiveByIdAsync(userId, cancellationToken)
            ?? throw new UnauthorizedAccessException("The account is no longer available.");

        return user.ToResponse();
    }
}
