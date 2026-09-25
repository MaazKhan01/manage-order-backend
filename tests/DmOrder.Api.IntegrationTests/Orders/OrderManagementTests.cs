using System.Net;
using System.Net.Http.Json;
using DmOrder.Api.IntegrationTests.Catalogue;
using DmOrder.Api.IntegrationTests.Stores;

namespace DmOrder.Api.IntegrationTests.Orders;

/// <summary>
/// The seller's side of an order: seeing it, moving it along, and recording what happened.
/// </summary>
public class OrderManagementTests(ApiFactory factory) : IntegrationTestBase(factory)
{
    private async Task<(HttpClient Seller, string Slug, string ProductSlug)> SetUpStoreWithOrderAsync(
        string email,
        string slug)
    {
        var auth = await RegisterSellerAsync(email);
        var seller = CreateAuthenticatedClient(auth.AccessToken);

        await StoreTests.CreateStoreAsync(seller, slug);
        await StoreTests.SetContactAsync(seller, slug);
        (await seller.PostAsync("/api/v1/seller/store/publish", null)).EnsureSuccessStatusCode();

        var product = await CatalogueTests.CreateProductAsync(seller, "Custom Cake", null);
        return (seller, slug, product.Slug);
    }

    private async Task SubmitOrderAsync(string slug, string productSlug, string name, string phone)
    {
        var response = await Client.PostAsJsonAsync($"/api/v1/public/stores/{slug}/orders", new
        {
            productSlug,
            quantity = 1,
            customerName = name,
            customerPhone = phone,
            answers = Array.Empty<object>(),
        });

        response.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task ASubmittedOrderAppearsInTheSellersList()
    {
        var (seller, slug, productSlug) = await SetUpStoreWithOrderAsync("list@example.com", "list-orders");
        using var _ = seller;

        await SubmitOrderAsync(slug, productSlug, "Ayesha", "+923001234567");

        var list = await seller.GetFromJsonAsync<PagedPayload<OrderListPayload>>("/api/v1/seller/orders");

        list!.TotalCount.ShouldBe(1);
        list.Items[0].OrderNumber.ShouldBe(1);
        list.Items[0].Status.ShouldBe("New");
        list.Items[0].CustomerName.ShouldBe("Ayesha");
        list.Items[0].ProductSummary.ShouldBe("Custom Cake");
    }

    [Fact]
    public async Task OrderCarriesItsHistoryFromTheMomentItArrived()
    {
        var (seller, slug, productSlug) = await SetUpStoreWithOrderAsync("hist@example.com", "hist-store");
        using var _ = seller;

        await SubmitOrderAsync(slug, productSlug, "Ayesha", "+923001234567");
        var orderId = await FirstOrderIdAsync(seller);

        var detail = await seller.GetFromJsonAsync<OrderDetailPayload>($"/api/v1/seller/orders/{orderId}");

        // The timeline starts when the customer submitted, not at the seller's first action.
        detail!.History.Count.ShouldBe(1);
        detail.History[0].FromStatus.ShouldBeNull();
        detail.History[0].ToStatus.ShouldBe("New");
        detail.History[0].ChangedBy.ShouldBeNull("no seller acted — the customer submitted it");
    }

    [Fact]
    public async Task ChangingStatusIsRecordedWithWhoAndWhy()
    {
        var (seller, slug, productSlug) = await SetUpStoreWithOrderAsync("status@example.com", "status-store");
        using var _ = seller;

        await SubmitOrderAsync(slug, productSlug, "Ayesha", "+923001234567");
        var orderId = await FirstOrderIdAsync(seller);

        var response = await seller.PutAsJsonAsync($"/api/v1/seller/orders/{orderId}/status", new
        {
            status = "Confirmed",
            note = "Spoke to her, confirmed the design.",
        });

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var detail = await seller.GetFromJsonAsync<OrderDetailPayload>($"/api/v1/seller/orders/{orderId}");

        detail!.Status.ShouldBe("Confirmed");
        detail.History.Count.ShouldBe(2);
        detail.History[1].FromStatus.ShouldBe("New");
        detail.History[1].ToStatus.ShouldBe("Confirmed");
        detail.History[1].Note.ShouldBe("Spoke to her, confirmed the design.");
        detail.History[1].ChangedBy.ShouldNotBeNullOrWhiteSpace("a seller did this, so it is attributed");
    }

    [Fact]
    public async Task ACompletedOrderCannotBeReopened()
    {
        var (seller, slug, productSlug) = await SetUpStoreWithOrderAsync("final@example.com", "final-store");
        using var _ = seller;

        await SubmitOrderAsync(slug, productSlug, "Ayesha", "+923001234567");
        var orderId = await FirstOrderIdAsync(seller);

        await seller.PutAsJsonAsync($"/api/v1/seller/orders/{orderId}/status",
            new { status = "Completed", note = (string?)null });

        var reopen = await seller.PutAsJsonAsync($"/api/v1/seller/orders/{orderId}/status",
            new { status = "InProgress", note = (string?)null });

        reopen.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);

        var detail = await seller.GetFromJsonAsync<OrderDetailPayload>($"/api/v1/seller/orders/{orderId}");
        detail!.Status.ShouldBe("Completed");
        detail.IsFinal.ShouldBeTrue();
    }

