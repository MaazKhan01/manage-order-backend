using DmOrder.Application.Common.Interfaces;
using DmOrder.Application.Features.Orders;
using DmOrder.Domain.CustomFields;
using Shouldly;

namespace DmOrder.Application.Tests.Features.Orders;

/// <summary>
/// The step where a model's suggestions become references to a real store's rows.
///
/// Everything here is about not trusting the reader. It returns names and labels; this decides
/// whether any of them correspond to something the seller actually has, and what to do when they
/// do not. A message written by a stranger reaches this code, so "what happens when the input is
/// wrong or hostile" is the whole point rather than an edge case.
/// </summary>
public sealed class OrderDraftMapperTests
{
    private static readonly Guid ToteId = Guid.CreateVersion7();

    private static readonly DraftProduct[] Catalogue =
    [
        new(ToteId, "Linen Tote Bag", 3500m),
        new(Guid.CreateVersion7(), "Cotton Scarf", 1200m),
    ];

    private static OrderDraft Draft(
        string? productName = null,
        int? quantity = null,
        string? customerName = "Ayesha",
        IReadOnlyList<DraftAnswer>? answers = null) =>
        new(productName, quantity, customerName, null, null, null, null, answers ?? [], null);

    [Fact]
    public void A_product_name_from_the_catalogue_resolves_to_its_id()
    {
        var result = OrderDraftMapper.Map(Draft("Linen Tote Bag"), Catalogue, [], "+923001234567");

        result.ProductId.ShouldBe(ToteId);
        result.ProductName.ShouldBe("Linen Tote Bag");
        result.Missing.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("linen tote bag")]
    [InlineData("LINEN TOTE BAG")]
    [InlineData("  Linen Tote Bag  ")]
    public void Matching_a_product_ignores_case_and_surrounding_space(string written)
    {
        // The model echoes what the customer wrote as often as what the catalogue says.
        OrderDraftMapper.Map(Draft(written), Catalogue, [], "+923001234567")
            .ProductId.ShouldBe(ToteId);
    }

    [Fact]
    public void A_product_the_seller_does_not_sell_becomes_a_one_off_rather_than_nothing()
    {
        // Custom work is a real order. "Three-tier wedding cake" is not in any catalogue and must
        // still reach the seller as something they can record.
        var result = OrderDraftMapper.Map(
            Draft("Three-tier wedding cake"), Catalogue, [], "+923001234567");

        result.ProductId.ShouldBeNull();
        result.ProductName.ShouldBe("Three-tier wedding cake");
        result.Missing.ShouldNotContain("product");
    }

    [Fact]
    public void No_product_at_all_is_reported_as_missing()
    {
        var result = OrderDraftMapper.Map(Draft(productName: null), Catalogue, [], "+923001234567");

        result.ProductId.ShouldBeNull();
        result.Missing.ShouldContain("product");
    }

