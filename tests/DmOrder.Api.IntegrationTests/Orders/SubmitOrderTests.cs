using System.Net;
using System.Net.Http.Json;
using DmOrder.Api.IntegrationTests.Catalogue;
using DmOrder.Api.IntegrationTests.Stores;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DmOrder.Api.IntegrationTests.Orders;

/// <summary>
/// The anonymous order path: the product's whole point, and its most hostile surface.
/// </summary>
public class SubmitOrderTests(ApiFactory factory) : IntegrationTestBase(factory)
{
    private async Task<(HttpClient Seller, string Slug)> CreatePublishedStoreAsync(string email, string slug)
    {
        var auth = await RegisterSellerAsync(email);
        var client = CreateAuthenticatedClient(auth.AccessToken);

        await StoreTests.CreateStoreAsync(client, slug);
        await StoreTests.SetContactAsync(client, slug);
        (await client.PostAsync("/api/v1/seller/store/publish", null)).EnsureSuccessStatusCode();

        return (client, slug);
    }

    private static async Task<FieldPayload> AddFieldAsync(
        HttpClient seller,
        Guid? productId,
        string label,
        string fieldType,
        bool required = false,
        string[]? options = null,
        decimal? min = null,
        decimal? max = null)
    {
        var created = await seller.PostAsJsonAsync("/api/v1/seller/custom-fields", new
        {
            productId,
            label,
            fieldType,
        });
        created.EnsureSuccessStatusCode();

        var field = (await created.Content.ReadFromJsonAsync<FieldPayload>())!;

        var updated = await seller.PutAsJsonAsync($"/api/v1/seller/custom-fields/{field.Id}", new
        {
            label,
            helpText = (string?)null,
            fieldType,
            isRequired = required,
            minValue = min,
            maxValue = max,
            maxLength = (int?)null,
            options = (options ?? []).Select(o => new { label = o, value = (string?)null }).ToArray(),
        });
        updated.EnsureSuccessStatusCode();

        return (await updated.Content.ReadFromJsonAsync<FieldPayload>())!;
    }

