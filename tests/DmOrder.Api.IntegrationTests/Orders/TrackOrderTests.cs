using System.Net;
using System.Net.Http.Json;
using Shouldly;

namespace DmOrder.Api.IntegrationTests.Orders;

/// <summary>
/// Customer-facing order tracking.
///
/// The tests that matter most are the negative ones: this endpoint is anonymous, so the only thing
/// standing between a guessed reference and somebody's name, address and order history is the phone
/// check — and the only thing stopping it being used to discover which references exist is that
/// every failure looks identical.
/// </summary>
public sealed class TrackOrderTests(ApiFactory factory) : IntegrationTestBase(factory)
{
    [Fact]
    public async Task A_customer_can_track_with_their_reference_and_phone()
    {
        var placed = await PlaceOrderAsync();

        var response = await Client.PostAsJsonAsync(
            "/api/v1/public/orders/track",
            new { reference = placed.Reference, phone = "+92 300 1234567" });

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var tracked = await response.Content.ReadFromJsonAsync<TrackedOrderPayload>();
        tracked.ShouldNotBeNull();
        tracked.Reference.ShouldBe(placed.Reference);
        tracked.StoreName.ShouldBe("Sarah's Cakes");
        tracked.Status.ShouldBe("New");
        tracked.CustomerName.ShouldBe("Ayesha Khan");
        tracked.Items.Count.ShouldBe(1);
        tracked.Timeline.Count.ShouldBeGreaterThanOrEqualTo(1);
    }

