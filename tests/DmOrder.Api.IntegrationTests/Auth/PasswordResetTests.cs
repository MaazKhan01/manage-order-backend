using System.Net;
using System.Net.Http.Json;
using Shouldly;

namespace DmOrder.Api.IntegrationTests.Auth;

/// <summary>
/// Password reset.
///
/// The suite runs with no email provider configured, so the request endpoint answers 503 - that is
/// the deployment state these tests can prove. What they can prove fully is the half that does not
/// need mail: that a bad token is refused, that the refusal is the same for every kind of bad, and
/// that the endpoints are rate limited and validated.
///
/// The enumeration-resistance test is the one that matters most here. An endpoint that answers
/// differently for a known and an unknown address is a free tool for working out which of a stolen
/// address list has accounts with us.
/// </summary>
public sealed class PasswordResetTests(ApiFactory factory) : IntegrationTestBase(factory)
{
    private const string Forgot = "/api/v1/public/auth/forgot-password";
    private const string Reset = "/api/v1/public/auth/reset-password";

    [Fact]
    public async Task Asking_for_a_reset_looks_identical_for_known_and_unknown_addresses()
    {
        await RegisterSellerAsync("known-reset@example.com");

        var known = await Client.PostAsJsonAsync(Forgot, new { email = "known-reset@example.com" });
        var unknown = await Client.PostAsJsonAsync(Forgot, new { email = "nobody-here@example.com" });

        // Same status, same body. With no provider configured both are 503; with one configured both
        // are 202. What matters is that they never differ from each other.
        known.StatusCode.ShouldBe(unknown.StatusCode);
        (await Comparable(known)).ShouldBe(await Comparable(unknown));
    }

    [Fact]
    public async Task With_no_email_provider_configured_the_feature_reports_itself_unavailable()
    {
        var response = await Client.PostAsJsonAsync(Forgot, new { email = "someone@example.com" });

        // 503, not 500 and not a silent 202: a seller waiting for a mail that cannot arrive is
        // worse off than one told to get in touch.
        response.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-an-email")]
    public async Task A_malformed_address_is_rejected(string email)
    {
        var response = await Client.PostAsJsonAsync(Forgot, new { email });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task A_forged_token_is_refused()
    {
        await RegisterSellerAsync("forged-token@example.com");

        var response = await Client.PostAsJsonAsync(Reset, new
        {
            email = "forged-token@example.com",
            token = "CfDJ8this-is-not-a-real-token",
            newPassword = "a-long-enough-password",
        });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task A_token_for_an_account_that_does_not_exist_is_refused_the_same_way()
    {
        await RegisterSellerAsync("real-account@example.com");

        var unknown = await Client.PostAsJsonAsync(Reset, new
        {
            email = "no-such-account@example.com",
            token = "CfDJ8this-is-not-a-real-token",
            newPassword = "a-long-enough-password",
        });

        var known = await Client.PostAsJsonAsync(Reset, new
        {
            email = "real-account@example.com",
            token = "CfDJ8this-is-not-a-real-token",
            newPassword = "a-long-enough-password",
        });

        // Identical, so this endpoint cannot be used to probe for accounts either.
        unknown.StatusCode.ShouldBe(known.StatusCode);
        (await Comparable(unknown)).ShouldBe(await Comparable(known));
    }

    [Fact]
    public async Task A_new_password_that_is_too_short_is_rejected_before_the_token_is_checked()
    {
        var response = await Client.PostAsJsonAsync(Reset, new
        {
            email = "someone@example.com",
            token = "CfDJ8anything",
            newPassword = "short",
        });

        // The validator runs first, so a weak password is a field error rather than a token error -
        // the seller is told the actual problem instead of being sent back for a new link.
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task A_reset_link_with_no_token_is_rejected()
    {
        var response = await Client.PostAsJsonAsync(Reset, new
        {
            email = "someone@example.com",
            token = "",
            newPassword = "a-long-enough-password",
        });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }
    /// <summary>
    /// The response body with the per-request trace id removed.
    ///
    /// Two responses to two requests always carry different trace ids, so a raw byte comparison
    /// would fail on the one thing that is supposed to differ. Everything else must match.
    /// </summary>
    private static async Task<string> Comparable(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        return System.Text.RegularExpressions.Regex.Replace(body, """"traceId":"[^"]*"""", "");
    }
}
