using System.Net;
using System.Net.Http.Json;
using DmOrder.Api.IntegrationTests.Stores;

namespace DmOrder.Api.IntegrationTests.Catalogue;

/// <summary>
/// Tenant isolation for the catalogue. Every resource Phase 4 introduced gets a case here, because a
/// resource without one is a resource nobody has checked.
/// </summary>
public class CatalogueIsolationTests(ApiFactory factory) : IntegrationTestBase(factory)
{
    private async Task<HttpClient> CreateSellerWithStoreAsync(string email, string slug)
    {
        var auth = await RegisterSellerAsync(email);
        var client = CreateAuthenticatedClient(auth.AccessToken);
        await StoreTests.CreateStoreAsync(client, slug);
        return client;
    }

    [Fact]
    public async Task SellerOnlySeesTheirOwnProducts()
    {
        using var alpha = await CreateSellerWithStoreAsync("iso-a@example.com", "iso-alpha");
        using var beta = await CreateSellerWithStoreAsync("iso-b@example.com", "iso-beta");

        await CatalogueTests.CreateProductAsync(alpha, "Alpha Cake", null);
        await CatalogueTests.CreateProductAsync(beta, "Beta Bouquet", null);

        var alphaList = await alpha.GetFromJsonAsync<PagedPayload<ProductListPayload>>("/api/v1/seller/products");
        var betaList = await beta.GetFromJsonAsync<PagedPayload<ProductListPayload>>("/api/v1/seller/products");

        alphaList!.Items.Select(p => p.Name).ShouldBe(["Alpha Cake"]);
        betaList!.Items.Select(p => p.Name).ShouldBe(["Beta Bouquet"]);
    }

    [Fact]
    public async Task CannotReadAnotherSellersProductById()
    {
        using var alpha = await CreateSellerWithStoreAsync("read-a@example.com", "read-alpha");
        using var beta = await CreateSellerWithStoreAsync("read-b@example.com", "read-beta");

        var betaProduct = await CatalogueTests.CreateProductAsync(beta, "Beta Cake", null);

        var response = await alpha.GetAsync($"/api/v1/seller/products/{betaProduct.Id}");

        // 404 rather than 403: a 403 would confirm the id exists.
        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task CannotEditAnotherSellersProduct()
    {
        using var alpha = await CreateSellerWithStoreAsync("edit-a@example.com", "edit-alpha2");
        using var beta = await CreateSellerWithStoreAsync("edit-b@example.com", "edit-beta2");

        var betaProduct = await CatalogueTests.CreateProductAsync(beta, "Beta Cake", null);

        var response = await alpha.PutAsJsonAsync($"/api/v1/seller/products/{betaProduct.Id}", new
        {
            name = "Hijacked",
            slug = "hijacked",
            price = (decimal?)null,
            priceIsFrom = false,
            isActive = true,
            acceptsCustomOrder = true,
        });

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        var untouched = await beta.GetFromJsonAsync<ProductDetailPayload>($"/api/v1/seller/products/{betaProduct.Id}");
        untouched!.Name.ShouldBe("Beta Cake");
    }

    [Fact]
    public async Task CannotDeleteAnotherSellersProduct()
    {
        using var alpha = await CreateSellerWithStoreAsync("del-a@example.com", "del-alpha");
        using var beta = await CreateSellerWithStoreAsync("del-b@example.com", "del-beta");

        var betaProduct = await CatalogueTests.CreateProductAsync(beta, "Beta Cake", null);

        (await alpha.DeleteAsync($"/api/v1/seller/products/{betaProduct.Id}"))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);

        (await beta.GetAsync($"/api/v1/seller/products/{betaProduct.Id}"))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task CannotFileAProductUnderAnotherSellersCategory()
    {
        using var alpha = await CreateSellerWithStoreAsync("file-a@example.com", "file-alpha");
        using var beta = await CreateSellerWithStoreAsync("file-b@example.com", "file-beta");

        var betaCategory = await CatalogueTests.CreateCategoryAsync(beta, "Beta Cakes");

        var response = await alpha.PostAsJsonAsync("/api/v1/seller/products", new
        {
            name = "Alpha Cake",
            categoryId = betaCategory.Id,
        });

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task CannotEditOrDeleteAnotherSellersCategory()
    {
        using var alpha = await CreateSellerWithStoreAsync("cat-a@example.com", "cat-alpha");
        using var beta = await CreateSellerWithStoreAsync("cat-b@example.com", "cat-beta");

        var betaCategory = await CatalogueTests.CreateCategoryAsync(beta, "Beta Cakes");

        var edit = await alpha.PutAsJsonAsync($"/api/v1/seller/categories/{betaCategory.Id}", new
        {
            name = "Hijacked",
            slug = "hijacked",
            isActive = true,
        });

        var delete = await alpha.DeleteAsync($"/api/v1/seller/categories/{betaCategory.Id}");

        edit.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        delete.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task CannotReorderAnotherSellersProducts()
    {
        using var alpha = await CreateSellerWithStoreAsync("ro-a@example.com", "ro-alpha");
        using var beta = await CreateSellerWithStoreAsync("ro-b@example.com", "ro-beta");

        var betaProduct = await CatalogueTests.CreateProductAsync(beta, "Beta Cake", null);

        var response = await alpha.PostAsJsonAsync("/api/v1/seller/products/reorder", new
        {
            idsInOrder = new[] { betaProduct.Id },
        });

        // The reorder is scoped to the caller's own rows, so a foreign id is simply not found — it
        // cannot be used to probe for or shuffle someone else's catalogue.
        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task CannotAttachAnotherSellersImageToAProduct()
    {
        using var alpha = await CreateSellerWithStoreAsync("img-a@example.com", "img-alpha");
        using var beta = await CreateSellerWithStoreAsync("img-b@example.com", "img-beta");

        var alphaProduct = await CatalogueTests.CreateProductAsync(alpha, "Alpha Cake", null);
        var betaImage = await UploadPngAsync(beta);

        var response = await alpha.PostAsJsonAsync(
            $"/api/v1/seller/products/{alphaProduct.Id}/images",
            new { mediaId = betaImage.Id });

        // Accepting this would let Alpha serve Beta's upload from Alpha's own storefront.
        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task OwnerCanAttachTheirOwnImage()
    {
        using var client = await CreateSellerWithStoreAsync("own-img@example.com", "own-img-store");

        var product = await CatalogueTests.CreateProductAsync(client, "Cake", null);
        var image = await UploadPngAsync(client);

        var response = await client.PostAsJsonAsync(
            $"/api/v1/seller/products/{product.Id}/images",
            new { mediaId = image.Id, altText = "Front view" });

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var detail = await response.Content.ReadFromJsonAsync<ProductDetailPayload>();
        detail!.Images.Count.ShouldBe(1);
        detail.Images[0].AltText.ShouldBe("Front view");
        detail.Images[0].Url.ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task CatalogueEndpointsRejectAnonymousCallers()
    {
        (await Client.GetAsync("/api/v1/seller/products")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await Client.GetAsync("/api/v1/seller/categories")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    private static async Task<UploadedMediaPayload> UploadPngAsync(HttpClient client)
    {
        using var content = new MultipartFormDataContent();
        var payload = new ByteArrayContent(MinimalPng);
        payload.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/png");
        content.Add(payload, "file", "product.png");
        content.Add(new StringContent("ProductImage"), "purpose");

        var response = await client.PostAsync("/api/v1/seller/media", content);
        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<UploadedMediaPayload>())!;
    }

    private static readonly byte[] MinimalPng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");
}
