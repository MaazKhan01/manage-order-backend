using System.Net;
using System.Net.Http.Json;
using Shouldly;

namespace DmOrder.Api.IntegrationTests.Orders;

/// <summary>
/// Phone numbers, checked against real numbering plans rather than by counting digits.
///
/// The platform is international by design, so the same rule has to accept a Karachi mobile and a
/// London landline while still refusing eleven random digits. A length check cannot do that — it
/// either rejects real customers or accepts nonsense, and usually both.
/// </summary>
public sealed class PhoneValidationTests(ApiFactory factory) : IntegrationTestBase(factory)
{
    [Theory]
    // Real numbers from several plans, in the E.164 the frontend sends.
    [InlineData("+923001234567")]   // Pakistan mobile
    [InlineData("+447911123456")]   // UK mobile
    [InlineData("+12125551234")]    // US
    [InlineData("+971501234567")]   // UAE mobile
    [InlineData("+2348021234567")]  // Nigeria mobile
    [InlineData("+92 300 123 4567")] // Spacing is the customer's business, not ours
    public async Task AcceptsRealNumbersFromAnywhere(string phone)
    {
        var slug = await SetUpStoreAsync();

        var response = await SubmitAsync(slug, phone);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Theory]
    [InlineData("12345")]            // too short for any plan
    [InlineData("+92300123")]        // right country, wrong length
    [InlineData("not a phone")]
    [InlineData("+9999999999999")]   // no such country code
    [InlineData("03001234567")]      // national format with no country: genuinely ambiguous
    public async Task RefusesWhatIsNotADialableNumber(string phone)
    {
        var slug = await SetUpStoreAsync();

        var response = await SubmitAsync(slug, phone);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task StoresOneCustomerWhateverFormatTheyTyped()
    {
        var slug = await SetUpStoreAsync();

        // The same number, four ways. Anything other than one customer here means a seller's list
        // fills up with duplicates of the same person.
        foreach (var phone in new[]
                 {
                     "+923001234567",
                     "+92 300 123 4567",
                     "+92-300-1234567",
                     "+92 (300) 1234567",
                 })
        {
            (await SubmitAsync(slug, phone)).EnsureSuccessStatusCode();
        }

        var seller = CreateAuthenticatedClient(_sellerToken);
        using var _ = seller;

        var customers = await seller.GetFromJsonAsync<PagedPayload<CustomerListPayload>>(
            "/api/v1/seller/customers");

        customers!.TotalCount.ShouldBe(1);
        customers.Items[0].OrderCount.ShouldBe(4);
        // Stored canonically, whatever arrived.
        customers.Items[0].Phone.ShouldBe("+923001234567");
    }

    [Fact]
    public async Task ASellerCanFindACustomerByTheNumberTheWayTheyKnowIt()
    {
        var slug = await SetUpStoreAsync();
        (await SubmitAsync(slug, "+923002222222")).EnsureSuccessStatusCode();

        var seller = CreateAuthenticatedClient(_sellerToken);
        using var _ = seller;

        // Stored as +923002222222; the seller types the number the way they say it out loud. Neither
        // a prefix nor a substring match connects those, which is why the search compares trailing
        // digits.
        foreach (var typed in new[] { "03002222222", "0300 222 2222", "2222222", "+923002222222" })
        {
            var found = await seller.GetFromJsonAsync<PagedPayload<CustomerListPayload>>(
                $"/api/v1/seller/customers?search={Uri.EscapeDataString(typed)}");

            found!.TotalCount.ShouldBe(1, $"searching '{typed}' should find the customer");
        }
    }

    // --- helpers ----------------------------------------------------------

    private string _sellerToken = string.Empty;
    private string _productSlug = string.Empty;

    private async Task<string> SetUpStoreAsync()
    {
        const string slug = "phone-store";

        var seller = await RegisterSellerAsync("phone@example.com");
        using var client = CreateAuthenticatedClient(seller.AccessToken);

        (await client.PostAsJsonAsync(
            "/api/v1/seller/store",
            new { name = "Phone Store", slug, currency = "PKR" })).EnsureSuccessStatusCode();

        (await client.PutAsJsonAsync(
            "/api/v1/seller/store",
            new { name = "Phone Store", contactPhone = "+92 300 1234567" })).EnsureSuccessStatusCode();

        (await client.PostAsJsonAsync("/api/v1/seller/store/publish", new { })).EnsureSuccessStatusCode();

        var created = await client.PostAsJsonAsync("/api/v1/seller/products", new { name = "Chocolate Cake" });
        created.EnsureSuccessStatusCode();
        var product = (await created.Content.ReadFromJsonAsync<ProductPayload>())!;

        (await client.PutAsJsonAsync(
            $"/api/v1/seller/products/{product.Id}",
            new
            {
                name = "Chocolate Cake",
                slug = product.Slug,
                description = (string?)null,
                price = 4500m,
                priceIsFrom = false,
                isActive = true,
                acceptsCustomOrder = true,
                categoryId = (Guid?)null,
            })).EnsureSuccessStatusCode();

        var refreshed = await Client.PostAsJsonAsync(
            "/api/v1/public/auth/refresh", new { refreshToken = seller.RefreshToken });
        refreshed.EnsureSuccessStatusCode();

        _sellerToken = (await refreshed.Content.ReadFromJsonAsync<AuthPayload>())!.AccessToken;
        _productSlug = product.Slug;

        return slug;
    }

    private Task<HttpResponseMessage> SubmitAsync(string slug, string phone) =>
        Client.PostAsJsonAsync($"/api/v1/public/stores/{slug}/orders", new
        {
            productSlug = _productSlug,
            quantity = 1,
            customerName = "Ayesha Khan",
            customerPhone = phone,
            customerEmail = (string?)null,
            deliveryAddress = (string?)null,
            customerNote = (string?)null,
            answers = Array.Empty<object>(),
            website = (string?)null,
        });
}