    [Fact]
    public async Task The_reference_alone_is_not_enough()
    {
        var placed = await PlaceOrderAsync();

        // The reference is printed on a slip and read out over the phone. It is not a secret, which
        // is exactly why it cannot be the only thing required.
        var response = await Client.PostAsJsonAsync(
            "/api/v1/public/orders/track",
            new { reference = placed.Reference, phone = "+92 300 9999999" });

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task A_wrong_reference_and_a_wrong_phone_are_indistinguishable()
    {
        var placed = await PlaceOrderAsync();

        var wrongPhone = await Client.PostAsJsonAsync(
            "/api/v1/public/orders/track",
            new { reference = placed.Reference, phone = "+92 300 9999999" });

        var wrongReference = await Client.PostAsJsonAsync(
            "/api/v1/public/orders/track",
            new { reference = "DM-2026-ZZZZZZ", phone = "+92 300 1234567" });

        // Same status and same body: telling them apart would turn this into an oracle for
        // discovering which references are real.
        //
        // The correlation id is per-request and is expected to differ, so it is stripped before
        // comparing — everything else must be byte-identical.
        wrongPhone.StatusCode.ShouldBe(wrongReference.StatusCode);

        WithoutTraceId(await wrongPhone.Content.ReadAsStringAsync())
            .ShouldBe(WithoutTraceId(await wrongReference.Content.ReadAsStringAsync()));
    }

    [Fact]
    public async Task Formatting_of_the_typed_reference_does_not_matter()
    {
        var placed = await PlaceOrderAsync();

        // Lower case with the hyphens left out is what people actually type off a printed slip.
        var typed = placed.Reference.Replace("-", "").ToLowerInvariant();

        var response = await Client.PostAsJsonAsync(
            "/api/v1/public/orders/track",
            new { reference = typed, phone = "+923001234567" });

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Tracking_never_exposes_the_sellers_private_notes()
    {
        var placed = await PlaceOrderAsync();

        using var seller = CreateAuthenticatedClient(placed.SellerAccessToken);

        // Find the order the seller's way, then attach a note only they should ever see.
        var orders = await seller.GetFromJsonAsync<PagedPayload<SellerOrderPayload>>("/api/v1/seller/orders");
        var orderId = orders!.Items[0].Id;

        (await seller.PostAsJsonAsync(
            $"/api/v1/seller/orders/{orderId}/notes",
            new { body = "Chase her about the deposit" })).EnsureSuccessStatusCode();

        var response = await Client.PostAsJsonAsync(
            "/api/v1/public/orders/track",
            new { reference = placed.Reference, phone = "+92 300 1234567" });

        var body = await response.Content.ReadAsStringAsync();

        body.ShouldNotContain("deposit");
        // Nor the platform identifier for the order.
        body.ShouldNotContain(orderId.ToString());
    }

    [Fact]
    public async Task References_are_unique_across_stores()
    {
        var first = await PlaceOrderAsync("sarahs-cakes", "baker@example.com");
        var second = await PlaceOrderAsync("adeel-clothing", "tailor@example.com");

        // Both stores number their own orders "#1"; the public reference is what makes a lookup
        // without naming a store possible at all.
        first.Reference.ShouldNotBe(second.Reference);
    }

    // --- helpers ----------------------------------------------------------

    private static string WithoutTraceId(string body) =>
        System.Text.RegularExpressions.Regex.Replace(body, "\"traceId\":\"[^\"]*\"", "\"traceId\":\"*\"");

    private sealed record PlacedOrder(string Reference, string SellerAccessToken);

    private async Task<PlacedOrder> PlaceOrderAsync(
        string slug = "sarahs-cakes",
        string sellerEmail = "baker@example.com")
    {
        var seller = await RegisterSellerAsync(sellerEmail);
        using var sellerClient = CreateAuthenticatedClient(seller.AccessToken);

        (await sellerClient.PostAsJsonAsync(
            "/api/v1/seller/store",
            new { name = "Sarah's Cakes", slug, currency = "PKR" })).EnsureSuccessStatusCode();

        // A store cannot go live without a contact channel.
        (await sellerClient.PutAsJsonAsync(
            "/api/v1/seller/store",
            new { name = "Sarah's Cakes", contactPhone = "+92 300 1234567" })).EnsureSuccessStatusCode();

        (await sellerClient.PostAsJsonAsync("/api/v1/seller/store/publish", new { }))
            .EnsureSuccessStatusCode();

        var product = await sellerClient.PostAsJsonAsync(
            "/api/v1/seller/products", new { name = "Chocolate Cake" });
        product.EnsureSuccessStatusCode();

        var created = await product.Content.ReadFromJsonAsync<ProductPayload>();

        (await sellerClient.PutAsJsonAsync(
            $"/api/v1/seller/products/{created!.Id}",
            new
            {
                name = "Chocolate Cake",
                slug = created.Slug,
                description = (string?)null,
                price = 4500m,
                priceIsFrom = false,
                isActive = true,
                acceptsCustomOrder = true,
                categoryId = (Guid?)null,
            })).EnsureSuccessStatusCode();

        var submitted = await Client.PostAsJsonAsync(
            $"/api/v1/public/stores/{slug}/orders",
            new
            {
                productSlug = created.Slug,
                quantity = 1,
                customerName = "Ayesha Khan",
                customerPhone = "+923001234567",
                customerEmail = (string?)null,
                deliveryAddress = (string?)null,
                customerNote = (string?)null,
                answers = Array.Empty<object>(),
                website = (string?)null,
            });

        submitted.EnsureSuccessStatusCode();

        var result = await submitted.Content.ReadFromJsonAsync<SubmitOrderPayload>();
        result!.Reference.ShouldNotBeNullOrWhiteSpace();

        // Freshly issued, because creating the store put a store id into the claims.
        var refreshed = await Client.PostAsJsonAsync(
            "/api/v1/public/auth/refresh", new { refreshToken = seller.RefreshToken });
        refreshed.EnsureSuccessStatusCode();

        var newAuth = await refreshed.Content.ReadFromJsonAsync<AuthPayload>();

        return new PlacedOrder(result.Reference, newAuth!.AccessToken);
    }
}

public sealed record SellerOrderPayload(Guid Id, int OrderNumber);

public sealed record TrackedOrderPayload(
    string Reference,
    string StoreName,
    string? StoreSlug,
    string Status,
    string CustomerName,
    List<TrackedItemPayload> Items,
    List<TrackedStepPayload> Timeline);

public sealed record TrackedItemPayload(string ProductName, int Quantity);

public sealed record TrackedStepPayload(string Status, DateTimeOffset At);
