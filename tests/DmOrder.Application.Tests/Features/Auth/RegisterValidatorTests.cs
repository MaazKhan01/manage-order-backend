using DmOrder.Application.Features.Auth;

namespace DmOrder.Application.Tests.Features.Auth;

public class RegisterValidatorTests
{
    private readonly RegisterValidator _validator = new();

    [Theory]
    [InlineData("")]
    [InlineData("not-an-email")]
    [InlineData("missing@tld")]
    public void RejectsInvalidEmail(string email)
    {
        var result = _validator.Validate(new RegisterRequest(email, "a-long-enough-password", "Sarah"));

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(e => e.PropertyName == nameof(RegisterRequest.Email));
    }

    [Fact]
    public void RejectsShortPassword()
    {
        var result = _validator.Validate(new RegisterRequest("sarah@example.com", "short", "Sarah"));

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(e => e.PropertyName == nameof(RegisterRequest.Password));
    }

    [Fact]
    public void RejectsOverlongPassword()
    {
        // The hash runs over whatever arrives, so an unbounded password is a cheap way to burn CPU.
        var password = new string('a', 129);

        var result = _validator.Validate(new RegisterRequest("sarah@example.com", password, "Sarah"));

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(e => e.PropertyName == nameof(RegisterRequest.Password));
    }

    [Fact]
    public void RejectsMissingDisplayName()
    {
        var result = _validator.Validate(new RegisterRequest("sarah@example.com", "a-long-enough-password", ""));

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(e => e.PropertyName == nameof(RegisterRequest.DisplayName));
    }

    [Fact]
    public void AcceptsAValidRegistration()
    {
        var result = _validator.Validate(
            new RegisterRequest("sarah@example.com", "a-long-enough-password", "Sarah's Cakes"));

        result.IsValid.ShouldBeTrue();
    }

    [Fact]
    public void DoesNotRequirePasswordComposition()
    {
        // Deliberate: length beats forced symbols, which mostly produce "Password1!". If this ever
        // fails, someone added composition rules without updating the reasoning.
        var result = _validator.Validate(
            new RegisterRequest("sarah@example.com", "correct horse battery staple", "Sarah"));

        result.IsValid.ShouldBeTrue();
    }
}

public class LoginValidatorTests
{
    private readonly LoginValidator _validator = new();

    [Fact]
    public void AcceptsAShortPassword()
    {
        // Login must not enforce the registration password rules: rejecting a short password here
        // would tell an attacker what the rules are, and the attempt should just fail instead.
        var result = _validator.Validate(new LoginRequest("sarah@example.com", "x"));

        result.IsValid.ShouldBeTrue();
    }

    [Fact]
    public void RejectsEmptyCredentials()
    {
        var result = _validator.Validate(new LoginRequest("", ""));

        result.IsValid.ShouldBeFalse();
        result.Errors.Count.ShouldBe(2);
    }
}
