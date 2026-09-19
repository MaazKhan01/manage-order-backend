using DmOrder.Domain.CustomFields;

namespace DmOrder.Domain.Tests.CustomFields;

public class CustomFieldValidatorTests
{
    private static readonly Guid StoreId = Guid.CreateVersion7();

    private static CustomField Field(
        string label,
        CustomFieldType type,
        bool required = false,
        Action<CustomField>? configure = null)
    {
        var field = CustomField.Create(StoreId, null, label, type, 0);
        field.Update(label, null, type, required, null, null, null);
        configure?.Invoke(field);
        return field;
    }

    private static CustomFieldAnswer Answer(CustomField field, string? value) =>
        new(field.Id, value, null, null);

    [Fact]
    public void RequiredFieldWithNoAnswerFails()
    {
        var field = Field("Delivery date", CustomFieldType.Date, required: true);

        var result = CustomFieldValidator.Validate([field], []);

        result.IsValid.ShouldBeFalse();
        result.Errors.Single().Message.ShouldContain("Delivery date");
    }

    [Fact]
    public void OptionalFieldWithNoAnswerIsSimplyAbsent()
    {
        var field = Field("Notes", CustomFieldType.LongText);

        var result = CustomFieldValidator.Validate([field], []);

        result.IsValid.ShouldBeTrue();
        result.Answers.ShouldBeEmpty();
    }

    [Fact]
    public void AnswersToFieldsTheSellerNeverDefinedAreIgnored()
    {
        var field = Field("Size", CustomFieldType.Text);

        // A hand-crafted payload must not be able to add data to an order.
        var result = CustomFieldValidator.Validate(
            [field],
            [new CustomFieldAnswer(Guid.CreateVersion7(), "injected", null, null)]);

        result.IsValid.ShouldBeTrue();
        result.Answers.ShouldBeEmpty();
    }

    [Fact]
    public void DeletedFieldsAreNotAsked()
    {
        var field = Field("Old question", CustomFieldType.Text, required: true);
        field.Delete(DateTimeOffset.UtcNow);

        var result = CustomFieldValidator.Validate([field], []);

        result.IsValid.ShouldBeTrue();
    }

    // --- Number ----------------------------------------------------------

    [Fact]
    public void NumberRejectsNonNumericText()
    {
        var field = Field("Bust (inches)", CustomFieldType.Number, required: true);

        var result = CustomFieldValidator.Validate([field], [Answer(field, "about 34")]);

        result.IsValid.ShouldBeFalse();
        result.Errors.Single().Message.ShouldContain("number");
    }

    [Fact]
    public void NumberEnforcesTheSellersRange()
    {
        var field = Field("Tiers", CustomFieldType.Number, required: true);
        field.Update("Tiers", null, CustomFieldType.Number, true, 1, 5, null);

        CustomFieldValidator.Validate([field], [Answer(field, "0")]).IsValid.ShouldBeFalse();
        CustomFieldValidator.Validate([field], [Answer(field, "6")]).IsValid.ShouldBeFalse();
        CustomFieldValidator.Validate([field], [Answer(field, "3")]).IsValid.ShouldBeTrue();
    }

    [Fact]
    public void NumberAcceptsDecimals()
    {
        var field = Field("Weight (kg)", CustomFieldType.Number, required: true);

        var result = CustomFieldValidator.Validate([field], [Answer(field, "1.5")]);

        result.IsValid.ShouldBeTrue();
        result.Answers.Single().Number.ShouldBe(1.5m);
    }

    // --- Text ------------------------------------------------------------

    [Fact]
    public void TextIsCappedEvenWhenTheSellerSetNoLimit()
    {
        // Unbounded text on an anonymous endpoint is a denial-of-service surface.
        var field = Field("Colour", CustomFieldType.Text, required: true);
        var tooLong = new string('a', CustomFieldValidator.DefaultMaxTextLength + 1);

        CustomFieldValidator.Validate([field], [Answer(field, tooLong)]).IsValid.ShouldBeFalse();
    }

    [Fact]
    public void TextRespectsTheSellersOwnLimit()
    {
        var field = Field("Cake message", CustomFieldType.Text);
        field.Update("Cake message", null, CustomFieldType.Text, true, null, null, 20);

        CustomFieldValidator.Validate([field], [Answer(field, new string('a', 21))]).IsValid.ShouldBeFalse();
        CustomFieldValidator.Validate([field], [Answer(field, "Happy Birthday!")]).IsValid.ShouldBeTrue();
    }

    // --- Choices ---------------------------------------------------------

    [Fact]
    public void SelectRejectsAValueNotOnTheSellersList()
    {
        var field = Field("Flavour", CustomFieldType.Select, required: true,
            configure: f => f.ReplaceOptions([("Chocolate", null), ("Vanilla", null)]));

        // The form only offered two options, but the submission is untrusted.
        var result = CustomFieldValidator.Validate([field], [Answer(field, "Gold Leaf")]);

        result.IsValid.ShouldBeFalse();
    }

    [Fact]
    public void SelectMatchesCaseInsensitively()
    {
        var field = Field("Flavour", CustomFieldType.Select, required: true,
            configure: f => f.ReplaceOptions([("Chocolate", null)]));

        var result = CustomFieldValidator.Validate([field], [Answer(field, "chocolate")]);

        result.IsValid.ShouldBeTrue();
        result.Answers.Single().Text.ShouldBe("Chocolate", "the seller's own casing is what gets stored");
    }

