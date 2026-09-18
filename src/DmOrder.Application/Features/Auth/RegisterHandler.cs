using DmOrder.Application.Common.Interfaces;
using DmOrder.Application.Common.Models;
using DmOrder.Domain.Exceptions;
using FluentValidation;
using Microsoft.Extensions.Logging;

namespace DmOrder.Application.Features.Auth;

public sealed class RegisterHandler(
    IUserAccountService accounts,
    ITokenService tokens,
    ILogger<RegisterHandler> logger)
{
    public async Task<AuthResponse> HandleAsync(RegisterRequest request, CancellationToken cancellationToken)
    {
        var result = await accounts.RegisterSellerAsync(
            request.Email.Trim(),
            request.Password,
            request.DisplayName.Trim(),
            cancellationToken);

        switch (result.Outcome)
        {
            case RegistrationOutcome.Success when result.User is not null:
                logger.LogInformation("Seller registered {UserId}", result.User.UserId);
                var issued = await tokens.IssueAsync(result.User, cancellationToken);
                return new AuthResult(result.User, issued).ToResponse();

            case RegistrationOutcome.EmailAlreadyRegistered:
                // Logged, not returned. Telling the caller that an address is taken turns this endpoint
                // into an account-enumeration oracle.
                logger.LogInformation("Registration rejected: email already registered");
                throw new ConflictException("That email address cannot be used to register.");

            default:
                throw new BusinessRuleException(
                    result.Errors.Count > 0
                        ? string.Join(" ", result.Errors)
                        : "The account could not be created.");
        }
    }
}

public sealed class RegisterValidator : AbstractValidator<RegisterRequest>
{
    public RegisterValidator()
    {
        RuleFor(x => x.Email)
            .NotEmpty().WithMessage("Email is required.")
            .MaximumLength(256)
            .EmailAddress().WithMessage("Enter a valid email address.");

        RuleFor(x => x.DisplayName)
            .NotEmpty().WithMessage("Your name is required.")
            .MaximumLength(120);

        // Length beats composition rules for real-world strength, and a maximum matters because the
        // password hash runs over whatever arrives.
        RuleFor(x => x.Password)
            .NotEmpty().WithMessage("Password is required.")
            .MinimumLength(10).WithMessage("Use at least 10 characters.")
            .MaximumLength(128).WithMessage("Use at most 128 characters.");
    }
}
