using System.ComponentModel.DataAnnotations;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using DmOrder.Application.Common.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DmOrder.Infrastructure.Email;

public sealed class EmailOptions
{
    public const string SectionName = "Email";

    /// <summary>Unset means no provider is configured and password reset reports itself unavailable.</summary>
    public string? ApiKey { get; set; }

    /// <summary>
    /// The From address. Must be on a domain verified with the provider, or every message is
    /// rejected - which is why this is checked at startup rather than discovered by a locked-out
    /// seller.
    /// </summary>
    /// <summary>
    /// Not annotated with [EmailAddress]: that attribute passes null but fails an empty string, and
    /// empty is how an unconfigured deployment - and the test host - says "no email provider". The
    /// address is checked below, only once there is one to check.
    /// </summary>
    public string? FromAddress { get; set; }

    public string FromName { get; set; } = "Ordviz";

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(ApiKey) && !string.IsNullOrWhiteSpace(FromAddress);

    /// <summary>
    /// A configured sender must have a usable From address. An unconfigured one is left alone -
    /// shipping without email is supported, shipping with a malformed sender is not.
    /// </summary>
    public IEnumerable<ValidationResult> ValidateSender()
    {
        if (!IsConfigured) yield break;

        if (!new EmailAddressAttribute().IsValid(FromAddress))
        {
            yield return new ValidationResult(
                "Email:FromAddress must be a valid address on a domain verified with the provider.");
        }
    }
}

/// <summary>
/// Transactional email through Resend.
///
/// Raw HTTP rather than an SDK: Resend publishes no official .NET package, the API is one POST, and
/// a hand-rolled client is less to keep current than a community wrapper.
///
/// Swapping providers - Postmark, SES, Mailgun - means writing one class against
/// <see cref="IEmailSender"/>. Nothing above Infrastructure knows which one is in use.
/// </summary>
public sealed class ResendEmailSender(
    HttpClient http,
    IOptions<EmailOptions> options,
    ILogger<ResendEmailSender> logger) : IEmailSender
{
    private readonly EmailOptions _options = options.Value;

    public bool IsConfigured => _options.IsConfigured;

    public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        if (!IsConfigured) throw new EmailSenderNotConfiguredException();

        using var request = new HttpRequestMessage(HttpMethod.Post, "emails")
        {
            Content = JsonContent.Create(new
            {
                from = $"{_options.FromName} <{_options.FromAddress}>",
                to = new[] { message.ToEmail },
                subject = message.Subject,
                html = message.HtmlBody,
                text = message.TextBody,
            }),
        };

        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiKey);

        using var response = await http.SendAsync(request, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);

            // The status and the provider's reason, never the recipient and never the body - this
            // message is about getting back into an account.
            logger.LogError(
                "Email provider rejected a message: {Status} {Reason}",
                (int)response.StatusCode,
                body.Length > 300 ? body[..300] : body);

            throw new HttpRequestException(
                $"The email provider returned {(int)response.StatusCode}.");
        }
    }
}

/// <summary>
/// The sender that exists when no API key is set: none.
///
/// Same shape as the unconfigured payment and AI providers. Nothing throws at startup; the one
/// feature that needs email says so and everything else is unaffected.
/// </summary>
public sealed class NotConfiguredEmailSender : IEmailSender
{
    public bool IsConfigured => false;

    public Task SendAsync(EmailMessage message, CancellationToken cancellationToken) =>
        throw new EmailSenderNotConfiguredException();
}
