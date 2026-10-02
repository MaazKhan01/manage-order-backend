namespace DmOrder.Application.Common.Interfaces;

/// <summary>
/// Sends transactional email.
///
/// Deliberately narrow. This is not a marketing or notification system - it exists because a seller
/// who forgets their password otherwise has no way back into their own account, and the only
/// alternative is editing the database by hand.
/// </summary>
public interface IEmailSender
{
    /// <summary>
    /// False when no provider is configured. Password reset then reports itself unavailable rather
    /// than accepting a request it cannot fulfil - a reset flow that silently sends nothing is worse
    /// than one that says it is switched off.
    /// </summary>
    bool IsConfigured { get; }

    Task SendAsync(EmailMessage message, CancellationToken cancellationToken);
}

/// <param name="HtmlBody">
/// The rendered message. <paramref name="TextBody"/> is sent alongside it rather than instead: some
/// clients prefer plain text, and a mail with no text part scores worse with spam filters, which
/// matters when the one email this product sends is the one people need most.
/// </param>
public sealed record EmailMessage(
    string ToEmail,
    string ToName,
    string Subject,
    string HtmlBody,
    string TextBody);

/// <summary>Thrown when no email provider is configured. Surfaces as 503, not 500.</summary>
public sealed class EmailSenderNotConfiguredException()
    : Exception("No email provider is configured, so this message cannot be sent.");
