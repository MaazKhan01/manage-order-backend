using System.Net;
using System.Net.Http.Json;
using DmOrder.Api.IntegrationTests;
using DmOrder.Domain.Identity;
using DmOrder.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace DmOrder.Api.IntegrationTests.Admin;

/// <summary>
/// The admin area is the one part of the API that reads across tenants, so the tests that matter most
/// are the ones proving a seller cannot reach it.
/// </summary>
public sealed class AdminTests(ApiFactory factory) : IntegrationTestBase(factory)
{
    [Fact]
    public async Task Admin_routes_reject_an_anonymous_caller()
    {
        var response = await Client.GetAsync("/api/v1/admin/stats");

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Admin_routes_reject_a_seller()
    {
        var seller = await RegisterSellerAsync("seller@example.com");
        using var sellerClient = CreateAuthenticatedClient(seller.AccessToken);

        // Every route, not just one: a single missing policy is exactly the mistake this catches.
        (await sellerClient.GetAsync("/api/v1/admin/stats")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await sellerClient.GetAsync("/api/v1/admin/stores")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await sellerClient.GetAsync("/api/v1/admin/sellers")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        var suspend = await sellerClient.PutAsJsonAsync(
            $"/api/v1/admin/stores/{Guid.CreateVersion7()}/status",
            new { isActive = false });

        suspend.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Stats_count_the_whole_platform()
    {
        await CreateStoreForNewSellerAsync("one@example.com", "Store One", "store-one");
        await CreateStoreForNewSellerAsync("two@example.com", "Store Two", "store-two");

        using var admin = await CreateAdminClientAsync();

        var stats = await admin.GetFromJsonAsync<AdminStatsPayload>("/api/v1/admin/stats");

        stats.ShouldNotBeNull();
        stats.TotalSellers.ShouldBe(2);
        stats.ActiveSellers.ShouldBe(2);
        stats.TotalStores.ShouldBe(2);
        // Neither store was published, so nothing is reachable by the public yet.
        stats.LiveStores.ShouldBe(0);
        stats.SuspendedStores.ShouldBe(0);
    }

    [Fact]
    public async Task Store_list_carries_the_owner_and_can_be_searched()
    {
        await CreateStoreForNewSellerAsync("baker@example.com", "Sarah's Cakes", "sarahs-cakes", "Sarah");
        await CreateStoreForNewSellerAsync("tailor@example.com", "Adeel Clothing", "adeel-clothing", "Adeel");

        using var admin = await CreateAdminClientAsync();

        var all = await admin.GetFromJsonAsync<PagedPayload<AdminStorePayload>>("/api/v1/admin/stores");
        all!.TotalCount.ShouldBe(2);

        var found = await admin.GetFromJsonAsync<PagedPayload<AdminStorePayload>>(
            "/api/v1/admin/stores?search=cakes");

        found!.Items.Count.ShouldBe(1);
        found.Items[0].Slug.ShouldBe("sarahs-cakes");
        found.Items[0].OwnerName.ShouldBe("Sarah");
        found.Items[0].OwnerEmail.ShouldBe("baker@example.com");
    }

    [Fact]
    public async Task Suspending_a_store_takes_the_storefront_off_the_public_api()
    {
        var seller = await CreateStoreForNewSellerAsync("baker@example.com", "Sarah's Cakes", "sarahs-cakes");
        using var sellerClient = CreateAuthenticatedClient(seller.AccessToken);

        // A store cannot go live without a way to reach the seller — that rule is what makes the
        // storefront's contact block trustworthy, so publishing has to satisfy it here too.
        (await sellerClient.PutAsJsonAsync(
            "/api/v1/seller/store",
            new { name = "Sarah's Cakes", contactPhone = "0300 1234567" })).EnsureSuccessStatusCode();

        (await sellerClient.PostAsJsonAsync("/api/v1/seller/store/publish", new { })).EnsureSuccessStatusCode();
        (await Client.GetAsync("/api/v1/public/stores/sarahs-cakes")).StatusCode.ShouldBe(HttpStatusCode.OK);

        using var admin = await CreateAdminClientAsync();
        var storeId = (await admin.GetFromJsonAsync<PagedPayload<AdminStorePayload>>("/api/v1/admin/stores"))!
            .Items[0].Id;

        var suspend = await admin.PutAsJsonAsync(
            $"/api/v1/admin/stores/{storeId}/status",
            new { isActive = false });
        suspend.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await Client.GetAsync("/api/v1/public/stores/sarahs-cakes")).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // Suspension keeps the seller's own access: they are locked out of the public, not their data.
        (await sellerClient.GetAsync("/api/v1/seller/store")).StatusCode.ShouldBe(HttpStatusCode.OK);

        var restore = await admin.PutAsJsonAsync(
            $"/api/v1/admin/stores/{storeId}/status",
            new { isActive = true });
        restore.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await Client.GetAsync("/api/v1/public/stores/sarahs-cakes")).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Suspending_a_seller_stops_them_signing_in()
    {
        await RegisterSellerAsync("baker@example.com", displayName: "Sarah");

        using var admin = await CreateAdminClientAsync();
        var sellers = await admin.GetFromJsonAsync<PagedPayload<AdminSellerPayload>>("/api/v1/admin/sellers");
        sellers!.Items.Count.ShouldBe(1);

        var suspend = await admin.PutAsJsonAsync(
            $"/api/v1/admin/sellers/{sellers.Items[0].Id}/status",
            new { isActive = false });
        suspend.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var login = await Client.PostAsJsonAsync(
            "/api/v1/public/auth/login",
            new { email = "baker@example.com", password = "a-long-enough-password" });

        login.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task An_administrator_cannot_be_suspended_through_the_seller_route()
    {
        using var admin = await CreateAdminClientAsync();

        // The id is a real administrator's, but the route only knows about Sellers — and reports
        // anything else as missing rather than confirming that an admin account exists.
        var adminId = await GetAdminUserIdAsync();

        var response = await admin.PutAsJsonAsync(
            $"/api/v1/admin/sellers/{adminId}/status",
            new { isActive = false });

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Seller_list_shows_an_account_that_has_not_created_a_store_yet()
    {
        await RegisterSellerAsync("nostore@example.com", displayName: "Undecided");

        using var admin = await CreateAdminClientAsync();

        var sellers = await admin.GetFromJsonAsync<PagedPayload<AdminSellerPayload>>("/api/v1/admin/sellers");

        sellers!.Items.Count.ShouldBe(1);
        sellers.Items[0].DisplayName.ShouldBe("Undecided");
        sellers.Items[0].StoreSlug.ShouldBeNull();
    }

    [Fact]
    public async Task Sorting_happens_in_the_database_and_ignores_an_unknown_column()
    {
        await CreateStoreForNewSellerAsync("c@example.com", "Charlie Cakes", "charlie");
        await CreateStoreForNewSellerAsync("a@example.com", "Alpha Attire", "alpha");
        await CreateStoreForNewSellerAsync("b@example.com", "Bravo Blooms", "bravo");

        using var admin = await CreateAdminClientAsync();

        var ascending = await admin.GetFromJsonAsync<PagedPayload<AdminStorePayload>>(
            "/api/v1/admin/stores?sort=name");
        ascending!.Items.Select(s => s.Name)
            .ShouldBe(["Alpha Attire", "Bravo Blooms", "Charlie Cakes"]);

        var descending = await admin.GetFromJsonAsync<PagedPayload<AdminStorePayload>>(
            "/api/v1/admin/stores?sort=-name");
        descending!.Items.Select(s => s.Name)
            .ShouldBe(["Charlie Cakes", "Bravo Blooms", "Alpha Attire"]);

        // A stale bookmark naming a column that no longer exists should still render a list rather
        // than fail, so an unknown field falls back to the default instead of throwing.
        var unknown = await admin.GetFromJsonAsync<PagedPayload<AdminStorePayload>>(
            "/api/v1/admin/stores?sort=drop%20table");
        unknown!.Items.Count.ShouldBe(3);
        unknown.Items[0].Name.ShouldBe("Bravo Blooms");
    }

    // --- helpers ----------------------------------------------------------

    private async Task<AuthPayload> CreateStoreForNewSellerAsync(
        string email,
        string storeName,
        string slug,
        string displayName = "Test Seller")
    {
        var seller = await RegisterSellerAsync(email, displayName: displayName);
        using var client = CreateAuthenticatedClient(seller.AccessToken);

        var created = await client.PostAsJsonAsync(
            "/api/v1/seller/store",
            new { name = storeName, slug, currency = "PKR" });

        created.EnsureSuccessStatusCode();

        // The store id lands in the token's claims, so the caller needs a freshly issued one.
        var refreshed = await Client.PostAsJsonAsync(
            "/api/v1/public/auth/refresh",
            new { refreshToken = seller.RefreshToken });

        refreshed.EnsureSuccessStatusCode();
        return (await refreshed.Content.ReadFromJsonAsync<AuthPayload>())!;
    }

    /// <summary>
    /// Creates a platform administrator directly, because the API deliberately has no route that
    /// promotes anyone — an endpoint that mints admins is a privilege-escalation hole.
    /// </summary>
    private async Task<HttpClient> CreateAdminClientAsync()
    {
        const string email = "admin@example.com";
        const string password = "a-long-enough-password";

        using (var scope = Factory.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

            var admin = new ApplicationUser
            {
                Id = Guid.CreateVersion7(),
                UserName = email,
                Email = email,
                DisplayName = "Platform Admin",
                EmailConfirmed = true,
                IsActive = true,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow,
            };

            (await users.CreateAsync(admin, password)).Succeeded.ShouldBeTrue();
            (await users.AddToRoleAsync(admin, ApplicationRoles.Admin)).Succeeded.ShouldBeTrue();
        }

        var login = await Client.PostAsJsonAsync("/api/v1/public/auth/login", new { email, password });
        login.EnsureSuccessStatusCode();

        var auth = (await login.Content.ReadFromJsonAsync<AuthPayload>())!;
        return CreateAuthenticatedClient(auth.AccessToken);
    }

    private async Task<Guid> GetAdminUserIdAsync()
    {
        using var scope = Factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        var admin = await users.FindByEmailAsync("admin@example.com");
        admin.ShouldNotBeNull();
        return admin.Id;
    }
}

public sealed record AdminStatsPayload(
    int TotalSellers,
    int ActiveSellers,
    int TotalStores,
    int LiveStores,
    int SuspendedStores,
    int TotalProducts,
    int TotalOrders,
    int OrdersLast30Days);

public sealed record AdminStorePayload(
    Guid Id,
    string Name,
    string Slug,
    bool IsPublished,
    bool IsActive,
    string OwnerName,
    string OwnerEmail,
    int ProductCount,
    int OrderCount);

public sealed record AdminSellerPayload(
    Guid Id,
    string DisplayName,
    string Email,
    bool IsActive,
    string? StoreName,
    string? StoreSlug);

