using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace DmOrder.Api.IntegrationTests;

[Collection(nameof(ApiCollection))]
public abstract class IntegrationTestBase(ApiFactory factory) : IAsyncLifetime
{
    protected ApiFactory Factory { get; } = factory;

    protected HttpClient Client { get; private set; } = null!;

    public Task InitializeAsync()
    {
        Client = Factory.CreateClient();
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        Client.Dispose();
        await Factory.ResetDatabaseAsync();
    }

    /// <summary>Registers a seller and returns the API's auth response.</summary>
    protected async Task<AuthPayload> RegisterSellerAsync(
        string email,
        string password = "a-long-enough-password",
        string displayName = "Test Seller")
    {
        var response = await Client.PostAsJsonAsync(
            "/api/v1/public/auth/register",
            new { email, password, displayName });

        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<AuthPayload>())!;
    }

    /// <summary>A client authenticated as the given seller, as the BFF would call the API.</summary>
    protected HttpClient CreateAuthenticatedClient(string accessToken)
    {
        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return client;
    }
}

public sealed record AuthPayload(
    CurrentUserPayload User,
    string AccessToken,
    DateTimeOffset AccessTokenExpiresAt,
    string RefreshToken,
    DateTimeOffset RefreshTokenExpiresAt);

public sealed record CurrentUserPayload(
    Guid Id,
    string Email,
    string DisplayName,
    string[] Roles,
    Guid? StoreId,
    bool HasStore);

/// <summary>
/// The API's one list envelope, mirrored once for every suite. A second copy in a feature namespace
/// is how two tests end up asserting against two different shapes of the same response.
/// </summary>
public sealed record PagedPayload<T>(
    List<T> Items,
    int Page,
    int PageSize,
    int TotalCount,
    int TotalPages,
    bool HasNextPage);
