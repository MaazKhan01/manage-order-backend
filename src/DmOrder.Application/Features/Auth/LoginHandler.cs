using DmOrder.Application.Common.Interfaces;
using DmOrder.Application.Common.Models;
using FluentValidation;
using Microsoft.Extensions.Logging;

namespace DmOrder.Application.Features.Auth;

public sealed class LoginHandler(
    IUserAccountService accounts,
    ITokenService tokens,
    ILogger<LoginHandler> logger)
{
    public async Task<AuthResponse> HandleAsync(LoginRequest request, CancellationToken cancellationToken)
    {
        var user = await accounts.ValidateCredentialsAsync(
            request.Email.Trim(),
            request.Password,
            cancellationToken);

        if (user is null)
        {
            // One message for "no such account", "wrong password" and "deactivated". Distinguishing
            // them tells an attacker which addresses are worth attacking.
            logger.LogWarning("Failed login attempt for {Email}", request.Email);
            throw new UnauthorizedAccessException("Invalid email or password.");
        }

        logger.LogInformation("User {UserId} signed in", user.UserId);

        var issued = await tokens.IssueAsync(user, cancellationToken);
        return new AuthResult(user, issued).ToResponse();
    }
}

public sealed class LoginValidator : AbstractValidator<LoginRequest>
{
    public LoginValidator()
    {
        RuleFor(x => x.Email).NotEmpty().MaximumLength(256);

        // No minimum here: rejecting a short password at login would confirm the rules rather than
        // just failing the attempt.
        RuleFor(x => x.Password).NotEmpty().MaximumLength(128);
    }
}
