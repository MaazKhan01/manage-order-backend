using DmOrder.Domain.Exceptions;
using DmOrder.Domain.Orders;
using Shouldly;

namespace DmOrder.Domain.Tests.Orders;

public class OrderReferenceTests
{
    [Fact]
    public void GeneratesAWellFormedReference()
    {
        var reference = OrderReference.Generate("DM", 2026);

        reference.ShouldStartWith("DM-2026-");
        reference.Length.ShouldBe("DM-2026-".Length + 6);
        OrderReference.IsWellFormed(reference).ShouldBeTrue();
    }

    [Fact]
    public void NeverUsesTheLettersPeopleMistype()
    {
        // I/1, L/1 and O/0 are the pairs that get confused reading a code aloud, so they are not in
        // the alphabet at all. A thousand references is enough to catch a bad alphabet.
        var suffixes = Enumerable.Range(0, 1000)
            .Select(_ => OrderReference.Generate("DM", 2026).Split('-')[2]);

        foreach (var suffix in suffixes)
        {
            suffix.ShouldNotContain("I");
            suffix.ShouldNotContain("L");
            suffix.ShouldNotContain("O");
            suffix.ShouldNotContain("U");
        }
    }

    [Fact]
    public void IsNotSequential()
    {
        // The whole point of the suffix. If this ever starts failing, someone has replaced the
        // generator with a counter and made every order guessable from its neighbour.
        var references = Enumerable.Range(0, 200)
            .Select(_ => OrderReference.Generate("DM", 2026))
            .ToList();

        references.Distinct().Count().ShouldBe(references.Count);
    }

    [Theory]
    // Lower case, spaces and missing hyphens are all things a customer will actually type.
    [InlineData("dm-2026-k4p7qx", "DM-2026-K4P7QX")]
    [InlineData("DM2026K4P7QX", "DM-2026-K4P7QX")]
    [InlineData("  DM-2026-K4P7QX  ", "DM-2026-K4P7QX")]
    [InlineData("dm 2026 k4p7qx", "DM-2026-K4P7QX")]
    public void NormalisesWhatSomeoneActuallyTypes(string input, string expected)
    {
        OrderReference.Normalise(input).ShouldBe(expected);
    }

    [Theory]
    // The confusable characters are corrected rather than rejected: someone reading "0" as "O" off a
    // printed slip should still find their order.
    [InlineData("DM-2026-K4P7QO", "DM-2026-K4P7Q0")]
    [InlineData("DM-2026-K4P7QI", "DM-2026-K4P7Q1")]
    [InlineData("DM-2026-K4P7QL", "DM-2026-K4P7Q1")]
    public void CorrectsConfusableCharacters(string input, string expected)
    {
        OrderReference.Normalise(input).ShouldBe(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("NOTAREFERENCE")]
    [InlineData("DM-2026-K4P7Q")]      // suffix too short
    [InlineData("DM-2026-K4P7QXY")]    // suffix too long
    [InlineData("DM-26-K4P7QX")]       // year wrong length
    [InlineData("D-2026-K4P7QX")]      // prefix too short
    public void RejectsMalformedReferences(string? value)
    {
        OrderReference.IsWellFormed(value).ShouldBeFalse();
    }

    [Theory]
    [InlineData("D")]
    [InlineData("")]
    [InlineData("TOOLONGAPREFIX")]
    public void RefusesAnUnusablePrefix(string prefix)
    {
        Should.Throw<BusinessRuleException>(() => OrderReference.Generate(prefix, 2026));
    }
}
