using System.Net;
using DmOrder.Application.Common.Interfaces;
using DmOrder.Application.Common.Models;
using FluentValidation;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DmOrder.Application.Features.Auth;

public sealed record ForgotPasswordRequest(string Email);

public sealed class ForgotPasswordValidator : AbstractValidator<ForgotPasswordRequest>
{
    public ForgotPasswordValidator()
    {
        RuleFor(x => x.Email)
            .NotEmpty().WithMessage("Enter the email address you signed up with.")
            .MaximumLength(256)
            .EmailAddress().WithMessage("Enter a valid email address.");
    }
}

/// <summary>
/// Starts a password reset.
///
/// **Always reports the same thing.** Whether or not an account exists, whether or not it is
/// suspended, whether or not the mail actually went out, the caller is told that a link has been
/// sent if the address is registered. Anything else turns this into a free tool for discovering
/// which of a list of addresses have accounts here - and that list is worth something to whoever is
/// phishing them next.
///
/// The one exception is "no provider configured", which is about our deployment rather than about
/// the account, and which the seller needs to know because waiting for a mail that cannot arrive is
/// worse than being told to get in touch.
/// </summary>
public sealed class ForgotPasswordHandler(
    IUserAccountService accounts,
    IEmailSender email,
    IOptions<PlatformBrandingOptions> branding,
    ILogger<ForgotPasswordHandler> logger)
{
    public async Task HandleAsync(ForgotPasswordRequest request, CancellationToken cancellationToken)
    {
        if (!email.IsConfigured) throw new EmailSenderNotConfiguredException();

        var reset = await accounts.CreatePasswordResetTokenAsync(request.Email.Trim(), cancellationToken);

        if (reset is null)
        {
            // Logged without the address: knowing that a reset was attempted for an unknown account
            // is operationally useful, knowing which address was probed is not worth storing.
            logger.LogInformation("Password reset requested for an address with no active account.");
            return;
        }

        var brand = branding.Value;
        var link = BuildResetLink(brand.PlatformUrl, reset.Email, reset.Token);

        try
        {
            await email.SendAsync(
                new EmailMessage(
                    reset.Email,
                    reset.DisplayName,
                    $"Reset your {brand.ProjectName} password",
                    HtmlBody(brand.ProjectName, reset.DisplayName, link),
                    TextBody(brand.ProjectName, reset.DisplayName, link)),
                cancellationToken);
        }
        catch (Exception error) when (error is not EmailSenderNotConfiguredException)
        {
            // A provider outage must not become an account-enumeration oracle: the caller is told
            // the same thing either way, and this is ours to notice in the logs.
            logger.LogError(error, "Could not send a password reset email for user {UserId}.", reset.UserId);
        }
    }

    /// <summary>
    /// The link the seller clicks.
    ///
    /// Both values are URL-encoded. An Identity reset token is base64 and routinely contains "+"
    /// and "/", which arrive mangled if they are pasted into a query string raw - producing an
    /// "invalid link" for a token that was perfectly good.
    /// </summary>
    private static string BuildResetLink(string platformUrl, string email, string token) =>
        $"{platformUrl.TrimEnd('/')}/reset-password"
        + $"?email={WebUtility.UrlEncode(email)}"
        + $"&token={WebUtility.UrlEncode(token)}";

    private static string HtmlBody(string project, string name, string link) =>
        $"""
        <p>Hi {WebUtility.HtmlEncode(name)},</p>
        <p>Use the link below to choose a new {WebUtility.HtmlEncode(project)} password. It works once and expires in two hours.</p>
        <p><a href="{link}">Choose a new password</a></p>
        <p>If you did not ask for this, you can ignore this email - your password has not changed.</p>
        """;

    private static string TextBody(string project, string name, string link) =>
        $"""
        Hi {name},

        Use the link below to choose a new {project} password. It works once and expires in two hours.

        {link}

        If you did not ask for this, you can ignore this email - your password has not changed.
        """;
}

public sealed record ResetPasswordRequest(string Email, string Token, string NewPassword);

public sealed class ResetPasswordValidator : AbstractValidator<ResetPasswordRequest>
{
    public ResetPasswordValidator()
    {
        RuleFor(x => x.Email).NotEmpty().MaximumLength(256).EmailAddress();
        RuleFor(x => x.Token).NotEmpty().WithMessage("This reset link is incomplete.");

        // The same rule registration enforces, stated here so it is not two numbers that can drift.
        RuleFor(x => x.NewPassword)
            .NotEmpty().WithMessage("Choose a new password.")
            .MinimumLength(10).WithMessage("Use at least 10 characters.")
            .MaximumLength(128).WithMessage("Use at most 128 characters.");
    }
}

/// <summary>
/// Finishes a password reset, and ends every session that was open before it.
/// </summary>
public sealed class ResetPasswordHandler(IUserAccountService accounts, ITokenService tokens)
{
    public async Task HandleAsync(ResetPasswordRequest request, CancellationToken cancellationToken)
    {
        var result = await accounts.ResetPasswordAsync(
            request.Email.Trim(),
            request.Token,
            request.NewPassword,
            cancellationToken);

        if (result.Outcome == PasswordResetOutcome.Succeeded)
        {
            // Every session opened before this moment is now dead, including whoever prompted the
            // reset. Done here rather than inside the account service because TokenService already
            // depends on that service, and the reverse edge would be a cycle.
            await tokens.RevokeAllForUserAsync(result.UserId!.Value, "password reset", cancellationToken);
            return;
        }

        throw result.Outcome switch
        {
            PasswordResetOutcome.WeakPassword => new Common.Exceptions.RequestValidationException(
                new Dictionary<string, string[]>
                {
                    ["newPassword"] = ["That password is not strong enough. Use at least 10 characters."],
                }),

            // Wrong, expired and already-used are one answer on purpose.
            _ => new Common.Exceptions.RequestValidationException(
                new Dictionary<string, string[]>
                {
                    ["token"] =
                    [
                        "This reset link has expired or has already been used. "
                        + "Ask for a new one and try again.",
                    ],
                }),
        };
    }
}
