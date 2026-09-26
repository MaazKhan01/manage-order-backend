using System.Net;
using System.Net.Http.Json;
using Shouldly;

namespace DmOrder.Api.IntegrationTests.Orders;

/// <summary>
/// The endpoint that reads a pasted customer message into a draft order.
///
/// The suite runs with no <c>Claude:ApiKey</c>, so the not-configured reader is registered and the
/// endpoint answers 503. That is the deployment state these tests can prove: that the route exists,
/// is authenticated, is gated behind the subscription, validates its input, and degrades honestly
/// when no provider is configured. What the model extracts is covered by OrderDraftMapperTests,
/// which needs neither a network nor an API key.
/// </summary>
public sealed class DraftOrderFromMessageTests(ApiFactory factory) : IntegrationTestBase(factory)
{
    private const string Route = "/api/v1/seller/orders/draft-from-message";

    [Fact]
    public async Task Drafting_requires_authentication()
    {
        var response = await Client.PostAsJsonAsync(Route, new { message = "2 cakes please" });

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task With_no_ai_provider_configured_the_feature_reports_itself_unavailable()
    {
        var client = await SetUpSellerAsync();

        var response = await client.PostAsJsonAsync(Route, new { message = "I want 2 tote bags" });

        // 503, not 500: nothing is broken. The provider simply is not switched on, and the
        // dashboard needs to be able to tell those apart.
        response.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
    }

    [Fact]
    public async Task An_empty_message_is_rejected_before_any_model_is_called()
    {
        var client = await SetUpSellerAsync();

        var response = await client.PostAsJsonAsync(Route, new { message = "   " });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task A_message_longer_than_the_limit_is_rejected_before_any_model_is_called()
    {
        // The validator runs first, so an oversized paste costs nothing. Without that, the cheapest
        // way to run up a bill would be to paste a book.
        var client = await SetUpSellerAsync();

        var response = await client.PostAsJsonAsync(Route, new { message = new string('x', 4001) });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task A_seller_with_no_store_cannot_draft()
    {
        var seller = await RegisterSellerAsync("no-store-draft@example.com");
        var client = CreateAuthenticatedClient(seller.AccessToken);

        var response = await client.PostAsJsonAsync(Route, new { message = "2 tote bags" });

        // Nothing to read a message against. Same answer as every other store-scoped route.
        response.StatusCode.ShouldBeOneOf(HttpStatusCode.NotFound, HttpStatusCode.Forbidden);
    }

    private async Task<HttpClient> SetUpSellerAsync(
        string email = "draft@example.com",
        string slug = "draft-store")
    {
        var seller = await RegisterSellerAsync(email);
        var client = CreateAuthenticatedClient(seller.AccessToken);

        (await client.PostAsJsonAsync(
            "/api/v1/seller/store",
            new { name = "Draft Store", slug, currency = "PKR" })).EnsureSuccessStatusCode();

        return client;
    }
}
