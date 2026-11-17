using InviteCraft.Application.Features.Account;
using InviteCraft.Application.Features.Auth;

namespace InviteCraft.UnitTests.Auth;

public class AuthValidatorTests
{
    private readonly RegisterRequestValidator _register = new();

    [Theory]
    [InlineData("short1A", "at least 8")]
    [InlineData("alllowercase1", "uppercase")]
    [InlineData("ALLUPPERCASE1", "lowercase")]
    [InlineData("NoDigitsHere", "number")]
    public void Weak_passwords_are_rejected_with_a_helpful_message(string password, string expectedFragment)
    {
        var result = _register.Validate(new RegisterRequest("Priya Sharma", "priya@example.com", password, null));

        result.Errors.ShouldContain(e => e.PropertyName == "Password" && e.ErrorMessage.Contains(expectedFragment));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("+919876543210")]
    [InlineData("9876543210")]
    public void Phone_is_optional_but_must_be_valid_when_given(string? phone)
    {
        _register.Validate(new RegisterRequest("Priya Sharma", "priya@example.com", "Str0ngPass", phone)).IsValid.ShouldBeTrue();
    }

    [Fact]
    public void Invalid_phone_and_email_are_rejected()
    {
        var result = _register.Validate(new RegisterRequest("P", "not-an-email", "Str0ngPass", "12-34"));

        result.Errors.Select(e => e.PropertyName).ShouldBe(["FullName", "Email", "Phone"], ignoreOrder: true);
    }

    [Fact]
    public void New_password_must_differ_from_current()
    {
        var result = new ChangePasswordRequestValidator().Validate(new ChangePasswordRequest("Str0ngPass", "Str0ngPass"));

        result.Errors.ShouldContain(e => e.PropertyName == "NewPassword" && e.ErrorMessage.Contains("different"));
    }
}
