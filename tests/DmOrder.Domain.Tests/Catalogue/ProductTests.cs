using DmOrder.Domain.Catalogue;
using DmOrder.Domain.Exceptions;

namespace DmOrder.Domain.Tests.Catalogue;

public class ProductTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 19, 12, 0, 0, TimeSpan.Zero);

    private static Product CreateProduct() =>
        Product.Create(Guid.CreateVersion7(), "Custom Birthday Cake", "custom-birthday-cake", null, 0);

    private static void Update(Product product, decimal? price = null, bool priceIsFrom = false) =>
        product.Update("Custom Birthday Cake", "custom-birthday-cake", null, price, priceIsFrom, null, true, true);

    [Fact]
    public void NewProductIsActiveAndAcceptsOrders()
    {
        var product = CreateProduct();

        product.IsActive.ShouldBeTrue();
        product.AcceptsCustomOrder.ShouldBeTrue();
        product.Price.ShouldBeNull("custom work often has no fixed price yet");
    }

    [Fact]
    public void PriceMayBeOmitted()
    {
        // A tailor or baker quoting per job must not be forced to invent a number.
        var product = CreateProduct();

        Should.NotThrow(() => Update(product, price: null));
        product.Price.ShouldBeNull();
    }

    [Fact]
    public void RejectsANegativePrice() =>
        Should.Throw<BusinessRuleException>(() => Update(CreateProduct(), price: -1m));

    [Fact]
    public void RejectsPriceStartsFromWithoutAPrice()
    {
        // "from" with nothing after it renders as a bare word on the storefront.
        var exception = Should.Throw<BusinessRuleException>(
            () => Update(CreateProduct(), price: null, priceIsFrom: true));

        exception.Message.ShouldContain("starting price");
    }

    [Fact]
    public void AcceptsPriceStartsFromWithAPrice()
    {
        var product = CreateProduct();

        Update(product, price: 3000m, priceIsFrom: true);

        product.Price.ShouldBe(3000m);
        product.PriceIsFrom.ShouldBeTrue();
    }

    [Fact]
    public void SlugIsLowercased()
    {
        var product = Product.Create(Guid.CreateVersion7(), "Cake", "Custom-CAKE", null, 0);

        product.Slug.ShouldBe("custom-cake");
    }

    [Fact]
    public void RejectsAMalformedSlug() =>
        Should.Throw<BusinessRuleException>(
            () => Product.Create(Guid.CreateVersion7(), "Cake", "not a slug!", null, 0));

    [Fact]
    public void DeletingIsSoftAndDeactivates()
    {
        // Orders reference products, so a hard delete would break order history.
        var product = CreateProduct();

        product.Delete(Now);

        product.IsDeleted.ShouldBeTrue();
        product.IsActive.ShouldBeFalse();
        product.DeletedAt.ShouldBe(Now);
    }

    [Fact]
    public void DeletingTwiceKeepsTheFirstTimestamp()
    {
        var product = CreateProduct();

        product.Delete(Now);
        product.Delete(Now.AddDays(1));

        product.DeletedAt.ShouldBe(Now);
    }

    [Fact]
    public void ImagesAreOrderedAsTheyAreAdded()
    {
        var product = CreateProduct();
        var first = Guid.CreateVersion7();
        var second = Guid.CreateVersion7();

        product.AddImage(first, "Front");
        product.AddImage(second, null);

        product.Images.Count.ShouldBe(2);
        product.Images[0].DisplayOrder.ShouldBe(0);
        product.Images[1].DisplayOrder.ShouldBe(1);
    }

    [Fact]
    public void AddingTheSameImageTwiceIsANoOp()
    {
        var product = CreateProduct();
        var mediaId = Guid.CreateVersion7();

        product.AddImage(mediaId, null);
        product.AddImage(mediaId, null);

        product.Images.Count.ShouldBe(1);
    }

    [Fact]
    public void RejectsMoreThanTheImageLimit()
    {
        var product = CreateProduct();

        for (var i = 0; i < Product.MaxImages; i++)
        {
            product.AddImage(Guid.CreateVersion7(), null);
        }

        Should.Throw<BusinessRuleException>(() => product.AddImage(Guid.CreateVersion7(), null));
    }

    [Fact]
    public void RemovingAnImageClosesTheGapInOrdering()
    {
        var product = CreateProduct();
        product.AddImage(Guid.CreateVersion7(), null);
        product.AddImage(Guid.CreateVersion7(), null);
        product.AddImage(Guid.CreateVersion7(), null);

        product.RemoveImage(product.Images[1].Id);

        product.Images.Count.ShouldBe(2);
        product.Images.Select(i => i.DisplayOrder).OrderBy(o => o).ShouldBe([0, 1]);
    }

    [Fact]
    public void ReorderingPutsTheNamedImagesFirst()
    {
        var product = CreateProduct();
        product.AddImage(Guid.CreateVersion7(), null);
        product.AddImage(Guid.CreateVersion7(), null);
        product.AddImage(Guid.CreateVersion7(), null);

        var last = product.Images[2].Id;
        product.ReorderImages([last]);

        // The first image is what a listing shows, so this is an editorial decision, not cosmetic.
        product.Images.Single(i => i.Id == last).DisplayOrder.ShouldBe(0);
        product.Images.Select(i => i.DisplayOrder).OrderBy(o => o).ShouldBe([0, 1, 2]);
    }

    [Fact]
    public void ReorderingRejectsAnImageFromAnotherProduct()
    {
        var product = CreateProduct();
        product.AddImage(Guid.CreateVersion7(), null);

        // Silently ignoring it would half-apply the reorder and look like a bug to the seller.
        Should.Throw<NotFoundException>(() => product.ReorderImages([Guid.CreateVersion7()]));
    }
}

