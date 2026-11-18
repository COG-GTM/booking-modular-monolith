using System.Threading.Tasks;
using Api;
using BuildingBlocks.TestBase;
using FluentAssertions;
using Identity.Data;
using Integration.Test.Fakes;
using Xunit;

namespace Integration.Test.Identity.Features;

using global::Identity.Identity.Exceptions;
using ValidationException = BuildingBlocks.Exception.ValidationException;

public class RegisterNewUserPasswordPolicyTests : IdentityIntegrationTestBase
{
    public RegisterNewUserPasswordPolicyTests(
        TestWriteFixture<Program, IdentityContext> integrationTestFactory) : base(integrationTestFactory)
    {
    }

    [Theory]
    [InlineData("abcdef")]
    [InlineData("Ab1!xyz")]
    [InlineData("Fl1ght#Deck")]
    public async Task should_reject_password_shorter_than_twelve_characters(string password)
    {
        var command = new FakeRegisterNewUserCommand().Generate() with
        {
            Username = "ShortPwdUser", Email = "short-pwd@test.com", Password = password, ConfirmPassword = password
        };

        var act = () => Fixture.SendAsync(command);

        (await act.Should().ThrowAsync<ValidationException>())
            .WithMessage("*at least 12 characters*");
    }

    [Theory]
    [InlineData("abcdefghijkl")]
    [InlineData("abcdefghijk1")]
    [InlineData("ABCDEFGHIJ1!")]
    [InlineData("Abcdefghijk1")]
    public async Task should_reject_password_missing_required_character_classes(string password)
    {
        var command = new FakeRegisterNewUserCommand().Generate() with
        {
            Username = "WeakPwdUser", Email = "weak-pwd@test.com", Password = password, ConfirmPassword = password
        };

        var act = () => Fixture.SendAsync(command);

        await act.Should().ThrowAsync<RegisterIdentityUserException>();
    }

    [Theory]
    [InlineData("Password@123")]
    [InlineData("P@ssw0rd2026!")]
    [InlineData("Welcome@1234")]
    [InlineData("Qwerty@123456")]
    [InlineData("Admin@123456")]
    [InlineData("!Letmein12345")]
    public async Task should_reject_common_password(string password)
    {
        var command = new FakeRegisterNewUserCommand().Generate() with
        {
            Username = "CommonPwdUser", Email = "common-pwd@test.com", Password = password, ConfirmPassword = password
        };

        var act = () => Fixture.SendAsync(command);

        (await act.Should().ThrowAsync<RegisterIdentityUserException>())
            .WithMessage("*commonly used password*");
    }

    [Theory]
    [InlineData("Mallory#Target9", "mallory", "someone@test.com")]
    [InlineData("Bob.Smith#2026x", "bobsmith2026", "bob.smith@test.com")]
    public async Task should_reject_password_containing_username_or_email(string password, string username,
        string email)
    {
        var command = new FakeRegisterNewUserCommand().Generate() with
        {
            Username = username, Email = email, Password = password, ConfirmPassword = password
        };

        var act = () => Fixture.SendAsync(command);

        (await act.Should().ThrowAsync<RegisterIdentityUserException>())
            .WithMessage("*must not contain the username or email*");
    }
}