    [Fact]
    public async Task SellerCanQuoteATotalOnAnUnpricedOrder()
    {
        var (seller, slug, productSlug) = await SetUpStoreWithOrderAsync("quote@example.com", "quote-store");
        using var _ = seller;

        await SubmitOrderAsync(slug, productSlug, "Ayesha", "+923001234567");
        var orderId = await FirstOrderIdAsync(seller);

        var response = await seller.PutAsJsonAsync($"/api/v1/seller/orders/{orderId}/payment", new
        {
            paymentStatus = "PartiallyPaid",
            totalAmount = 12_500m,
        });

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var detail = await seller.GetFromJsonAsync<OrderDetailPayload>($"/api/v1/seller/orders/{orderId}");
        detail!.TotalAmount.ShouldBe(12_500m);
        detail.PaymentStatus.ShouldBe("PartiallyPaid");
    }

    [Fact]
    public async Task NotesAreAddedAndRemovedByTheSeller()
    {
        var (seller, slug, productSlug) = await SetUpStoreWithOrderAsync("note@example.com", "note-store");
        using var _ = seller;

        await SubmitOrderAsync(slug, productSlug, "Ayesha", "+923001234567");
        var orderId = await FirstOrderIdAsync(seller);

        var added = await seller.PostAsJsonAsync($"/api/v1/seller/orders/{orderId}/notes", new
        {
            body = "Needs chasing about the deposit.",
        });
        added.StatusCode.ShouldBe(HttpStatusCode.OK);

        var note = await added.Content.ReadFromJsonAsync<NotePayload>();

        var afterAdd = await seller.GetFromJsonAsync<OrderDetailPayload>($"/api/v1/seller/orders/{orderId}");
        afterAdd!.Notes.Count.ShouldBe(1);

        (await seller.DeleteAsync($"/api/v1/seller/orders/{orderId}/notes/{note!.Id}"))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var afterDelete = await seller.GetFromJsonAsync<OrderDetailPayload>($"/api/v1/seller/orders/{orderId}");
        afterDelete!.Notes.ShouldBeEmpty();
    }

    [Fact]
    public async Task OrderCountsDriveTheFilterTabs()
    {
        var (seller, slug, productSlug) = await SetUpStoreWithOrderAsync("counts@example.com", "counts-store");
        using var _ = seller;

        await SubmitOrderAsync(slug, productSlug, "Ayesha", "+923001111111");
        await SubmitOrderAsync(slug, productSlug, "Bilal", "+923002222222");

        var orderId = await FirstOrderIdAsync(seller);
        await seller.PutAsJsonAsync($"/api/v1/seller/orders/{orderId}/status",
            new { status = "Confirmed", note = (string?)null });

        var counts = await seller.GetFromJsonAsync<CountsPayload>("/api/v1/seller/orders/counts");

        counts!.Total.ShouldBe(2);
        counts.New.ShouldBe(1);
        counts.Confirmed.ShouldBe(1);
    }