    [Fact]
    public void A_name_that_merely_contains_a_product_name_does_not_match_it()
    {
        // Guards against a substring match being introduced later. "Not the Linen Tote Bag" must
        // not silently become an order for one.
        OrderDraftMapper.Map(Draft("Not the Linen Tote Bag"), Catalogue, [], "+923001234567")
            .ProductId.ShouldBeNull();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    [InlineData(1000)]
    public void A_quantity_the_form_would_reject_is_dropped_rather_than_shown(int quantity)
    {
        // The seller sees an empty box and fills it in. Showing an out-of-range number would only
        // produce a save that fails validation.
        OrderDraftMapper.Map(Draft("Linen Tote Bag", quantity), Catalogue, [], "+923001234567")
            .Quantity.ShouldBeNull();
    }

    [Fact]
    public void A_missing_phone_is_reported_because_it_is_how_the_order_is_tracked()
    {
        var result = OrderDraftMapper.Map(Draft("Linen Tote Bag"), Catalogue, [], null);

        result.CustomerPhone.ShouldBeNull();
        result.Missing.ShouldContain("customerPhone");
    }

    [Fact]
    public void A_missing_customer_name_is_reported()
    {
        var result = OrderDraftMapper.Map(
            Draft("Linen Tote Bag", customerName: "   "), Catalogue, [], "+923001234567");

        result.CustomerName.ShouldBeNull();
        result.Missing.ShouldContain("customerName");
    }

    [Fact]
    public void An_answer_matches_the_sellers_question_by_label()
    {
        var size = Question("Size", ["Small", "Large"]);

        var result = OrderDraftMapper.Map(
            Draft("Linen Tote Bag", answers: [new DraftAnswer("size", "Large")]),
            Catalogue,
            [size],
            "+923001234567");

        result.Answers.ShouldHaveSingleItem();
        result.Answers[0].FieldId.ShouldBe(size.Id);
        result.Answers[0].Label.ShouldBe("Size");
        result.Answers[0].Value.ShouldBe("Large");
    }

    [Fact]
    public void An_answer_to_a_question_the_seller_never_asked_is_dropped()
    {
        // The clearest case of not trusting the reader: it may invent a question entirely.
        var result = OrderDraftMapper.Map(
            Draft("Linen Tote Bag", answers: [new DraftAnswer("Discount code", "FREE100")]),
            Catalogue,
            [Question("Size", ["Small", "Large"])],
            "+923001234567");

        result.Answers.ShouldBeEmpty();
    }

    [Fact]
    public void An_option_the_seller_does_not_offer_is_dropped()
    {
        var result = OrderDraftMapper.Map(
            Draft("Linen Tote Bag", answers: [new DraftAnswer("Size", "Enormous")]),
            Catalogue,
            [Question("Size", ["Small", "Large"])],
            "+923001234567");

        result.Answers.ShouldBeEmpty();
    }

    [Fact]
    public void A_free_text_question_accepts_whatever_the_customer_wrote()
    {
        var note = Question("Message on the card", []);

        var result = OrderDraftMapper.Map(
            Draft("Linen Tote Bag", answers: [new DraftAnswer("Message on the card", "Happy birthday")]),
            Catalogue,
            [note],
            "+923001234567");

        result.Answers.ShouldHaveSingleItem();
        result.Answers[0].Value.ShouldBe("Happy birthday");
    }

    [Fact]
    public void Only_the_first_answer_to_a_question_is_kept()
    {
        var size = Question("Size", ["Small", "Large"]);

        var result = OrderDraftMapper.Map(
            Draft("Linen Tote Bag", answers:
            [
                new DraftAnswer("Size", "Small"),
                new DraftAnswer("Size", "Large"),
            ]),
            Catalogue,
            [size],
            "+923001234567");

        result.Answers.ShouldHaveSingleItem();
        result.Answers[0].Value.ShouldBe("Small");
    }

    [Fact]
    public void Blank_answers_are_not_offered()
    {
        var result = OrderDraftMapper.Map(
            Draft("Linen Tote Bag", answers: [new DraftAnswer("Message on the card", "   ")]),
            Catalogue,
            [Question("Message on the card", [])],
            "+923001234567");

        result.Answers.ShouldBeEmpty();
    }

    [Fact]
    public void A_store_with_an_empty_catalogue_still_produces_a_usable_draft()
    {
        var result = OrderDraftMapper.Map(Draft("A birthday cake"), [], [], "+923001234567");

        result.ProductId.ShouldBeNull();
        result.ProductName.ShouldBe("A birthday cake");
        result.Missing.ShouldBeEmpty();
    }

    private static CustomField Question(string label, string[] options)
    {
        var field = CustomField.Create(
            storeId: Guid.CreateVersion7(),
            productId: null,
            label: label,
            fieldType: options.Length > 0 ? CustomFieldType.Select : CustomFieldType.Text,
            displayOrder: 0);

        field.ReplaceOptions([.. options.Select(o => (Label: o, Value: (string?)o))]);

        return field;
    }
}
