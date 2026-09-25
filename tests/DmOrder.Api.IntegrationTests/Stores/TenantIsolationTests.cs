using System.Net;
using System.Net.Http.Json;

namespace DmOrder.Api.IntegrationTests.Stores;

/// <summary>
/// The release gate for multi-tenancy.
///
/// Seller A must never read or write anything belonging to Seller B. A new tenant-owned resource is
/// not finished until it has a case in here.
/// </summary>
public class TenantIsolationTests(ApiFactory factory) : IntegrationTestBase(factory)
{
    private async Task<(HttpClient Client, StorePayload Store)> CreateSellerWithStoreAsync(
        string email,
        string slug)
    {
        var auth = await RegisterSellerAsync(email);
        var client = CreateAuthenticatedClient(auth.AccessToken);
        var store = await StoreTests.CreateStoreAsync(client, slug);
        return (client, store);
    }

    [Fact]
    public async Task SellerOnlyEverSeesTheirOwnStore()
    {
        var (clientA, storeA) = await CreateSellerWithStoreAsync("tenant-a@example.com", "store-alpha");
        var (clientB, storeB) = await CreateSellerWithStoreAsync("tenant-b@example.com", "store-beta");

        using var _ = clientA;
        using var __ = clientB;

        var seenByA = await clientA.GetFromJsonAsync<StorePayload>("/api/v1/seller/store");
        var seenByB = await clientB.GetFromJsonAsync<StorePayload>("/api/v1/seller/store");

        // The endpoint takes no id at all, so there is nothing to tamper with — but this pins the
        // behaviour in case someone later "helpfully" adds a storeId parameter.
        seenByA!.Id.ShouldBe(storeA.Id);
        seenByB!.Id.ShouldBe(storeB.Id);
        seenByA.Id.ShouldNotBe(seenByB.Id);
    }

    [Fact]
    public async Task SellerAsEditsNeverTouchSellerBsStore()
    {
        var (clientA, _) = await CreateSellerWithStoreAsync("edit-a@example.com", "edit-alpha");
        var (clientB, storeB) = await CreateSellerWithStoreAsync("edit-b@example.com", "edit-beta");

        using var _1 = clientA;
        using var _2 = clientB;

        await clientA.PutAsJsonAsync("/api/v1/seller/store", new
        {
            name = "Renamed By A",
            contactPhone = "+92 300 1111111",
        });

        var bAfter = await clientB.GetFromJsonAsync<StorePayload>("/api/v1/seller/store");

        bAfter!.Id.ShouldBe(storeB.Id);
        bAfter.Name.ShouldNotBe("Renamed By A");
    }

    [Fact]
    public async Task SellerCannotAttachAnotherSellersImageToTheirStore()
    {
        var (clientA, _) = await CreateSellerWithStoreAsync("media-a@example.com", "media-alpha");
        var (clientB, _) = await CreateSellerWithStoreAsync("media-b@example.com", "media-beta");

        using var _1 = clientA;
        using var _2 = clientB;

        var uploaded = await UploadPngAsync(clientB, "StoreLogo");

        // A owns neither the media row nor the file. Accepting this would let A read B's upload
        // through A's own storefront.
        var response = await clientA.PutAsJsonAsync("/api/v1/seller/store/logo", new { mediaId = uploaded.Id });

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task PublishingIsScopedToTheCallersOwnStore()
    {
        var (clientA, _) = await CreateSellerWithStoreAsync("pub-a@example.com", "pub-alpha");
        var (clientB, _) = await CreateSellerWithStoreAsync("pub-b@example.com", "pub-beta");

        using var _1 = clientA;
        using var _2 = clientB;

        await StoreTests.SetContactAsync(clientA, "Alpha");
        await clientA.PostAsync("/api/v1/seller/store/publish", null);

        var alpha = await Client.GetAsync("/api/v1/public/stores/pub-alpha");
        var beta = await Client.GetAsync("/api/v1/public/stores/pub-beta");

        alpha.StatusCode.ShouldBe(HttpStatusCode.OK);
        beta.StatusCode.ShouldBe(HttpStatusCode.NotFound, "B never published, and A cannot publish for them");
    }

    [Fact]
    public async Task SlugChangeCannotStealAnotherSellersAddress()
    {
        var (clientA, _) = await CreateSellerWithStoreAsync("slug-a2@example.com", "wanted-name");
        var (clientB, _) = await CreateSellerWithStoreAsync("slug-b2@example.com", "other-name");

        using var _1 = clientA;
        using var _2 = clientB;

        var response = await clientB.PutAsJsonAsync("/api/v1/seller/store/slug", new { slug = "wanted-name" });

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task UploadedMediaBelongsToTheUploadersStore()
    {
        var (clientA, storeA) = await CreateSellerWithStoreAsync("own-a@example.com", "own-alpha");
        using var _ = clientA;

        var uploaded = await UploadPngAsync(clientA, "StoreLogo");

        // The owner can attach their own image.
        var attach = await clientA.PutAsJsonAsync("/api/v1/seller/store/logo", new { mediaId = uploaded.Id });
        attach.StatusCode.ShouldBe(HttpStatusCode.OK);

        var store = await attach.Content.ReadFromJsonAsync<StorePayload>();
        store!.Id.ShouldBe(storeA.Id);
        store.LogoUrl.ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task RejectsANonImageUpload()
    {
        var (client, _) = await CreateSellerWithStoreAsync("upload@example.com", "upload-store");
        using var _1 = client;

        using var content = new MultipartFormDataContent();
        var payload = new ByteArrayContent("<script>alert(1)</script>"u8.ToArray());
        payload.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/png");
        content.Add(payload, "file", "totally-an-image.png");
        content.Add(new StringContent("StoreLogo"), "purpose");

        var response = await client.PostAsync("/api/v1/seller/media", content);

        // The declared content type and the extension both claim PNG. Only the bytes are believed.
        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
    }

    private static async Task<UploadedMediaPayload> UploadPngAsync(HttpClient client, string purpose)
    {
        using var content = new MultipartFormDataContent();
        var payload = new ByteArrayContent(MinimalPng);
        payload.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/png");
        content.Add(payload, "file", "logo.png");
        content.Add(new StringContent(purpose), "purpose");

        var response = await client.PostAsync("/api/v1/seller/media", content);
        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<UploadedMediaPayload>())!;
    }

    /// <summary>A real 1x1 PNG, so magic-byte validation sees an actual image.</summary>
    private static readonly byte[] MinimalPng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");
}

public sealed record UploadedMediaPayload(Guid Id, string Url, string ContentType, long SizeBytes);
