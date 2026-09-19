using System.Net;
using System.Net.Http.Json;
using DmOrder.Api.IntegrationTests.Stores;

namespace DmOrder.Api.IntegrationTests.Catalogue;

public class CatalogueTests(ApiFactory factory) : IntegrationTestBase(factory)
{
    private async Task<HttpClient> CreateSellerWithStoreAsync(string email, string slug, bool publish = false)
    {
        var auth = await RegisterSellerAsync(email);
        var client = CreateAuthenticatedClient(auth.AccessToken);
        await StoreTests.CreateStoreAsync(client, slug);

        if (publish)
        {
            await StoreTests.SetContactAsync(client, slug);
            (await client.PostAsync("/api/v1/seller/store/publish", null)).EnsureSuccessStatusCode();
        }

        return client;
    }

    [Fact]
    public async Task CreatesACategoryAndDerivesItsWebAddress()
    {
        using var client = await CreateSellerWithStoreAsync("cat@example.com", "cat-store");

        var response = await client.PostAsJsonAsync("/api/v1/seller/categories", new { name = "Birthday Cakes" });

        response.StatusCode.ShouldBe(HttpStatusCode.Created);

        var category = await response.Content.ReadFromJsonAsync<CategoryPayload>();
        category!.Slug.ShouldBe("birthday-cakes");
        category.ProductCount.ShouldBe(0);
    }

    [Fact]
    public async Task CategoryAddressesAreUniquePerStoreButNotGlobally()
    {
        using var first = await CreateSellerWithStoreAsync("uniq-a@example.com", "uniq-a");
        using var second = await CreateSellerWithStoreAsync("uniq-b@example.com", "uniq-b");

        (await first.PostAsJsonAsync("/api/v1/seller/categories", new { name = "Cakes" }))
            .StatusCode.ShouldBe(HttpStatusCode.Created);

        // Two different sellers may both have "cakes" — uniqueness is per store.
        (await second.PostAsJsonAsync("/api/v1/seller/categories", new { name = "Cakes" }))
            .StatusCode.ShouldBe(HttpStatusCode.Created);

        // But the same seller may not have it twice.
        (await first.PostAsJsonAsync("/api/v1/seller/categories", new { name = "Cakes" }))
            .StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task DeletingACategoryKeepsItsProducts()
    {
        using var client = await CreateSellerWithStoreAsync("keep@example.com", "keep-store");

        var category = await CreateCategoryAsync(client, "Cakes");
        var product = await CreateProductAsync(client, "Chocolate Cake", category.Id);

        (await client.DeleteAsync($"/api/v1/seller/categories/{category.Id}"))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);

        // A seller tidying up their categories must not lose products as a side effect.
        var after = await client.GetFromJsonAsync<ProductDetailPayload>($"/api/v1/seller/products/{product.Id}");
        after!.CategoryId.ShouldBeNull();
        after.Name.ShouldBe("Chocolate Cake");
    }

