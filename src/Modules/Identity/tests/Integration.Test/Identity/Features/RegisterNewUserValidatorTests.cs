using FluentAssertions;
using global::Identity.Identity.Features.RegisteringNewUser.V1;
using Integration.Test.Fakes;
using Xunit;

namespace Integration.Test.Identity.Features;

public class RegisterNewUserValidatorTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("ABCDEFGHIJK")]
    [InlineData("  ABCDEFGHIJK  ")]
    public void should_reject_missing_or_overlength_passport_number(string passportNumber)
    {
        var command = new FakeRegisterNewUserCommand().Generate() with { PassportNumber = passportNumber };

        var result = new RegisterNewUserValidator().Validate(command);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(error => error.PropertyName == nameof(RegisterNewUser.PassportNumber));
    }

    [Theory]
    [InlineData("ABCDEFGHIJ")]
    [InlineData("  ABCDEFGHIJ  ")]
    public void should_accept_ten_character_passport_number_after_trimming(string passportNumber)
    {
        var command = new FakeRegisterNewUserCommand().Generate() with { PassportNumber = passportNumber };

        var result = new RegisterNewUserValidator().Validate(command);

        result.IsValid.Should().BeTrue();
    }
}
