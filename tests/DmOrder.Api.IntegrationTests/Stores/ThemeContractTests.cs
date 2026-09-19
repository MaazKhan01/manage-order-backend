using System.Net;
using System.Net.Http.Json;

namespace DmOrder.Api.IntegrationTests.Stores;

/// <summary>
/// The theme contract must be symmetric: whatever the API returns for an enum must be accepted back.
/// It was not, and the failure surfaced as a 500 rather than anything a client could act on.
/// </summary>
public class ThemeContractTests(ApiFactory factory) : IntegrationTestBase(factory)
{
    [Fact]
    public async Task AcceptsEnumNamesExactlyAsTheApiReturnsThem()
    {
        var auth = await RegisterSellerAsync("enum@example.com");
        using var client = CreateAuthenticatedClient(auth.AccessToken);
        await StoreTests.CreateStoreAsync(client, "enum-store");

        var current = await client.GetFromJsonAsync<StorePayload>("/api/v1/seller/store");
        current!.Theme.FontChoice.ShouldBe("Sans");

        // Echo back the shape the API just handed us, with one value changed.
        var response = await client.PutAsJsonAsync("/api/v1/seller/store/theme", new
        {
            primaryColor = current.Theme.PrimaryColor,
            accentColor = current.Theme.AccentColor,
            backgroundColor = current.Theme.BackgroundColor,
            fontChoice = "Rounded",
            buttonStyle = current.Theme.ButtonStyle,
            layoutVariant = "Showcase",
        });

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var updated = await response.Content.ReadFromJsonAsync<StorePayload>();
        updated!.Theme.FontChoice.ShouldBe("Rounded");
        updated.Theme.LayoutVariant.ShouldBe("Showcase");
    }

    [Fact]
    public async Task RejectsAnUnknownEnumNameWithoutA500()
    {
        var auth = await RegisterSellerAsync("badenum@example.com");
        using var client = CreateAuthenticatedClient(auth.AccessToken);
        await StoreTests.CreateStoreAsync(client, "badenum-store");

        var response = await client.PutAsJsonAsync("/api/v1/seller/store/theme", new
        {
            primaryColor = "#111827",
            accentColor = "#0F766E",
            backgroundColor = "#FFFFFF",
            fontChoice = "Comic",
            buttonStyle = "Rounded",
            layoutVariant = "Grid",
        });

        // A body the binder cannot read is the client's mistake. It must not read as a server fault.
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task MalformedJsonReturns400NotAServerError()
    {
        var auth = await RegisterSellerAsync("malformed@example.com");
        using var client = CreateAuthenticatedClient(auth.AccessToken);
        await StoreTests.CreateStoreAsync(client, "malformed-store");

        using var content = new StringContent(
            "{ not json at all",
            System.Text.Encoding.UTF8,
            "application/json");

        var response = await client.PutAsync("/api/v1/seller/store/theme", content);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        // And it must not echo the parser's message, which quotes the payload back.
        var body = await response.Content.ReadAsStringAsync();
        body.ShouldNotContain("not json at all");
    }
}
