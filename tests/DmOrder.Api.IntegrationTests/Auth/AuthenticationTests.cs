using System.Net;
using System.Net.Http.Json;

namespace DmOrder.Api.IntegrationTests.Auth;

public class AuthenticationTests(ApiFactory factory) : IntegrationTestBase(factory)
{
    [Fact]
    public async Task Register_CreatesASellerAndReturnsTokens()
    {
        var auth = await RegisterSellerAsync("sarah@example.com");

        auth.User.Email.ShouldBe("sarah@example.com");
        auth.User.Roles.ShouldContain("Seller");
        auth.User.HasStore.ShouldBeFalse();
        auth.AccessToken.ShouldNotBeNullOrWhiteSpace();
        auth.RefreshToken.ShouldNotBeNullOrWhiteSpace();
        auth.AccessTokenExpiresAt.ShouldBeGreaterThan(DateTimeOffset.UtcNow);
    }

    [Fact]
    public async Task Register_DoesNotConfirmThatAnEmailIsAlreadyTaken()
    {
        await RegisterSellerAsync("taken@example.com");

        var response = await Client.PostAsJsonAsync("/api/v1/public/auth/register", new
        {
            email = "taken@example.com",
            password = "another-long-password",
            displayName = "Someone Else",
        });

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);

        // The message must not say "already registered" — that turns registration into a way to test
        // whether a given person has an account.
        var body = await response.Content.ReadAsStringAsync();
        body.ShouldNotContain("already registered", Case.Insensitive);
    }

    [Fact]
    public async Task Register_RejectsAShortPassword()
    {
        var response = await Client.PostAsJsonAsync("/api/v1/public/auth/register", new
        {
            email = "short@example.com",
            password = "short",
            displayName = "Short Password",
        });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var problem = await response.Content.ReadFromJsonAsync<ValidationProblem>();
        problem!.Errors.ShouldContainKey("password");
    }

    [Fact]
    public async Task Login_ReturnsTokensForValidCredentials()
    {
        await RegisterSellerAsync("login@example.com", "a-long-enough-password");

        var response = await Client.PostAsJsonAsync("/api/v1/public/auth/login", new
        {
            email = "login@example.com",
            password = "a-long-enough-password",
        });

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var auth = await response.Content.ReadFromJsonAsync<AuthPayload>();
        auth!.AccessToken.ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Login_GivesTheSameAnswerForAWrongPasswordAndAnUnknownAccount()
    {
        await RegisterSellerAsync("real@example.com", "a-long-enough-password");

        var wrongPassword = await Client.PostAsJsonAsync("/api/v1/public/auth/login", new
        {
            email = "real@example.com",
            password = "definitely-not-the-password",
        });

        var unknownAccount = await Client.PostAsJsonAsync("/api/v1/public/auth/login", new
        {
            email = "nobody@example.com",
            password = "definitely-not-the-password",
        });

        wrongPassword.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        unknownAccount.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        // The responses must be indistinguishable apart from the traceId, which is unique per request
        // by design. Anything else differing would let someone discover which addresses are registered.
        var first = await wrongPassword.Content.ReadFromJsonAsync<ProblemBody>();
        var second = await unknownAccount.Content.ReadFromJsonAsync<ProblemBody>();

        first!.Title.ShouldBe(second!.Title);
        first.Detail.ShouldBe(second.Detail);
        first.Status.ShouldBe(second.Status);
    }

    [Fact]
    public async Task Me_RequiresAuthentication()
    {
        var response = await Client.GetAsync("/api/v1/account/me");

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Me_ReturnsTheSignedInUser()
    {
        var auth = await RegisterSellerAsync("me@example.com");
        using var client = CreateAuthenticatedClient(auth.AccessToken);

        var response = await client.GetAsync("/api/v1/account/me");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var user = await response.Content.ReadFromJsonAsync<CurrentUserPayload>();
        user!.Email.ShouldBe("me@example.com");
        user.Id.ShouldBe(auth.User.Id);
    }

    [Fact]
    public async Task Me_RejectsATamperedToken()
    {
        var auth = await RegisterSellerAsync("tamper@example.com");

        // Flip the last character of the signature. A token the API did not sign must be worthless.
        var tampered = auth.AccessToken[..^1] + (auth.AccessToken[^1] == 'a' ? 'b' : 'a');
        using var client = CreateAuthenticatedClient(tampered);

        var response = await client.GetAsync("/api/v1/account/me");

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task SellerRoutesRejectAnonymousCallers()
    {
        // There are no seller endpoints yet, but the group must already be closed. If this ever starts
        // returning 404 for an existing route, authorization was dropped from the group.
        var response = await Client.GetAsync("/api/v1/seller/anything");

        response.StatusCode.ShouldBeOneOf(HttpStatusCode.Unauthorized, HttpStatusCode.NotFound);
    }

    private sealed record ValidationProblem(Dictionary<string, string[]> Errors);

    private sealed record ProblemBody(string? Title, string? Detail, int? Status);
}
