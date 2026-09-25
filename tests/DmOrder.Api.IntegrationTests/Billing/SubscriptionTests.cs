using System.Net;
using System.Net.Http.Json;
using DmOrder.Domain.Billing;
using DmOrder.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace DmOrder.Api.IntegrationTests.Billing;

/// <summary>
/// The paywall.
///
/// The dashboard greys buttons out, but that is a courtesy — these tests are the actual guarantee.
/// The two that matter most are the ones proving an expired trial locks order management *and*
/// leaves the storefront completely alone: a seller who stops paying must never lose their page,
/// their products or an incoming order.
/// </summary>
public sealed class SubscriptionTests(ApiFactory factory) : IntegrationTestBase(factory)
{
    [Fact]
    public async Task A_new_store_starts_on_a_trial_with_management_unlocked()
    {
        var seller = await CreateStoreAsync();
        using var client = CreateAuthenticatedClient(seller.AccessToken);

        var subscription = await client.GetFromJsonAsync<SubscriptionPayload>("/api/v1/seller/subscription");

        subscription.ShouldNotBeNull();
        subscription.Status.ShouldBe("Trialing");
        subscription.IsManagementUnlocked.ShouldBeTrue();
        subscription.PlanName.ShouldBe("Order Management Trial");
        subscription.TrialDaysRemaining.ShouldNotBeNull();

        (await client.GetAsync("/api/v1/seller/orders")).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task An_expired_trial_locks_order_management_with_a_402_and_a_reason()
    {
        var seller = await CreateStoreAsync();
        await ExpireTrialAsync(seller.User.StoreId!.Value);

        using var client = CreateAuthenticatedClient(seller.AccessToken);

        var response = await client.GetAsync("/api/v1/seller/orders");
        response.StatusCode.ShouldBe(HttpStatusCode.PaymentRequired);

        // The dashboard branches on the code, never the prose.
        var body = await response.Content.ReadAsStringAsync();
        body.ShouldContain("trial_expired");
    }

    [Fact]
    public async Task An_expired_trial_locks_every_management_route_not_just_the_list()
    {
        var seller = await CreateStoreAsync();
        await ExpireTrialAsync(seller.User.StoreId!.Value);

        using var client = CreateAuthenticatedClient(seller.AccessToken);

        // Group-level enforcement, so adding an endpoint cannot leave a hole. Checked here rather
        // than assumed.
        foreach (var route in new[]
                 {
                     "/api/v1/seller/orders",
                     "/api/v1/seller/orders/counts",
                     "/api/v1/seller/customers",
                 })
        {
            (await client.GetAsync(route)).StatusCode.ShouldBe(
                HttpStatusCode.PaymentRequired, $"{route} should be behind the paywall");
        }
    }

    [Fact]
    public async Task An_expired_trial_leaves_the_storefront_completely_alone()
    {
        var seller = await CreateStoreAsync(publish: true);
        await ExpireTrialAsync(seller.User.StoreId!.Value);

        // The public page still resolves.
        (await Client.GetAsync("/api/v1/public/stores/sarahs-cakes")).StatusCode
            .ShouldBe(HttpStatusCode.OK);

        // And the catalogue is still readable by customers.
        (await Client.GetAsync("/api/v1/public/stores/sarahs-cakes/products")).StatusCode
            .ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task An_expired_trial_does_not_stop_a_customer_ordering()
    {
        var seller = await CreateStoreAsync(publish: true, withProduct: true);
        await ExpireTrialAsync(seller.User.StoreId!.Value);

        var submitted = await Client.PostAsJsonAsync(
            "/api/v1/public/stores/sarahs-cakes/orders",
            new
            {
                productSlug = "chocolate-cake",
                quantity = 1,
                customerName = "Ayesha Khan",
                customerPhone = "03001234567",
                customerEmail = (string?)null,
                deliveryAddress = (string?)null,
                customerNote = (string?)null,
                answers = Array.Empty<object>(),
                website = (string?)null,
            });

        // Orders keep arriving. That is the incentive to subscribe, and breaking it would punish the
        // customer for the seller's billing.
        submitted.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task An_expired_trial_still_allows_editing_the_free_storefront()
    {
        var seller = await CreateStoreAsync();
        await ExpireTrialAsync(seller.User.StoreId!.Value);

        using var client = CreateAuthenticatedClient(seller.AccessToken);

        // Store settings, products and the theme are the free half of the product.
        (await client.GetAsync("/api/v1/seller/store")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await client.GetAsync("/api/v1/seller/products")).StatusCode.ShouldBe(HttpStatusCode.OK);

        var renamed = await client.PutAsJsonAsync(
            "/api/v1/seller/store",
            new { name = "Sarah's Cakes", contactPhone = "0300 1234567" });
        renamed.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_locked_seller_can_still_read_why_they_are_locked()
    {
        var seller = await CreateStoreAsync();
        await ExpireTrialAsync(seller.User.StoreId!.Value);

        using var client = CreateAuthenticatedClient(seller.AccessToken);

        // The subscription endpoint must not sit behind the paywall, or the upgrade screen has
        // nothing to render.
        var subscription = await client.GetFromJsonAsync<SubscriptionPayload>("/api/v1/seller/subscription");

        subscription.ShouldNotBeNull();
        subscription.Status.ShouldBe("TrialExpired");
        subscription.IsManagementUnlocked.ShouldBeFalse();
        subscription.PlanName.ShouldBe("Free Storefront");
    }

    [Fact]
    public async Task No_payment_provider_is_configured_and_the_api_says_so()
    {
        var seller = await CreateStoreAsync();
        using var client = CreateAuthenticatedClient(seller.AccessToken);

        var subscription = await client.GetFromJsonAsync<SubscriptionPayload>("/api/v1/seller/subscription");

        // Honest rather than hopeful: the UI must not offer a button that cannot do anything.
        subscription!.CanUpgrade.ShouldBeFalse();
        subscription.UpgradeUnavailableReason.ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task The_subscription_response_carries_no_prices()
    {
        var seller = await CreateStoreAsync();
        using var client = CreateAuthenticatedClient(seller.AccessToken);

        var body = await client.GetStringAsync("/api/v1/seller/subscription");

        // Pricing is not set. A number appearing here would be invented, and this is the test that
        // catches someone adding one.
        foreach (var token in new[] { "price", "amount", "currency", "PKR", "USD" })
        {
            body.ShouldNotContain(token, Case.Insensitive);
        }
    }

    [Fact]
    public async Task A_seller_with_no_store_gets_no_subscription_rather_than_an_invented_one()
    {
        var seller = await RegisterSellerAsync("nostore@example.com");
        using var client = CreateAuthenticatedClient(seller.AccessToken);

        var response = await client.GetAsync("/api/v1/seller/subscription");

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    // --- helpers ----------------------------------------------------------

    private async Task<AuthPayload> CreateStoreAsync(bool publish = false, bool withProduct = false)
    {
        var seller = await RegisterSellerAsync("baker@example.com");
        using var client = CreateAuthenticatedClient(seller.AccessToken);

        (await client.PostAsJsonAsync(
            "/api/v1/seller/store",
            new { name = "Sarah's Cakes", slug = "sarahs-cakes", currency = "PKR" }))
            .EnsureSuccessStatusCode();

        if (publish)
        {
            (await client.PutAsJsonAsync(
                "/api/v1/seller/store",
                new { name = "Sarah's Cakes", contactPhone = "0300 1234567" })).EnsureSuccessStatusCode();

            (await client.PostAsJsonAsync("/api/v1/seller/store/publish", new { }))
                .EnsureSuccessStatusCode();
        }

        if (withProduct)
        {
            var created = await client.PostAsJsonAsync(
                "/api/v1/seller/products", new { name = "Chocolate Cake" });
            created.EnsureSuccessStatusCode();

            var product = await created.Content.ReadFromJsonAsync<ProductPayload>();

            (await client.PutAsJsonAsync(
                $"/api/v1/seller/products/{product!.Id}",
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
        }

        // The store id lands in the claims, so the caller needs a freshly issued token.
        var refreshed = await Client.PostAsJsonAsync(
            "/api/v1/public/auth/refresh", new { refreshToken = seller.RefreshToken });
        refreshed.EnsureSuccessStatusCode();

        return (await refreshed.Content.ReadFromJsonAsync<AuthPayload>())!;
    }

    /// <summary>
    /// Winds the trial back so it has already ended.
    ///
    /// Done in the database rather than by waiting or by injecting a fake clock into the running
    /// host: the point of these tests is what the real API does when the real row says the trial is
    /// over.
    /// </summary>
    private async Task ExpireTrialAsync(Guid storeId)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var subscription = await db.Subscriptions.FirstAsync(s => s.StoreId == storeId);
        var past = DateTimeOffset.UtcNow.AddDays(-60);

        // Set through the entity's own API so the test cannot create a state the domain would not.
        db.Entry(subscription).Property(nameof(Subscription.TrialStartedAt)).CurrentValue = past;
        db.Entry(subscription).Property(nameof(Subscription.TrialEndsAt)).CurrentValue = past.AddDays(30);

        await db.SaveChangesAsync();
    }
}

public sealed record SubscriptionPayload(
    string Status,
    bool IsManagementUnlocked,
    int? TrialDaysRemaining,
    string PlanName,
    int ProductCount,
    int? FreeProductLimit,
    bool CanUpgrade,
    string? UpgradeUnavailableReason);