    [Fact]
    public async Task ABakerTakesACustomCakeOrder()
    {
        var (seller, slug) = await CreatePublishedStoreAsync("baker@example.com", "sweet-things");
        using var _ = seller;

        var product = await CatalogueTests.CreateProductAsync(seller, "Custom Birthday Cake", null);
        var flavour = await AddFieldAsync(seller, product.Id, "Flavour", "Select", true, ["Chocolate", "Vanilla"]);
        var eggless = await AddFieldAsync(seller, product.Id, "Eggless", "Boolean", true);
        var message = await AddFieldAsync(seller, product.Id, "Message on cake", "Text");
        var deliverOn = await AddFieldAsync(seller, null, "Delivery date", "Date", true);

        var response = await Client.PostAsJsonAsync($"/api/v1/public/stores/{slug}/orders", new
        {
            productSlug = product.Slug,
            quantity = 1,
            customerName = "Ayesha",
            customerPhone = "0300 1234567",
            deliveryAddress = "12 Gulberg, Lahore",
            answers = new object[]
            {
                new { fieldId = flavour.Id, value = "Chocolate" },
                new { fieldId = eggless.Id, value = "yes" },
                new { fieldId = message.Id, value = "Happy Birthday Ayesha" },
                new { fieldId = deliverOn.Id, value = "2026-12-25" },
            },
        });

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<SubmitOrderPayload>();
        result!.OrderNumber.ShouldBe(1, "the first order in a store is #1");
        result.StoreName.ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task ATailorTakesAMeasurementOrderWithTheSameMachinery()
    {
        // The same endpoint, the same validator, a completely different trade. If this needed
        // anything bespoke, the custom-field design would have failed.
        var (seller, slug) = await CreatePublishedStoreAsync("tailor@example.com", "threads-by-aisha");
        using var _ = seller;

        var product = await CatalogueTests.CreateProductAsync(seller, "Custom Bridal Lehenga", null);
        var bust = await AddFieldAsync(seller, product.Id, "Bust (inches)", "Number", true, min: 20, max: 60);
        var fabric = await AddFieldAsync(seller, product.Id, "Fabric", "Text", true);

        var response = await Client.PostAsJsonAsync($"/api/v1/public/stores/{slug}/orders", new
        {
            productSlug = product.Slug,
            quantity = 1,
            customerName = "Sana",
            customerPhone = "03009998877",
            answers = new object[]
            {
                new { fieldId = bust.Id, value = "34" },
                new { fieldId = fabric.Id, value = "Raw silk" },
            },
        });

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task MissingRequiredAnswersAreReportedPerField()
    {
        var (seller, slug) = await CreatePublishedStoreAsync("req@example.com", "req-store");
        using var _ = seller;

        var product = await CatalogueTests.CreateProductAsync(seller, "Cake", null);
        var flavour = await AddFieldAsync(seller, product.Id, "Flavour", "Select", true, ["Chocolate"]);
        var deliverOn = await AddFieldAsync(seller, product.Id, "Delivery date", "Date", true);

        var response = await Client.PostAsJsonAsync($"/api/v1/public/stores/{slug}/orders", new
        {
            productSlug = product.Slug,
            quantity = 1,
            customerName = "Ayesha",
            customerPhone = "03001234567",
            answers = Array.Empty<object>(),
        });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        // Keyed by field id so the form can mark each input, rather than making the customer guess.
        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemPayload>();
        problem!.Errors.ShouldContainKey(flavour.Id.ToString());
        problem.Errors.ShouldContainKey(deliverOn.Id.ToString());
    }

    [Fact]
    public async Task RejectsAChoiceThatIsNotOnTheSellersList()
    {
        var (seller, slug) = await CreatePublishedStoreAsync("choice@example.com", "choice-store");
        using var _ = seller;

        var product = await CatalogueTests.CreateProductAsync(seller, "Cake", null);
        var flavour = await AddFieldAsync(seller, product.Id, "Flavour", "Select", true, ["Chocolate"]);

        // The form only ever offered "Chocolate", but the submission is untrusted.
        var response = await Client.PostAsJsonAsync($"/api/v1/public/stores/{slug}/orders", new
        {
            productSlug = product.Slug,
            quantity = 1,
            customerName = "Ayesha",
            customerPhone = "03001234567",
            answers = new object[] { new { fieldId = flavour.Id, value = "Gold Leaf" } },
        });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task IgnoresAnswersToFieldsThatDoNotExist()
    {
        var (seller, slug) = await CreatePublishedStoreAsync("ghost@example.com", "ghost-store");
        using var _ = seller;

        var product = await CatalogueTests.CreateProductAsync(seller, "Cake", null);

        var response = await Client.PostAsJsonAsync($"/api/v1/public/stores/{slug}/orders", new
        {
            productSlug = product.Slug,
            quantity = 1,
            customerName = "Ayesha",
            customerPhone = "03001234567",
            answers = new object[] { new { fieldId = Guid.CreateVersion7(), value = "injected" } },
        });

        // A hand-crafted payload must not be able to add data to an order.
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task CannotOrderFromAnUnpublishedStore()
    {
        var auth = await RegisterSellerAsync("draft@example.com");
        using var seller = CreateAuthenticatedClient(auth.AccessToken);
        await StoreTests.CreateStoreAsync(seller, "draft-store");
        var product = await CatalogueTests.CreateProductAsync(seller, "Cake", null);

        var response = await Client.PostAsJsonAsync("/api/v1/public/stores/draft-store/orders", new
        {
            productSlug = product.Slug,
            quantity = 1,
            customerName = "Ayesha",
            customerPhone = "03001234567",
            answers = Array.Empty<object>(),
        });

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task CannotOrderAProductThatIsNotAcceptingOrders()
    {
        var (seller, slug) = await CreatePublishedStoreAsync("closed@example.com", "closed-store");
        using var _ = seller;

        var product = await CatalogueTests.CreateProductAsync(seller, "Display Only", null);

        await seller.PutAsJsonAsync($"/api/v1/seller/products/{product.Id}", new
        {
            name = "Display Only",
            slug = product.Slug,
            price = (decimal?)null,
            priceIsFrom = false,
            isActive = true,
            acceptsCustomOrder = false,
        });

        var response = await Client.PostAsJsonAsync($"/api/v1/public/stores/{slug}/orders", new
        {
            productSlug = product.Slug,
            quantity = 1,
            customerName = "Ayesha",
            customerPhone = "03001234567",
            answers = Array.Empty<object>(),
        });

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task OrderNumbersIncrementPerStoreIndependently()
    {
        var (sellerA, slugA) = await CreatePublishedStoreAsync("num-a@example.com", "num-alpha");
        var (sellerB, slugB) = await CreatePublishedStoreAsync("num-b@example.com", "num-beta");
        using var _1 = sellerA;
        using var _2 = sellerB;

        var productA = await CatalogueTests.CreateProductAsync(sellerA, "Cake", null);
        var productB = await CatalogueTests.CreateProductAsync(sellerB, "Bouquet", null);

        var first = await SubmitAsync(slugA, productA.Slug, "Ayesha", "03001111111");
        var second = await SubmitAsync(slugA, productA.Slug, "Bilal", "03002222222");
        var otherStore = await SubmitAsync(slugB, productB.Slug, "Chand", "03003333333");

        first.OrderNumber.ShouldBe(1);
        second.OrderNumber.ShouldBe(2);
        // Each seller counts from 1 — "#1" means something different in each store.
        otherStore.OrderNumber.ShouldBe(1);
    }

    [Fact]
    public async Task ReturningCustomerIsMatchedByPhoneRegardlessOfFormatting()
    {
        var (seller, slug) = await CreatePublishedStoreAsync("repeat@example.com", "repeat-store");
        using var _ = seller;

        var product = await CatalogueTests.CreateProductAsync(seller, "Cake", null);

        await SubmitAsync(slug, product.Slug, "Ayesha Khan", "0300 123 4567", email: "ayesha@example.com");
        var second = await SubmitAsync(slug, product.Slug, "Ayesha", "03001234567");

        second.OrderNumber.ShouldBe(2);

        // "0300 123 4567" and "03001234567" are the same person, so there must be exactly one record.
        var customers = await CountCustomersAsync(slug);
        customers.ShouldBe(1);
    }

    [Fact]
    public async Task HoneypotSubmissionsAreDiscardedButLookSuccessful()
    {
        var (seller, slug) = await CreatePublishedStoreAsync("bot@example.com", "bot-store");
        using var _ = seller;

        var product = await CatalogueTests.CreateProductAsync(seller, "Cake", null);

        var response = await Client.PostAsJsonAsync($"/api/v1/public/stores/{slug}/orders", new
        {
            productSlug = product.Slug,
            quantity = 1,
            customerName = "Bot",
            customerPhone = "03001234567",
            answers = Array.Empty<object>(),
            website = "http://spam.example",
        });

        response.StatusCode.ShouldBe(HttpStatusCode.OK, "a bot must learn nothing from the response");

        var result = await response.Content.ReadFromJsonAsync<SubmitOrderPayload>();
        result!.OrderNumber.ShouldBe(0, "but nothing was actually stored");

        (await CountCustomersAsync(slug)).ShouldBe(0);
    }

    [Theory]
    [InlineData("123")]
    [InlineData("not a phone")]
    [InlineData("")]
    public async Task RejectsAnImplausiblePhoneNumber(string phone)
    {
        var (seller, slug) = await CreatePublishedStoreAsync($"phone{phone.Length}@example.com", $"phone-{phone.Length}");
        using var _ = seller;

        var product = await CatalogueTests.CreateProductAsync(seller, "Cake", null);

        var response = await Client.PostAsJsonAsync($"/api/v1/public/stores/{slug}/orders", new
        {
            productSlug = product.Slug,
            quantity = 1,
            customerName = "Ayesha",
            customerPhone = phone,
            answers = Array.Empty<object>(),
        });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task RejectsAnAbsurdQuantity()
    {
        var (seller, slug) = await CreatePublishedStoreAsync("qty@example.com", "qty-store");
        using var _ = seller;

        var product = await CatalogueTests.CreateProductAsync(seller, "Cake", null);

        var response = await Client.PostAsJsonAsync($"/api/v1/public/stores/{slug}/orders", new
        {
            productSlug = product.Slug,
            quantity = 100_000,
            customerName = "Ayesha",
            customerPhone = "03001234567",
            answers = Array.Empty<object>(),
        });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task DeletingAQuestionLeavesExistingAnswersReadable()
    {
        var (seller, slug) = await CreatePublishedStoreAsync("snap@example.com", "snap-store");
        using var _ = seller;

        var product = await CatalogueTests.CreateProductAsync(seller, "Cake", null);
        var flavour = await AddFieldAsync(seller, product.Id, "Flavour", "Select", true, ["Chocolate"]);

        await Client.PostAsJsonAsync($"/api/v1/public/stores/{slug}/orders", new
        {
            productSlug = product.Slug,
            quantity = 1,
            customerName = "Ayesha",
            customerPhone = "03001234567",
            answers = new object[] { new { fieldId = flavour.Id, value = "Chocolate" } },
        });

        // Deleting the question must not rewrite or destroy what the customer actually said.
        (await seller.DeleteAsync($"/api/v1/seller/custom-fields/{flavour.Id}"))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);

        // A later order for the same product simply is not asked any more.
        var after = await SubmitAsync(slug, product.Slug, "Bilal", "03007777777");
        after.OrderNumber.ShouldBe(2);
    }

    [Fact]
    public async Task PublicProductDetailCarriesTheSellersQuestions()
    {
        var (seller, slug) = await CreatePublishedStoreAsync("form@example.com", "form-store");
        using var _ = seller;

        var product = await CatalogueTests.CreateProductAsync(seller, "Cake", null);
        await AddFieldAsync(seller, product.Id, "Flavour", "Select", true, ["Chocolate", "Vanilla"]);
        await AddFieldAsync(seller, null, "Delivery date", "Date", true);

        var detail = await Client.GetFromJsonAsync<PublicProductDetailPayload>(
            $"/api/v1/public/stores/{slug}/products/{product.Slug}");

        detail!.CustomFields.Count.ShouldBe(2);

        // Store-wide questions come first, so a delivery date is not buried under product detail.
        detail.CustomFields[0].Label.ShouldBe("Delivery date");
        detail.CustomFields[1].Options.ShouldBe(["Chocolate", "Vanilla"]);
    }

    // --- Helpers ---------------------------------------------------------

    private async Task<SubmitOrderPayload> SubmitAsync(
        string storeSlug,
        string productSlug,
        string name,
        string phone,
        string? email = null)
    {
        var response = await Client.PostAsJsonAsync($"/api/v1/public/stores/{storeSlug}/orders", new
        {
            productSlug,
            quantity = 1,
            customerName = name,
            customerPhone = phone,
            customerEmail = email,
            answers = Array.Empty<object>(),
        });

        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<SubmitOrderPayload>())!;
    }

    /// <summary>Counted straight from the database — Phase 6 adds the endpoint that would do this.</summary>
    private async Task<int> CountCustomersAsync(string storeSlug)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Infrastructure.Persistence.AppDbContext>();

        var storeId = await db.Stores
            .Where(s => s.Slug == storeSlug)
            .Select(s => s.Id)
            .FirstAsync();

        return await db.Customers.CountAsync(c => c.StoreId == storeId);
    }
}

public sealed record FieldPayload(Guid Id, string Label, string FieldType, bool IsRequired, int DisplayOrder);

public sealed record SubmitOrderPayload(int OrderNumber, string StoreName, string? WhatsApp);

public sealed record ValidationProblemPayload(Dictionary<string, string[]> Errors);

public sealed record PublicProductDetailPayload(
    string Slug,
    string Name,
    List<PublicFieldPayload> CustomFields);

public sealed record PublicFieldPayload(
    Guid Id,
    string Label,
    string FieldType,
    bool IsRequired,
    List<string> Options);