    [Fact]
    public async Task SearchFindsAnOrderByNumberOrCustomer()
    {
        var (seller, slug, productSlug) = await SetUpStoreWithOrderAsync("search@example.com", "search-store");
        using var _ = seller;

        await SubmitOrderAsync(slug, productSlug, "Ayesha Khan", "+923001111111");
        await SubmitOrderAsync(slug, productSlug, "Bilal Ahmed", "+923002222222");

        var byName = await seller.GetFromJsonAsync<PagedPayload<OrderListPayload>>(
            "/api/v1/seller/orders?search=ayesha");
        var byNumber = await seller.GetFromJsonAsync<PagedPayload<OrderListPayload>>(
            "/api/v1/seller/orders?search=%232");
        var byPhone = await seller.GetFromJsonAsync<PagedPayload<OrderListPayload>>(
            "/api/v1/seller/orders?search=03002222222");

        byName!.TotalCount.ShouldBe(1);
        byNumber!.TotalCount.ShouldBe(1);
        byNumber.Items[0].OrderNumber.ShouldBe(2);
        byPhone!.TotalCount.ShouldBe(1);
    }

    [Fact]
    public async Task CustomerRecordIsBuiltFromTheirOrders()
    {
        var (seller, slug, productSlug) = await SetUpStoreWithOrderAsync("cust@example.com", "cust-store");
        using var _ = seller;

        await SubmitOrderAsync(slug, productSlug, "Ayesha Khan", "+92 300 123 4567");
        await SubmitOrderAsync(slug, productSlug, "Ayesha", "+923001234567");

        var customers = await seller.GetFromJsonAsync<PagedPayload<CustomerListPayload>>("/api/v1/seller/customers");

        // Both orders are the same person — the phone formatting differed, not the customer.
        customers!.TotalCount.ShouldBe(1);
        customers.Items[0].OrderCount.ShouldBe(2);

        var detail = await seller.GetFromJsonAsync<CustomerDetailPayload>(
            $"/api/v1/seller/customers/{customers.Items[0].Id}");

        detail!.Orders.Count.ShouldBe(2);
        detail.Name.ShouldBe("Ayesha", "the most recent order's name wins");
    }

    [Fact]
    public async Task AnswersRenderFromTheirSnapshotAfterTheQuestionIsDeleted()
    {
        var (seller, slug, productSlug) = await SetUpStoreWithOrderAsync("snap@example.com", "snap-orders");
        using var _ = seller;

        var productId = (await seller.GetFromJsonAsync<PagedPayload<ProductListPayload>>("/api/v1/seller/products"))!
            .Items[0].Id;

        var created = await seller.PostAsJsonAsync("/api/v1/seller/custom-fields", new
        {
            productId,
            label = "Flavour",
            fieldType = "Text",
        });
        var field = (await created.Content.ReadFromJsonAsync<FieldPayload>())!;

        await Client.PostAsJsonAsync($"/api/v1/public/stores/{slug}/orders", new
        {
            productSlug,
            quantity = 1,
            customerName = "Ayesha",
            customerPhone = "+923001234567",
            answers = new object[] { new { fieldId = field.Id, value = "Chocolate" } },
        });

        (await seller.DeleteAsync($"/api/v1/seller/custom-fields/{field.Id}"))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var orderId = await FirstOrderIdAsync(seller);
        var detail = await seller.GetFromJsonAsync<OrderDetailPayload>($"/api/v1/seller/orders/{orderId}");

        // The whole promise of ADR 0004: deleting the question does not erase the answer.
        var answer = detail!.Items[0].Answers.Single();
        answer.Label.ShouldBe("Flavour");
        answer.Value.ShouldBe("Chocolate");
    }

    // --- Tenant isolation ------------------------------------------------

    [Fact]
    public async Task SellerCannotSeeOrTouchAnotherSellersOrder()
    {
        var (alpha, alphaSlug, alphaProduct) = await SetUpStoreWithOrderAsync("iso-a@example.com", "order-alpha");
        var (beta, betaSlug, betaProduct) = await SetUpStoreWithOrderAsync("iso-b@example.com", "order-beta");
        using var _1 = alpha;
        using var _2 = beta;

        await SubmitOrderAsync(alphaSlug, alphaProduct, "Ayesha", "+923001111111");
        await SubmitOrderAsync(betaSlug, betaProduct, "Bilal", "+923002222222");

        var betaOrderId = await FirstOrderIdAsync(beta);

        // Alpha's own list shows only their order.
        var alphaList = await alpha.GetFromJsonAsync<PagedPayload<OrderListPayload>>("/api/v1/seller/orders");
        alphaList!.TotalCount.ShouldBe(1);
        alphaList.Items[0].CustomerName.ShouldBe("Ayesha");

        // And Beta's order is invisible by id — 404, not 403, so its existence is not confirmed.
        (await alpha.GetAsync($"/api/v1/seller/orders/{betaOrderId}"))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);