public class CategoryTests
{
    [Fact]
    public void DeletingIsSoftAndDeactivates()
    {
        var category = Category.Create(Guid.CreateVersion7(), "Cakes", "cakes", 0);

        category.Delete(new DateTimeOffset(2026, 9, 19, 0, 0, 0, TimeSpan.Zero));

        category.IsDeleted.ShouldBeTrue();
        category.IsActive.ShouldBeFalse();
    }

    [Fact]
    public void RejectsAnEmptyName() =>
        Should.Throw<BusinessRuleException>(() => Category.Create(Guid.CreateVersion7(), "  ", "cakes", 0));
}

public class CatalogueSlugTests
{
    [Theory]
    [InlineData("Custom Birthday Cake", "custom-birthday-cake")]
    [InlineData("  Bridal Lehenga  ", "bridal-lehenga")]
    [InlineData("Roses & Lilies", "roses-lilies")]
    [InlineData("Size 12 — Red", "size-12-red")]
    public void SuggestsAUsableSlug(string name, string expected)
    {
        var slug = CatalogueSlug.Suggest(name);

        slug.ShouldBe(expected);
        CatalogueSlug.IsValid(slug).ShouldBeTrue();
    }

    [Fact]
    public void SuggestionFromPunctuationOnlyIsEmptyAndInvalid()
    {
        // The handler turns this into a clear message rather than creating an unreachable product.
        var slug = CatalogueSlug.Suggest("!!!");

        slug.ShouldBeEmpty();
        CatalogueSlug.IsValid(slug).ShouldBeFalse();
    }

    [Fact]
    public void LongNamesAreTruncatedWithoutATrailingHyphen()
    {
        var slug = CatalogueSlug.Suggest(string.Join(" ", Enumerable.Repeat("cake", 40)));

        slug.Length.ShouldBeLessThanOrEqualTo(CatalogueSlug.MaxLength);
        slug.ShouldNotEndWith("-");
        CatalogueSlug.IsValid(slug).ShouldBeTrue();
    }
}
