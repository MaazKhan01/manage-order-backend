using System.Net;
using System.Net.Http.Json;
using Shouldly;

namespace DmOrder.Api.IntegrationTests.Orders;

/// <summary>
/// Orders the seller records by hand.
///
/// Most of these sellers still take orders through a DM or a conversation at a stall. This is what
/// stops the dashboard being a report on one channel and makes it the whole book.
/// </summary>
public sealed class ManualOrderTests(ApiFactory factory) : IntegrationTestBase(factory)
{
    [Fact]
    public async Task A_seller_can_record_an_order_against_a_catalogue_product()
    {
        var (client, product) = await SetUpAsync();

        var response = await client.PostAsJsonAsync("/api/v1/seller/orders", new
        {
            productId = product.Id,
            productName = (string?)null,
            unitPrice = 4500m,
            quantity = 2,
            customerName = "Walk-in Customer",
            customerPhone = "+923001234567",
            customerEmail = (string?)null,
            deliveryAddress = (string?)null,
            deliveryDate = (DateOnly?)null,
            customerNote = "Came into the shop",
            answers = Array.Empty<object>(),
        });

        response.StatusCode.ShouldBe(HttpStatusCode.Created);

        var created = await response.Content.ReadFromJsonAsync<ManualOrderPayload>();
        created.ShouldNotBeNull();
        created.OrderNumber.ShouldBe(1);

        // It gets a public reference like any other order, so the customer can still track it.
        created.Reference.ShouldStartWith("DM-");
    }