        (await alpha.PutAsJsonAsync($"/api/v1/seller/orders/{betaOrderId}/status",
            new { status = "Cancelled", note = (string?)null }))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);

        (await alpha.PostAsJsonAsync($"/api/v1/seller/orders/{betaOrderId}/notes", new { body = "snooping" }))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // Beta's order is untouched.
        var betaDetail = await beta.GetFromJsonAsync<OrderDetailPayload>($"/api/v1/seller/orders/{betaOrderId}");
        betaDetail!.Status.ShouldBe("New");
        betaDetail.Notes.ShouldBeEmpty();
    }

    [Fact]
    public async Task SellerCannotSeeAnotherSellersCustomers()
    {
        var (alpha, alphaSlug, alphaProduct) = await SetUpStoreWithOrderAsync("cust-a@example.com", "cust-alpha");
        var (beta, betaSlug, betaProduct) = await SetUpStoreWithOrderAsync("cust-b@example.com", "cust-beta");
        using var _1 = alpha;
        using var _2 = beta;

        // The same person orders from both sellers.
        await SubmitOrderAsync(alphaSlug, alphaProduct, "Ayesha", "+923001234567");
        await SubmitOrderAsync(betaSlug, betaProduct, "Ayesha", "+923001234567");

        var betaCustomers = await beta.GetFromJsonAsync<PagedPayload<CustomerListPayload>>("/api/v1/seller/customers");
        var alphaCustomers = await alpha.GetFromJsonAsync<PagedPayload<CustomerListPayload>>("/api/v1/seller/customers");

        // Two separate records, one per store. Neither seller learns the other has her too.
        alphaCustomers!.TotalCount.ShouldBe(1);
        betaCustomers!.TotalCount.ShouldBe(1);
        alphaCustomers.Items[0].Id.ShouldNotBe(betaCustomers.Items[0].Id);
        alphaCustomers.Items[0].OrderCount.ShouldBe(1);

        (await alpha.GetAsync($"/api/v1/seller/customers/{betaCustomers.Items[0].Id}"))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task OrderEndpointsRejectAnonymousCallers()
    {
        (await Client.GetAsync("/api/v1/seller/orders")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await Client.GetAsync("/api/v1/seller/customers")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    private static async Task<Guid> FirstOrderIdAsync(HttpClient seller)
    {
        var list = await seller.GetFromJsonAsync<PagedPayload<OrderListPayload>>("/api/v1/seller/orders");
        return list!.Items[0].Id;
    }
}

public sealed record OrderListPayload(
    Guid Id,
    int OrderNumber,
    string Status,
    string PaymentStatus,
    decimal? TotalAmount,
    string CustomerName,
    string CustomerPhone,
    string ProductSummary,
    int ItemCount);

public sealed record OrderDetailPayload(
    Guid Id,
    int OrderNumber,
    string Status,
    string PaymentStatus,
    decimal? TotalAmount,
    bool IsFinal,
    OrderCustomerPayload Customer,
    List<OrderItemPayload> Items,
    List<HistoryPayload> History,
    List<NotePayload> Notes);

public sealed record OrderCustomerPayload(Guid Id, string Name, string Phone, int TotalOrders);

public sealed record OrderItemPayload(
    Guid Id,
    string ProductName,
    int Quantity,
    List<AnswerPayload> Answers);

public sealed record AnswerPayload(string Label, string FieldType, string? Value, string? ImageUrl);

public sealed record HistoryPayload(
    string? FromStatus,
    string ToStatus,
    string? ChangedBy,
    string? Note,
    DateTimeOffset CreatedAt);

public sealed record NotePayload(Guid Id, string Body, string Author, DateTimeOffset CreatedAt);

public sealed record CountsPayload(
    int New, int Confirmed, int InProgress, int ReadyForDelivery, int Completed, int Cancelled, int Total);

public sealed record CustomerListPayload(
    Guid Id, string Name, string Phone, string? Email, int OrderCount, DateTimeOffset? LastOrderAt, decimal? TotalSpent);

public sealed record CustomerDetailPayload(
    Guid Id, string Name, string Phone, int OrderCount, decimal? TotalSpent, List<CustomerOrderPayload> Orders);

public sealed record CustomerOrderPayload(Guid Id, int OrderNumber, string Status, decimal? TotalAmount);