    [Fact]
    public async Task ReusesTheAddressOfADeletedCategory()
    {
        using var client = await CreateSellerWithStoreAsync("reuse@example.com", "reuse-store");

        var category = await CreateCategoryAsync(client, "Cakes");
        await client.DeleteAsync($"/api/v1/seller/categories/{category.Id}");

        // Deleting by mistake must not permanently burn the name.
        (await client.PostAsJsonAsync("/api/v1/seller/categories", new { name = "Cakes" }))
            .StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    [Fact]
    public async Task ProductWithoutAPriceIsAllowed()
    {
        using var client = await CreateSellerWithStoreAsync("noprice@example.com", "noprice-store");

        var product = await CreateProductAsync(client, "Custom Bridal Lehenga", null);

        var response = await client.PutAsJsonAsync($"/api/v1/seller/products/{product.Id}", new
        {
            name = "Custom Bridal Lehenga",
            slug = product.Slug,
            price = (decimal?)null,
            priceIsFrom = false,
            isActive = true,
            acceptsCustomOrder = true,
        });

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task RejectsPriceStartsFromWithoutAPrice()
    {
        using var client = await CreateSellerWithStoreAsync("from@example.com", "from-store");
        var product = await CreateProductAsync(client, "Custom Cake", null);

        var response = await client.PutAsJsonAsync($"/api/v1/seller/products/{product.Id}", new
        {
            name = "Custom Cake",
            slug = product.Slug,
            price = (decimal?)null,
            priceIsFrom = true,
            isActive = true,
            acceptsCustomOrder = true,
        });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task ListIsPaginatedAndSearchable()
    {
        using var client = await CreateSellerWithStoreAsync("list@example.com", "list-store");

        await CreateProductAsync(client, "Chocolate Cake", null);
        await CreateProductAsync(client, "Vanilla Cake", null);
        await CreateProductAsync(client, "Rose Bouquet", null);

        var all = await client.GetFromJsonAsync<PagedPayload<ProductListPayload>>("/api/v1/seller/products");
        all!.TotalCount.ShouldBe(3);

        var page = await client.GetFromJsonAsync<PagedPayload<ProductListPayload>>(
            "/api/v1/seller/products?page=1&pageSize=2");
        page!.Items.Count.ShouldBe(2);
        page.HasNextPage.ShouldBeTrue();

        // Case-insensitive, and not Npgsql-specific.
        var search = await client.GetFromJsonAsync<PagedPayload<ProductListPayload>>(
            "/api/v1/seller/products?search=cake");
        search!.TotalCount.ShouldBe(2);
    }

    [Fact]
    public async Task SearchTreatsWildcardsAsLiteralText()
    {
        using var client = await CreateSellerWithStoreAsync("wild@example.com", "wild-store");
        await CreateProductAsync(client, "Chocolate Cake", null);
        await CreateProductAsync(client, "50% Off Bundle", null);

        var result = await client.GetFromJsonAsync<PagedPayload<ProductListPayload>>(
            "/api/v1/seller/products?search=%25");

        // An unescaped "%" would match everything instead of the one product containing it.
        result!.TotalCount.ShouldBe(1);
        result.Items[0].Name.ShouldBe("50% Off Bundle");
    }

    [Fact]
    public async Task PageSizeIsClampedRatherThanRejected()
    {
        using var client = await CreateSellerWithStoreAsync("clamp@example.com", "clamp-store");
        await CreateProductAsync(client, "Cake", null);

        var result = await client.GetFromJsonAsync<PagedPayload<ProductListPayload>>(
            "/api/v1/seller/products?page=0&pageSize=5000");

        result!.Page.ShouldBe(1);
        result.PageSize.ShouldBe(100, "an unbounded page must not be expressible");
    }

    [Fact]
    public async Task DeletedProductDisappearsFromTheList()
    {
        using var client = await CreateSellerWithStoreAsync("del@example.com", "del-store");
        var product = await CreateProductAsync(client, "Old Cake", null);

        (await client.DeleteAsync($"/api/v1/seller/products/{product.Id}"))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var list = await client.GetFromJsonAsync<PagedPayload<ProductListPayload>>("/api/v1/seller/products");
        list!.TotalCount.ShouldBe(0);

        (await client.GetAsync($"/api/v1/seller/products/{product.Id}"))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task ReorderingSetsDisplayOrder()
    {
        using var client = await CreateSellerWithStoreAsync("order@example.com", "order-store");

        var first = await CreateProductAsync(client, "First", null);
        var second = await CreateProductAsync(client, "Second", null);
        var third = await CreateProductAsync(client, "Third", null);

        var response = await client.PostAsJsonAsync("/api/v1/seller/products/reorder", new
        {
            idsInOrder = new[] { third.Id, first.Id, second.Id },
        });

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var list = await client.GetFromJsonAsync<PagedPayload<ProductListPayload>>("/api/v1/seller/products");
        list!.Items.Select(p => p.Name).ShouldBe(["Third", "First", "Second"]);
    }

    [Fact]
    public async Task ReorderRejectsDuplicateIds()
    {
        using var client = await CreateSellerWithStoreAsync("dupe@example.com", "dupe-store");
        var product = await CreateProductAsync(client, "Cake", null);

        var response = await client.PostAsJsonAsync("/api/v1/seller/products/reorder", new
        {
            idsInOrder = new[] { product.Id, product.Id },
        });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    // --- Public catalogue ------------------------------------------------

    [Fact]
    public async Task StorefrontShowsOnlyActiveProducts()
    {
        using var client = await CreateSellerWithStoreAsync("shop@example.com", "shop-store", publish: true);

        var visible = await CreateProductAsync(client, "Visible Cake", null);
        var hidden = await CreateProductAsync(client, "Draft Cake", null);

        await client.PutAsJsonAsync($"/api/v1/seller/products/{hidden.Id}", new
        {
            name = "Draft Cake",
            slug = hidden.Slug,
            price = (decimal?)null,
            priceIsFrom = false,
            isActive = false,
            acceptsCustomOrder = true,
        });

        var catalogue = await Client.GetFromJsonAsync<PublicCataloguePayload>(
            "/api/v1/public/stores/shop-store/products");

        // A draft must not reach the client at all — hiding it in the UI would still ship it.
        catalogue!.TotalProducts.ShouldBe(1);
        catalogue.Uncategorised.Single().Name.ShouldBe("Visible Cake");
        _ = visible;
    }

    [Fact]
    public async Task StorefrontGroupsByCategoryAndSkipsEmptyOnes()
    {
        using var client = await CreateSellerWithStoreAsync("group@example.com", "group-store", publish: true);

        var cakes = await CreateCategoryAsync(client, "Cakes");
        await CreateCategoryAsync(client, "Empty Category");
        await CreateProductAsync(client, "Chocolate Cake", cakes.Id);
        await CreateProductAsync(client, "Loose Item", null);

        var catalogue = await Client.GetFromJsonAsync<PublicCataloguePayload>(
            "/api/v1/public/stores/group-store/products");

        catalogue!.Categories.Count.ShouldBe(1, "an empty category is noise on a storefront");
        catalogue.Categories[0].Name.ShouldBe("Cakes");
        catalogue.Uncategorised.Count.ShouldBe(1);
    }

    [Fact]
    public async Task StorefrontCatalogueIs404ForAnUnpublishedStore()
    {
        using var client = await CreateSellerWithStoreAsync("unpub@example.com", "unpub-store");
        await CreateProductAsync(client, "Secret Cake", null);

        var response = await Client.GetAsync("/api/v1/public/stores/unpub-store/products");

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task PublicProductDetailIs404ForAnInactiveProduct()
    {
        using var client = await CreateSellerWithStoreAsync("inactive@example.com", "inactive-store", publish: true);
        var product = await CreateProductAsync(client, "Hidden Cake", null);

        await client.PutAsJsonAsync($"/api/v1/seller/products/{product.Id}", new
        {
            name = "Hidden Cake",
            slug = product.Slug,
            price = (decimal?)null,
            priceIsFrom = false,
            isActive = false,
            acceptsCustomOrder = true,
        });

        var response = await Client.GetAsync($"/api/v1/public/stores/inactive-store/products/{product.Slug}");

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    // --- Helpers ---------------------------------------------------------

    internal static async Task<CategoryPayload> CreateCategoryAsync(HttpClient client, string name)
    {
        var response = await client.PostAsJsonAsync("/api/v1/seller/categories", new { name });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<CategoryPayload>())!;
    }

    internal static async Task<ProductDetailPayload> CreateProductAsync(
        HttpClient client,
        string name,
        Guid? categoryId)
    {
        var response = await client.PostAsJsonAsync("/api/v1/seller/products", new { name, categoryId });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ProductDetailPayload>())!;
    }
}

public sealed record CategoryPayload(Guid Id, string Name, string Slug, int DisplayOrder, bool IsActive, int ProductCount);

public sealed record ProductDetailPayload(
    Guid Id,
    string Name,
    string Slug,
    decimal? Price,
    bool PriceIsFrom,
    bool IsActive,
    Guid? CategoryId,
    List<ProductImagePayload> Images);

public sealed record ProductImagePayload(Guid Id, string Url, string? AltText, int DisplayOrder);

public sealed record ProductListPayload(Guid Id, string Name, string Slug, string? PrimaryImageUrl, int ImageCount);

public sealed record PagedPayload<T>(List<T> Items, int Page, int PageSize, int TotalCount, int TotalPages, bool HasNextPage);

public sealed record PublicCataloguePayload(
    List<PublicCategoryPayload> Categories,
    List<PublicProductPayload> Uncategorised,
    int TotalProducts);

public sealed record PublicCategoryPayload(string Slug, string Name, List<PublicProductPayload> Products);

public sealed record PublicProductPayload(string Slug, string Name, decimal? Price, string? PrimaryImageUrl);
