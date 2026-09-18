using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace DmOrder.Api.IntegrationTests.Auth;

/// <summary>
/// The main suite raises the rate limits so it does not throttle itself (TestServer puts every request
/// in one partition). This host lowers them instead, so the limiter is still actually verified.
///
/// It never writes to the database — only rejected and failed login attempts — so it does not need the
/// shared fixture or a reset.
/// </summary>
public sealed class RateLimitedApiFactory : WebApplicationFactory<Program>
{
    public const int PermitLimit = 3;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        var connectionString =
            Environment.GetEnvironmentVariable("TEST_DATABASE_CONNECTION_STRING")
            ?? "Host=localhost;Port=5432;Database=dmorder_test;Username=postgres;Password=postgres";

        Environment.SetEnvironmentVariable("DATABASE_CONNECTION_STRING", connectionString);

        builder.ConfigureAppConfiguration(configuration =>
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Default"] = connectionString,
                ["Jwt:Secret"] = "integration-tests-signing-key-not-used-anywhere-else",
                ["Jwt:Issuer"] = "dmorder-api-tests",
                ["Jwt:Audience"] = "dmorder-web-tests",
                ["PlatformBranding:ProjectName"] = "DM Order",
                ["PlatformBranding:ProjectShortName"] = "DMO",
                ["RateLimiting:AuthPermitLimit"] = PermitLimit.ToString(),
                ["RateLimiting:AuthWindowSeconds"] = "60",
            }));
    }
}

public class RateLimitingTests
{
    [Fact]
    public async Task AuthEndpointsRejectOnceTheLimitIsReached()
    {
        using var factory = new RateLimitedApiFactory();
        using var client = factory.CreateClient();

        var statuses = new List<HttpStatusCode>();

        for (var attempt = 0; attempt < RateLimitedApiFactory.PermitLimit + 2; attempt++)
        {
            var response = await client.PostAsJsonAsync("/api/v1/public/auth/login", new
            {
                email = $"nobody{attempt}@example.com",
                password = "whatever-it-does-not-matter",
            });

            statuses.Add(response.StatusCode);
        }

        // The first requests get through and fail on credentials; the rest are turned away before the
        // handler runs. Without this, the login endpoint is an unlimited password-guessing oracle.
        statuses.Take(RateLimitedApiFactory.PermitLimit)
            .ShouldAllBe(status => status == HttpStatusCode.Unauthorized);

        statuses.Skip(RateLimitedApiFactory.PermitLimit)
            .ShouldAllBe(status => status == HttpStatusCode.TooManyRequests);
    }
}