    [Fact]
    public async Task A_seller_can_record_something_that_is_not_in_their_catalogue()
    {
        var (client, _) = await SetUpAsync();

        // The whole point of a one-off: a custom job that was never a listed product.
        var response = await client.PostAsJsonAsync("/api/v1/seller/orders", new
        {
            productId = (Guid?)null,
            productName = "Custom three-tier wedding cake",
            unitPrice = (decimal?)null,
            quantity = 1,
            customerName = "Ayesha Khan",
            customerPhone = "+923001234567",
            customerEmail = (string?)null,
            deliveryAddress = (string?)null,
            deliveryDate = (DateOnly?)null,
            customerNote = (string?)null,
            answers = Array.Empty<object>(),
        });

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    [Fact]
    public async Task A_manual_order_needs_either_a_product_or_a_name()
    {
        var (client, _) = await SetUpAsync();

        var response = await client.PostAsJsonAsync("/api/v1/seller/orders", new
        {
            productId = (Guid?)null,
            productName = (string?)null,
            unitPrice = (decimal?)null,
            quantity = 1,
            customerName = "Ayesha Khan",
            customerPhone = "+923001234567",
            customerEmail = (string?)null,
            deliveryAddress = (string?)null,
            deliveryDate = (DateOnly?)null,
            customerNote = (string?)null,
            answers = Array.Empty<object>(),
        });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task A_manual_order_can_be_dated_in_the_past()
    {
        var (client, product) = await SetUpAsync();

        // The public form refuses a past delivery date - a customer cannot need something yesterday.
        // Recording history is the opposite case, and must not inherit that rule.
        var response = await client.PostAsJsonAsync("/api/v1/seller/orders", new
        {
            productId = product.Id,
            productName = (string?)null,
            unitPrice = 4500m,
            quantity = 1,
            customerName = "Ayesha Khan",
            customerPhone = "+923001234567",
            customerEmail = (string?)null,
            deliveryAddress = (string?)null,
            deliveryDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-7)),
            customerNote = "Delivered last week, recording it now",
            answers = Array.Empty<object>(),
        });

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    [Fact]
    public async Task A_manual_order_shares_the_customer_record_with_storefront_orders()
    {
        var (client, product) = await SetUpAsync(publish: true);

        // Same person, same number, typed two different ways through two different doors.
        (await Client.PostAsJsonAsync("/api/v1/public/stores/manual-store/orders", new
        {
            productSlug = product.Slug,
            quantity = 1,
            customerName = "Ayesha Khan",
            customerPhone = "+92 300 123 4567",
            customerEmail = (string?)null,
            deliveryAddress = (string?)null,
            customerNote = (string?)null,
            answers = Array.Empty<object>(),
            website = (string?)null,
        })).EnsureSuccessStatusCode();

        (await client.PostAsJsonAsync("/api/v1/seller/orders", new
        {
            productId = product.Id,
            productName = (string?)null,
            unitPrice = 4500m,
            quantity = 1,
            customerName = "Ayesha",
            customerPhone = "+923001234567",
            customerEmail = (string?)null,
            deliveryAddress = (string?)null,
            deliveryDate = (DateOnly?)null,
            customerNote = (string?)null,
            answers = Array.Empty<object>(),
        })).EnsureSuccessStatusCode();

        var customers = await client.GetFromJsonAsync<PagedPayload<CustomerListPayload>>(
            "/api/v1/seller/customers");

        // One customer with two orders, not two customers with one each. This is the whole reason
        // phone numbers are normalised to E.164 on the way in.
        customers!.TotalCount.ShouldBe(1);
        customers.Items[0].OrderCount.ShouldBe(2);
    }

    [Fact]
    public async Task A_manual_order_cannot_borrow_another_sellers_product()
    {
        var (alpha, alphaProduct) = await SetUpAsync("alpha@example.com", "alpha-store");
        var (beta, _) = await SetUpAsync("beta@example.com", "beta-store");

        using var _unused = alpha;

        var response = await beta.PostAsJsonAsync("/api/v1/seller/orders", new
        {
            // Alpha's product id, sent by Beta. Untrusted input like any other id in a request.
            productId = alphaProduct.Id,
            productName = (string?)null,
            unitPrice = 4500m,
            quantity = 1,
            customerName = "Ayesha Khan",
            customerPhone = "+923001234567",
            customerEmail = (string?)null,
            deliveryAddress = (string?)null,
            deliveryDate = (DateOnly?)null,
            customerNote = (string?)null,
            answers = Array.Empty<object>(),
        });

        // 404 rather than 403: a 403 would confirm the product exists.
        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task A_manual_order_is_trackable_by_the_customer()
    {
        var (client, product) = await SetUpAsync(publish: true);

        var created = await client.PostAsJsonAsync("/api/v1/seller/orders", new
        {
            productId = product.Id,
            productName = (string?)null,
            unitPrice = 4500m,
            quantity = 1,
            customerName = "Ayesha Khan",
            customerPhone = "+923001234567",
            customerEmail = (string?)null,
            deliveryAddress = (string?)null,
            deliveryDate = (DateOnly?)null,
            customerNote = (string?)null,
            answers = Array.Empty<object>(),
        });

        var order = await created.Content.ReadFromJsonAsync<ManualOrderPayload>();

        // The seller can hand over the reference exactly as they would for a storefront order.
        var tracked = await Client.PostAsJsonAsync("/api/v1/public/orders/track", new
        {
            reference = order!.Reference,
            phone = "+923001234567",
        });

        tracked.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    // --- helpers ----------------------------------------------------------

    private async Task<(HttpClient Client, ProductPayload Product)> SetUpAsync(
        string email = "manual@example.com",
        string slug = "manual-store",
        bool publish = false)
    {
        var seller = await RegisterSellerAsync(email);
        var client = CreateAuthenticatedClient(seller.AccessToken);

        (await client.PostAsJsonAsync(
            "/api/v1/seller/store",
            new { name = "Manual Store", slug, currency = "PKR" })).EnsureSuccessStatusCode();

        if (publish)
        {
            (await client.PutAsJsonAsync(
                "/api/v1/seller/store",
                new { name = "Manual Store", contactPhone = "+92 300 1234567" })).EnsureSuccessStatusCode();

            (await client.PostAsJsonAsync("/api/v1/seller/store/publish", new { }))
                .EnsureSuccessStatusCode();
        }

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

        // The store id lands in the claims, so the client needs a freshly issued token.
        var refreshed = await Client.PostAsJsonAsync(
            "/api/v1/public/auth/refresh", new { refreshToken = seller.RefreshToken });
        refreshed.EnsureSuccessStatusCode();

        var auth = (await refreshed.Content.ReadFromJsonAsync<AuthPayload>())!;

        client.Dispose();
        return (CreateAuthenticatedClient(auth.AccessToken), product);
    }
}

public sealed record ManualOrderPayload(Guid Id, int OrderNumber, string Reference);
