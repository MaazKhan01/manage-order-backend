using DmOrder.Domain.Exceptions;
using DmOrder.Domain.Stores;

namespace DmOrder.Domain.Tests.Stores;

public class StoreTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 19, 12, 0, 0, TimeSpan.Zero);

    private static Store CreateStore() => Store.Create(Guid.CreateVersion7(), "Sarah's Cakes", "sarah-cakes", "PKR");

    [Fact]
    public void NewStoreIsUnpublishedAndActive()
    {
        var store = CreateStore();

        store.IsPublished.ShouldBeFalse();
        store.IsActive.ShouldBeTrue();
        store.IsVisibleToPublic.ShouldBeFalse();
    }

    [Fact]
    public void NewStoreGetsADefaultTheme()
    {
        var store = CreateStore();

        store.Theme.ShouldNotBeNull();
        store.Theme.PrimaryColor.ShouldBe(StoreTheme.DefaultPrimary);
        store.Theme.LayoutVariant.ShouldBe(StoreLayoutVariant.Grid);
    }

    [Fact]
    public void SlugIsLowercased()
    {
        var store = Store.Create(Guid.CreateVersion7(), "Noor", "NOOR-Jewellery", null);

        store.Slug.ShouldBe("noor-jewellery");
    }

    [Fact]
    public void UnsupportedCurrencyFallsBackToTheDefault()
    {
        var store = Store.Create(Guid.CreateVersion7(), "Noor", "noor-jewellery", "XYZ");

        store.Currency.ShouldBe(SupportedCurrencies.Default);
    }

    [Fact]
    public void CannotCreateAStoreOnAReservedSlug() =>
        Should.Throw<BusinessRuleException>(() => Store.Create(Guid.CreateVersion7(), "Admin", "admin", null));

    [Fact]
    public void CannotPublishWithoutAContactChannel()
    {
        // A storefront nobody can order from is worse than no storefront.
        var store = CreateStore();

        var exception = Should.Throw<BusinessRuleException>(() => store.Publish(Now));

        exception.Message.ShouldContain("reach you");
        store.IsPublished.ShouldBeFalse();
    }

    [Theory]
    [InlineData("0300 1234567", null, null)]
    [InlineData(null, "923001234567", null)]
    [InlineData(null, null, "sarah@example.com")]
    public void PublishesWithAnySingleContactChannel(string? phone, string? whatsApp, string? email)
    {
        var store = CreateStore();
        store.UpdateProfile("Sarah's Cakes", null, phone, whatsApp, email,
            null, null, null, null, null, null, null);

        store.Publish(Now);

        store.IsPublished.ShouldBeTrue();
        store.PublishedAt.ShouldBe(Now);
        store.IsVisibleToPublic.ShouldBeTrue();
    }

    [Fact]
    public void PublishingTwiceKeepsTheOriginalTimestamp()
    {
        var store = CreateStore();
        store.UpdateProfile("Sarah's Cakes", null, "0300 1234567", null, null,
            null, null, null, null, null, null, null);

        store.Publish(Now);
        store.Publish(Now.AddDays(3));

        store.PublishedAt.ShouldBe(Now);
    }

    [Fact]
    public void ADeactivatedStoreCannotBePublishedByItsOwner()
    {
        // IsActive is the admin's switch. If publishing could override it, suspension would be
        // meaningless.
        var store = CreateStore();
        store.UpdateProfile("Sarah's Cakes", null, "0300 1234567", null, null,
            null, null, null, null, null, null, null);
        store.SetActive(false);

        Should.Throw<BusinessRuleException>(() => store.Publish(Now));
    }

    [Fact]
    public void DeactivatingHidesAnAlreadyPublishedStore()
    {
        var store = CreateStore();
        store.UpdateProfile("Sarah's Cakes", null, "0300 1234567", null, null,
            null, null, null, null, null, null, null);
        store.Publish(Now);

        store.SetActive(false);

        store.IsPublished.ShouldBeTrue("the seller's own setting is untouched");
        store.IsVisibleToPublic.ShouldBeFalse("but the storefront must go dark immediately");
    }

    [Fact]
    public void CannotRemoveTheLastContactChannelFromAPublishedStore()
    {
        var store = CreateStore();
        store.UpdateProfile("Sarah's Cakes", null, "0300 1234567", null, null,
            null, null, null, null, null, null, null);
        store.Publish(Now);

        Should.Throw<BusinessRuleException>(() =>
            store.UpdateProfile("Sarah's Cakes", null, null, null, null,
                null, null, null, null, null, null, null));
    }

    [Fact]
    public void BlankProfileFieldsAreStoredAsNullNotEmptyStrings()
    {
        var store = CreateStore();

        store.UpdateProfile("Sarah's Cakes", "   ", "0300 1234567", "  ", null,
            null, null, null, "  ", null, null, null);

        store.Description.ShouldBeNull();
        store.WhatsApp.ShouldBeNull();
        store.AddressText.ShouldBeNull();
    }
}

public class StoreThemeTests
{
    [Fact]
    public void RejectsAColourThatIsNotHex()
    {
        // Theme colours are injected into CSS custom properties, so anything that is not a plain hex
        // value is a stylesheet injection.
        var theme = StoreTheme.CreateDefault(Guid.CreateVersion7());

        Should.Throw<BusinessRuleException>(() => theme.Update(
            "red; } body { display:none",
            StoreTheme.DefaultAccent,
            StoreTheme.DefaultBackground,
            StoreFontChoice.Sans,
            StoreButtonStyle.Rounded,
            StoreLayoutVariant.Grid));
    }

    [Theory]
    [InlineData("#fff")]
    [InlineData("1A2B3C")]
    [InlineData("#12345G")]
    [InlineData("")]
    public void RejectsMalformedHexColours(string colour)
    {
        var theme = StoreTheme.CreateDefault(Guid.CreateVersion7());

        Should.Throw<BusinessRuleException>(() => theme.Update(
            colour, StoreTheme.DefaultAccent, StoreTheme.DefaultBackground,
            StoreFontChoice.Sans, StoreButtonStyle.Rounded, StoreLayoutVariant.Grid));
    }

    [Fact]
    public void RejectsAnEnumValueCastFromAnArbitraryInteger()
    {
        var theme = StoreTheme.CreateDefault(Guid.CreateVersion7());

        Should.Throw<BusinessRuleException>(() => theme.Update(
            StoreTheme.DefaultPrimary, StoreTheme.DefaultAccent, StoreTheme.DefaultBackground,
            (StoreFontChoice)99, StoreButtonStyle.Rounded, StoreLayoutVariant.Grid));
    }

    [Fact]
    public void NormalisesHexToUppercase()
    {
        var theme = StoreTheme.CreateDefault(Guid.CreateVersion7());

        theme.Update("#aabbcc", "#ddeeff", "#ffffff",
            StoreFontChoice.Serif, StoreButtonStyle.Pill, StoreLayoutVariant.Showcase);

        theme.PrimaryColor.ShouldBe("#AABBCC");
        theme.FontChoice.ShouldBe(StoreFontChoice.Serif);
        theme.LayoutVariant.ShouldBe(StoreLayoutVariant.Showcase);
    }
}
