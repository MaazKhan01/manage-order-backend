using System.Net;
using System.Net.Http.Json;
using ClosedXML.Excel;
using Shouldly;

namespace DmOrder.Api.IntegrationTests.Orders;

/// <summary>
/// Orders as a spreadsheet.
///
/// The thing worth asserting is not that a file comes back - it is that the file is usable: that the
/// seller's own questions became columns, and that a reference like OV-2026-00123 survives the round
/// trip as text rather than being eaten by Excel's type guessing.
/// </summary>
public sealed class ExportOrdersTests(ApiFactory factory) : IntegrationTestBase(factory)
{
    private const string Route = "/api/v1/seller/orders/export";

    [Fact]
    public async Task Exporting_requires_authentication()
    {
        (await Client.GetAsync(Route)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task A_seller_gets_a_spreadsheet_of_their_own_orders()
    {
        var (client, _) = await SetUpWithOrderAsync();

        var response = await client.GetAsync(Route);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType
            .ShouldBe("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");

        // The filename is what makes a weekly download a folder of files rather than orders(3).xlsx.
        (response.Content.Headers.ContentDisposition!.FileName ?? string.Empty).ShouldContain(".xlsx");
    }

    [Fact]
    public async Task The_sellers_own_questions_become_columns()
    {
        var (client, _) = await SetUpWithOrderAsync();

        var sheet = await DownloadAsync(client);

        var headers = HeaderRow(sheet);

        // Asked for in SetUp, so it must be a column even though this order answered it.
        headers.ShouldContain("Fabric");
        headers.ShouldContain("Reference");
        headers.ShouldContain("Customer");
    }

    [Fact]
    public async Task An_answer_lands_under_its_own_question()
    {
        var (client, _) = await SetUpWithOrderAsync();

        var sheet = await DownloadAsync(client);

        var headers = HeaderRow(sheet);
        var column = headers.IndexOf("Fabric") + 1;

        sheet.Cell(2, column).GetString().ShouldBe("Raw silk");
    }

    [Fact]
    public async Task A_reference_survives_as_text_rather_than_being_reinterpreted()
    {
        var (client, _) = await SetUpWithOrderAsync();

        var sheet = await DownloadAsync(client);

        var column = HeaderRow(sheet).IndexOf("Reference") + 1;
        var cell = sheet.Cell(2, column);

        // The whole reason for writing a typed workbook instead of CSV: left to itself Excel will
        // happily turn this into a date or a formula depending on the machine's locale.
        cell.DataType.ShouldBe(XLDataType.Text);
        cell.GetString().ShouldStartWith("DM-");
    }

    [Fact]
    public async Task The_filter_the_seller_is_looking_at_is_the_one_they_export()
    {
        var (client, _) = await SetUpWithOrderAsync();

        // The order created in SetUp is New, so filtering to Completed must return a header row and
        // nothing else - not the whole book.
        var sheet = await DownloadAsync(client, "?status=Completed");

        sheet.LastRowUsed()!.RowNumber().ShouldBe(1);
    }

    [Fact]
    public async Task One_seller_never_sees_another_sellers_orders()
    {
        var (_, _) = await SetUpWithOrderAsync();

        var other = await RegisterSellerAsync("export-other@example.com");
        var otherClient = CreateAuthenticatedClient(other.AccessToken);
        (await otherClient.PostAsJsonAsync(
            "/api/v1/seller/store",
            new { name = "Other Store", slug = "export-other-store", currency = "PKR" }))
            .EnsureSuccessStatusCode();

        var sheet = await DownloadAsync(otherClient);

        // Their own store, their own empty book.
        sheet.LastRowUsed()!.RowNumber().ShouldBe(1);
    }

    private static List<string> HeaderRow(IXLWorksheet sheet) =>
        [.. sheet.Row(1).CellsUsed().Select(c => c.GetString())];

    private static async Task<IXLWorksheet> DownloadAsync(HttpClient client, string query = "")
    {
        var response = await client.GetAsync(Route + query);
        response.EnsureSuccessStatusCode();

        var bytes = await response.Content.ReadAsByteArrayAsync();
        var workbook = new XLWorkbook(new MemoryStream(bytes));
        return workbook.Worksheet(1);
    }

    private async Task<(HttpClient Client, Guid ProductId)> SetUpWithOrderAsync()
    {
        var seller = await RegisterSellerAsync("export@example.com");
        var client = CreateAuthenticatedClient(seller.AccessToken);

        (await client.PostAsJsonAsync(
            "/api/v1/seller/store",
            new { name = "Export Store", slug = "export-store", currency = "PKR" }))
            .EnsureSuccessStatusCode();

        var field = await client.PostAsJsonAsync(
            "/api/v1/seller/custom-fields",
            new { label = "Fabric", fieldType = "Text", isRequired = false });
        field.EnsureSuccessStatusCode();

        var created = await client.PostAsJsonAsync(
            "/api/v1/seller/products",
            new { name = "Silk Scarf" });
        created.EnsureSuccessStatusCode();
        var product = (await created.Content.ReadFromJsonAsync<ProductPayload>())!;

        var fieldId = (await field.Content.ReadFromJsonAsync<FieldPayload>())!.Id;

        (await client.PostAsJsonAsync("/api/v1/seller/orders", new
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
            answers = new[] { new { fieldId, value = "Raw silk", values = (string[]?)null, mediaId = (Guid?)null } },
        })).EnsureSuccessStatusCode();

        return (client, product.Id);
    }

    private sealed record ProductPayload(Guid Id);

    private sealed record FieldPayload(Guid Id);
}
