using DmOrder.Domain.Stores;

namespace DmOrder.Domain.Tests.Stores;

public class StoreSlugTests
{
    [Theory]
    [InlineData("sarah-cakes")]
    [InlineData("noor")]
    [InlineData("threads-by-aisha")]
    [InlineData("bloom99")]
    [InlineData("a1b")]
    public void AcceptsValidSlugs(string slug) =>
        StoreSlug.Validate(slug).IsValid.ShouldBeTrue();

    [Theory]
    [InlineData("", "required")]
    [InlineData("ab", "too short")]
    [InlineData("-leading", "leading hyphen")]
    [InlineData("trailing-", "trailing hyphen")]
    [InlineData("double--hyphen", "doubled hyphen")]
    [InlineData("Upper-Case", "uppercase")]
    [InlineData("has space", "space")]
    [InlineData("under_score", "underscore")]
    [InlineData("emoji-🎂", "non-ascii")]
    [InlineData("dots.here", "dot")]
    public void RejectsMalformedSlugs(string slug, string why)
    {
        var result = StoreSlug.Validate(slug);

        result.IsValid.ShouldBeFalse($"'{slug}' should be rejected: {why}");
        result.Error.ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public void RejectsASlugLongerThanTheMaximum() =>
        StoreSlug.Validate(new string('a', StoreSlug.MaxLength + 1)).IsValid.ShouldBeFalse();

    [Theory]
    [InlineData("admin")]
    [InlineData("api")]
    [InlineData("dashboard")]
    [InlineData("login")]
    [InlineData("register")]
    [InlineData("setup")]
    [InlineData("_next")]
    [InlineData("www")]
    [InlineData("support")]
    public void RejectsReservedSlugs(string slug)
    {
        // Storefronts live at the URL root, so any of these would shadow a platform route.
        var result = StoreSlug.Validate(slug);

        result.IsValid.ShouldBeFalse($"'{slug}' is a platform route and must stay reserved");
    }

    [Fact]
    public void ReservedCheckIgnoresCase()
    {
        // The slug is lowercased before storage, but the reserved check must not be the thing that
        // depends on that having happened.
        StoreSlug.IsReserved("ADMIN").ShouldBeTrue();
        StoreSlug.IsReserved("Dashboard").ShouldBeTrue();
    }

    [Theory]
    [InlineData("Sarah's Cakes", "sarah-s-cakes")]
    [InlineData("  Threads by Aisha  ", "threads-by-aisha")]
    [InlineData("Bloom & Co.", "bloom-co")]
    [InlineData("Noor---Jewellery", "noor-jewellery")]
    public void SuggestsAUsableSlugFromAStoreName(string name, string expected)
    {
        var suggestion = StoreSlug.Suggest(name);

        suggestion.ShouldBe(expected);
        StoreSlug.Validate(suggestion).IsValid.ShouldBeTrue();
    }

    [Fact]
    public void SuggestionIsStillValidatedLikeAnyOtherInput()
    {
        // A name made entirely of punctuation suggests an empty slug, which must fail validation
        // rather than silently creating a store at the site root.
        var suggestion = StoreSlug.Suggest("!!!");

        suggestion.ShouldBeEmpty();
        StoreSlug.Validate(suggestion).IsValid.ShouldBeFalse();
    }
}