    [Fact]
    public void MultiSelectRejectsTheWholeAnswerIfAnyChoiceIsInvalid()
    {
        var field = Field("Flowers", CustomFieldType.MultiSelect, required: true,
            configure: f => f.ReplaceOptions([("Roses", null), ("Lilies", null)]));

        var result = CustomFieldValidator.Validate(
            [field],
            [new CustomFieldAnswer(field.Id, null, ["Roses", "Orchids"], null)]);

        result.IsValid.ShouldBeFalse();
    }

    [Fact]
    public void MultiSelectDeduplicatesAndKeepsTheSellersCasing()
    {
        var field = Field("Flowers", CustomFieldType.MultiSelect, required: true,
            configure: f => f.ReplaceOptions([("Roses", null), ("Lilies", null)]));

        var result = CustomFieldValidator.Validate(
            [field],
            [new CustomFieldAnswer(field.Id, null, ["roses", "Roses", "Lilies"], null)]);

        result.IsValid.ShouldBeTrue();
        result.Answers.Single().Choices.ShouldBe(["Roses", "Lilies"]);
    }

    // --- Boolean, date, time ---------------------------------------------

    [Theory]
    [InlineData("true")]
    [InlineData("yes")]
    [InlineData("on")]
    [InlineData("1")]
    public void BooleanAcceptsWhatFormsActuallySend(string value)
    {
        var field = Field("Eggless", CustomFieldType.Boolean, required: true);

        var result = CustomFieldValidator.Validate([field], [Answer(field, value)]);

        result.IsValid.ShouldBeTrue();
        result.Answers.Single().Boolean.ShouldBe(true);
    }

    [Fact]
    public void BooleanRejectsAnythingElse()
    {
        var field = Field("Eggless", CustomFieldType.Boolean, required: true);

        CustomFieldValidator.Validate([field], [Answer(field, "maybe")]).IsValid.ShouldBeFalse();
    }

    [Fact]
    public void DateRejectsNonsense()
    {
        var field = Field("Delivery date", CustomFieldType.Date, required: true);

        CustomFieldValidator.Validate([field], [Answer(field, "next Tuesday")]).IsValid.ShouldBeFalse();
        CustomFieldValidator.Validate([field], [Answer(field, "2026-12-25")]).IsValid.ShouldBeTrue();
    }

    [Fact]
    public void TimeRejectsNonsense()
    {
        var field = Field("Delivery time", CustomFieldType.Time, required: true);

        CustomFieldValidator.Validate([field], [Answer(field, "afternoon")]).IsValid.ShouldBeFalse();
        CustomFieldValidator.Validate([field], [Answer(field, "16:30")]).IsValid.ShouldBeTrue();
    }

    // --- The point of the whole design -----------------------------------

    [Fact]
    public void TheSameEngineServesABakerAndATailorAndAFlorist()
    {
        // If this ever needs a branch per trade, the custom-field design has failed.
        var eggless = Field("Eggless", CustomFieldType.Boolean, required: true);
        var flavour = Field("Flavour", CustomFieldType.Select, required: true,
            configure: f => f.ReplaceOptions([("Chocolate", null), ("Vanilla", null)]));
        var message = Field("Message on cake", CustomFieldType.Text);

        var bust = Field("Bust (inches)", CustomFieldType.Number, required: true);
        var fabric = Field("Fabric", CustomFieldType.Text, required: true);

        var flowers = Field("Flower types", CustomFieldType.MultiSelect, required: true,
            configure: f => f.ReplaceOptions([("Roses", null), ("Lilies", null), ("Tulips", null)]));
        var deliverOn = Field("Deliver on", CustomFieldType.Date, required: true);

        var fields = new[] { eggless, flavour, message, bust, fabric, flowers, deliverOn };

        var result = CustomFieldValidator.Validate(fields,
        [
            Answer(eggless, "yes"),
            Answer(flavour, "Chocolate"),
            Answer(message, "Happy Birthday Ayesha"),
            Answer(bust, "34"),
            Answer(fabric, "Raw silk"),
            new CustomFieldAnswer(flowers.Id, null, ["Roses", "Tulips"], null),
            Answer(deliverOn, "2026-12-25"),
        ]);

        result.IsValid.ShouldBeTrue();
        result.Answers.Count.ShouldBe(7);
        result.Answers.Single(a => a.Field.Label == "Eggless").Boolean.ShouldBe(true);
        result.Answers.Single(a => a.Field.Label == "Bust (inches)").Number.ShouldBe(34m);
        result.Answers.Single(a => a.Field.Label == "Flower types").Choices!.Count.ShouldBe(2);
        result.Answers.Single(a => a.Field.Label == "Deliver on").Date.ShouldBe(new DateOnly(2026, 12, 25));
    }

    [Fact]
    public void EveryMissingRequiredFieldIsReportedAtOnce()
    {
        // A customer should not have to submit five times to discover five problems.
        var a = Field("Size", CustomFieldType.Text, required: true);
        var b = Field("Colour", CustomFieldType.Text, required: true);
        var c = Field("Deliver on", CustomFieldType.Date, required: true);

        var result = CustomFieldValidator.Validate([a, b, c], []);

        result.Errors.Count.ShouldBe(3);
    }
}
