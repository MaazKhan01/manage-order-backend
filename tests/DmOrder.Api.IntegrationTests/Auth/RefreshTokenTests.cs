using System.Net;
using System.Net.Http.Json;

namespace DmOrder.Api.IntegrationTests.Auth;

public class RefreshTokenTests(ApiFactory factory) : IntegrationTestBase(factory)
{
    [Fact]
    public async Task Refresh_RotatesTheToken()
    {
        var auth = await RegisterSellerAsync("rotate@example.com");

        var response = await Client.PostAsJsonAsync(
            "/api/v1/public/auth/refresh",
            new { refreshToken = auth.RefreshToken });

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var refreshed = await response.Content.ReadFromJsonAsync<AuthPayload>();
        refreshed!.RefreshToken.ShouldNotBe(auth.RefreshToken);
        refreshed.User.Id.ShouldBe(auth.User.Id);
    }

    [Fact]
    public async Task Refresh_RejectsAnAlreadyRotatedToken()
    {
        var auth = await RegisterSellerAsync("replay@example.com");

        var first = await Client.PostAsJsonAsync(
            "/api/v1/public/auth/refresh",
            new { refreshToken = auth.RefreshToken });
        first.StatusCode.ShouldBe(HttpStatusCode.OK);

        // Replaying the original token: either it was stolen, or the legitimate client is confused.
        var replay = await Client.PostAsJsonAsync(
            "/api/v1/public/auth/refresh",
            new { refreshToken = auth.RefreshToken });

        replay.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Refresh_ReuseKillsTheWholeFamily()
    {
        var auth = await RegisterSellerAsync("family@example.com");

        var rotated = await (await Client.PostAsJsonAsync(
            "/api/v1/public/auth/refresh",
            new { refreshToken = auth.RefreshToken })).Content.ReadFromJsonAsync<AuthPayload>();

        // Replaying the old token must invalidate the chain, including the currently valid token the
        // legitimate client is holding. Both parties are forced to sign in again, which is the point:
        // we cannot tell which one is the attacker.
        await Client.PostAsJsonAsync("/api/v1/public/auth/refresh", new { refreshToken = auth.RefreshToken });

        var afterReuse = await Client.PostAsJsonAsync(
            "/api/v1/public/auth/refresh",
            new { refreshToken = rotated!.RefreshToken });

        afterReuse.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Refresh_RejectsAnUnknownToken()
    {
        var response = await Client.PostAsJsonAsync(
            "/api/v1/public/auth/refresh",
            new { refreshToken = "this-was-never-issued" });

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Logout_RevokesTheRefreshToken()
    {
        var auth = await RegisterSellerAsync("logout@example.com");

        var logout = await Client.PostAsJsonAsync(
            "/api/v1/public/auth/logout",
            new { refreshToken = auth.RefreshToken });
        logout.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var afterLogout = await Client.PostAsJsonAsync(
            "/api/v1/public/auth/refresh",
            new { refreshToken = auth.RefreshToken });

        afterLogout.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Logout_IsIdempotent()
    {
        var auth = await RegisterSellerAsync("twice@example.com");

        var first = await Client.PostAsJsonAsync("/api/v1/public/auth/logout", new { refreshToken = auth.RefreshToken });
        var second = await Client.PostAsJsonAsync("/api/v1/public/auth/logout", new { refreshToken = auth.RefreshToken });
        var unknown = await Client.PostAsJsonAsync("/api/v1/public/auth/logout", new { refreshToken = "never-issued" });

        // A browser retrying logout, or logging out of an already-dead session, must not see an error.
        first.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        second.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        unknown.StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }
}
