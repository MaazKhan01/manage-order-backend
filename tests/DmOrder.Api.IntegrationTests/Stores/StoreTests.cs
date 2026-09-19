using System.Net;
using System.Net.Http.Json;

namespace DmOrder.Api.IntegrationTests.Stores;

public class StoreTests(ApiFactory factory) : IntegrationTestBase(factory)
{
    [Fact]
    public async Task SellerWithoutAStoreGets204()
    {
        var auth = await RegisterSellerAsync("nostore@example.com");
        using var client = CreateAuthenticatedClient(auth.AccessToken);

        var response = await client.GetAsync("/api/v1/seller/store");

        // 204, not 404: having no store yet is a normal state for a new seller, and the frontend
        // routes them to setup on it.
        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task CreatesAStoreWithADefaultTheme()
    {
        var auth = await RegisterSellerAsync("create@example.com");
        using var client = CreateAuthenticatedClient(auth.AccessToken);

        var response = await client.PostAsJsonAsync("/api/v1/seller/store", new
        {
            name = "Sarah's Cakes",
            slug = "sarah-cakes",
            currency = "PKR",
        });

        response.StatusCode.ShouldBe(HttpStatusCode.Created);

        var store = await response.Content.ReadFromJsonAsync<StorePayload>();
        store!.Slug.ShouldBe("sarah-cakes");
        store.IsPublished.ShouldBeFalse();
        store.IsActive.ShouldBeTrue();
        store.Theme.LayoutVariant.ShouldBe("Grid");
    }

    [Fact]
    public async Task ASellerCanOnlyHaveOneStore()
    {
        var auth = await RegisterSellerAsync("onestore@example.com");
        using var client = CreateAuthenticatedClient(auth.AccessToken);

        await CreateStoreAsync(client, "first-store");

        var second = await client.PostAsJsonAsync("/api/v1/seller/store", new
        {
            name = "Second",
            slug = "second-store",
            currency = "PKR",
        });

        second.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task SlugsAreUniqueAcrossSellers()
    {
        var first = await RegisterSellerAsync("slug-a@example.com");
        using var firstClient = CreateAuthenticatedClient(first.AccessToken);
        await CreateStoreAsync(firstClient, "taken-slug");

        var second = await RegisterSellerAsync("slug-b@example.com");
        using var secondClient = CreateAuthenticatedClient(second.AccessToken);

        var response = await secondClient.PostAsJsonAsync("/api/v1/seller/store", new
        {
            name = "Other",
            slug = "taken-slug",
            currency = "PKR",
        });

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Theory]
    [InlineData("admin")]
    [InlineData("api")]
    [InlineData("dashboard")]
    [InlineData("login")]
    public async Task ReservedSlugsAreRejected(string slug)
    {
        var auth = await RegisterSellerAsync($"reserved-{slug}@example.com");
        using var client = CreateAuthenticatedClient(auth.AccessToken);

        var response = await client.PostAsJsonAsync("/api/v1/seller/store", new
        {
            name = "Trying it on",
            slug,
            currency = "PKR",
        });

        // These shadow platform routes. A store on any of them would break the site.
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task CreatingAStoreMakesTheStoreClaimAvailableOnTheNextToken()
    {
        var auth = await RegisterSellerAsync("claim@example.com");
        using var client = CreateAuthenticatedClient(auth.AccessToken);
        await CreateStoreAsync(client, "claim-store");

        // The token issued at registration had no store. Refreshing re-reads the user, so the claim
        // appears without the seller having to sign out and back in.
        var refreshed = await Client.PostAsJsonAsync(
            "/api/v1/public/auth/refresh",
            new { refreshToken = auth.RefreshToken });

        var payload = await refreshed.Content.ReadFromJsonAsync<AuthPayload>();
        payload!.User.StoreId.ShouldNotBeNull();
        payload.User.HasStore.ShouldBeTrue();
    }

    [Fact]
    public async Task CannotPublishWithoutAContactChannel()
    {
        var auth = await RegisterSellerAsync("nopublish@example.com");
        using var client = CreateAuthenticatedClient(auth.AccessToken);
        await CreateStoreAsync(client, "no-contact");

        var response = await client.PostAsync("/api/v1/seller/store/publish", null);

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task PublishingMakesTheStorefrontVisibleAnonymously()
    {
        var auth = await RegisterSellerAsync("publish@example.com");
        using var client = CreateAuthenticatedClient(auth.AccessToken);
        await CreateStoreAsync(client, "bloom-florist");
        await SetContactAsync(client, "Bloom Florist");

        var beforePublish = await Client.GetAsync("/api/v1/public/stores/bloom-florist");
        beforePublish.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        var publish = await client.PostAsync("/api/v1/seller/store/publish", null);
        publish.StatusCode.ShouldBe(HttpStatusCode.OK);

        var afterPublish = await Client.GetAsync("/api/v1/public/stores/bloom-florist");
        afterPublish.StatusCode.ShouldBe(HttpStatusCode.OK);

        var storefront = await afterPublish.Content.ReadFromJsonAsync<PublicStorePayload>();
        storefront!.Name.ShouldBe("Bloom Florist");
    }

    [Fact]
    public async Task AnUnpublishedStorefrontIsIndistinguishableFromOneThatDoesNotExist()
    {
        var auth = await RegisterSellerAsync("hidden@example.com");
        using var client = CreateAuthenticatedClient(auth.AccessToken);
        await CreateStoreAsync(client, "still-hidden");

        var hidden = await Client.GetAsync("/api/v1/public/stores/still-hidden");
        var nonexistent = await Client.GetAsync("/api/v1/public/stores/never-existed");

        // Saying "exists but hidden" would leak that a seller is preparing something.
        hidden.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        nonexistent.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task UnpublishingTakesTheStorefrontOffline()
    {
        var auth = await RegisterSellerAsync("offline@example.com");
        using var client = CreateAuthenticatedClient(auth.AccessToken);
        await CreateStoreAsync(client, "going-offline");
        await SetContactAsync(client, "Going Offline");
        await client.PostAsync("/api/v1/seller/store/publish", null);

        await client.PostAsync("/api/v1/seller/store/unpublish", null);

        var response = await Client.GetAsync("/api/v1/public/stores/going-offline");
        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task SlugAvailabilityReportsReservedAndTakenNames()
    {
        var auth = await RegisterSellerAsync("avail@example.com");
        using var client = CreateAuthenticatedClient(auth.AccessToken);
        await CreateStoreAsync(client, "already-mine");

        var free = await Client.GetFromJsonAsync<SlugAvailabilityPayload>(
            "/api/v1/public/stores/slug-available?slug=totally-free");
        var taken = await Client.GetFromJsonAsync<SlugAvailabilityPayload>(
            "/api/v1/public/stores/slug-available?slug=already-mine");
        var reserved = await Client.GetFromJsonAsync<SlugAvailabilityPayload>(
            "/api/v1/public/stores/slug-available?slug=admin");
        var malformed = await Client.GetFromJsonAsync<SlugAvailabilityPayload>(
            "/api/v1/public/stores/slug-available?slug=Not Valid");

        free!.IsAvailable.ShouldBeTrue();
        taken!.IsAvailable.ShouldBeFalse();
        reserved!.IsAvailable.ShouldBeFalse();
        malformed!.IsAvailable.ShouldBeFalse();
    }

    [Fact]
    public async Task RejectsAThemeColourThatIsNotHex()
    {
        var auth = await RegisterSellerAsync("theme@example.com");
        using var client = CreateAuthenticatedClient(auth.AccessToken);
        await CreateStoreAsync(client, "theme-store");

        var response = await client.PutAsJsonAsync("/api/v1/seller/store/theme", new
        {
            primaryColor = "red; } body { display: none",
            accentColor = "#0F766E",
            backgroundColor = "#FFFFFF",
            fontChoice = "Sans",
            buttonStyle = "Rounded",
            layoutVariant = "Grid",
        });

        // Theme colours reach a stylesheet on a public page.
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task RejectsAJavascriptUrlAsASocialLink()
    {
        var auth = await RegisterSellerAsync("xss@example.com");
        using var client = CreateAuthenticatedClient(auth.AccessToken);
        await CreateStoreAsync(client, "xss-store");

        var response = await client.PutAsJsonAsync("/api/v1/seller/store", new
        {
            name = "XSS Store",
            contactPhone = "0300 1234567",
            instagramUrl = "javascript:alert(document.cookie)",
        });

        // Social links are rendered as anchors customers click.
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task StoreEndpointsRejectAnonymousCallers()
    {
        var response = await Client.GetAsync("/api/v1/seller/store");

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    internal static async Task<StorePayload> CreateStoreAsync(HttpClient client, string slug)
    {
        var response = await client.PostAsJsonAsync("/api/v1/seller/store", new
        {
            name = slug,
            slug,
            currency = "PKR",
        });

        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<StorePayload>())!;
    }

    internal static async Task SetContactAsync(HttpClient client, string name)
    {
        var response = await client.PutAsJsonAsync("/api/v1/seller/store", new
        {
            name,
            contactPhone = "0300 1234567",
        });

        response.EnsureSuccessStatusCode();
    }
}

public sealed record StorePayload(
    Guid Id,
    string Name,
    string Slug,
    bool IsPublished,
    bool IsActive,
    bool CanPublish,
    string? LogoUrl,
    ThemePayload Theme);

public sealed record ThemePayload(
    string PrimaryColor,
    string AccentColor,
    string BackgroundColor,
    string FontChoice,
    string ButtonStyle,
    string LayoutVariant,
    string? BackgroundUrl);

public sealed record PublicStorePayload(string Name, string Slug, string? Description, ThemePayload Theme);

public sealed record SlugAvailabilityPayload(string Slug, bool IsAvailable, string? Reason);
