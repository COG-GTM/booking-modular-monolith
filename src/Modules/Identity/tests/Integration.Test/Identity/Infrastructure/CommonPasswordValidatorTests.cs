using FluentAssertions;
using global::Identity.Configurations;
using global::Identity.Identity.Models;
using Xunit;

namespace Integration.Test.Identity.Infrastructure;

public class CommonPasswordValidatorTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Flight#Deck2026")]
    [InlineData("Nebula!River472")]
    public async Task should_accept_empty_or_strong_passwords(string? password)
    {
        var result = await new CommonPasswordValidator().ValidateAsync(null!, CreateUser(), password);

        result.Succeeded.Should().BeTrue();
    }

    [Theory]
    [InlineData("password")]
    [InlineData("P@ssw0rd2026!")]
    [InlineData("Welcome@1234")]
    public async Task should_reject_common_and_leet_passwords(string password)
    {
        var result = await new CommonPasswordValidator().ValidateAsync(null!, CreateUser(), password);

        result.Succeeded.Should().BeFalse();
        result.Errors.Should().ContainSingle().Which.Code.Should().Be("PasswordTooCommon");
    }

    [Theory]
    [InlineData("MALLORY#River26", "mallory", "other@test.com")]
    [InlineData("BOB.SMITH#2026x", "unrelated", "bob.smith@test.com")]
    public async Task should_reject_user_identifiers_ignoring_case(string password, string username, string email)
    {
        var user = CreateUser(username, email);

        var result = await new CommonPasswordValidator().ValidateAsync(null!, user, password);

        result.Errors.Should().ContainSingle().Which.Code.Should().Be("PasswordContainsUserIdentifier");
    }

    [Theory]
    [InlineData("ab", "xy@test.com", "AB!xyRiver2026")]
    [InlineData(null, null, "Flight#Deck2026")]
    [InlineData("", "@test.com", "Flight#Deck2026")]
    [InlineData("unrelated", "Flight", "Flight#Deck2026")]
    public async Task should_ignore_short_or_missing_identifiers(string? username, string? email, string password)
    {
        var user = CreateUser(username, email);

        var result = await new CommonPasswordValidator().ValidateAsync(null!, user, password);

        result.Succeeded.Should().BeTrue();
    }

    [Fact]
    public async Task should_report_both_common_password_and_identifier_errors()
    {
        var result = await new CommonPasswordValidator().ValidateAsync(null!, CreateUser("password"), "Password2026!");

        result.Succeeded.Should().BeFalse();
        result
            .Errors.Select(error => error.Code)
            .Should()
            .BeEquivalentTo("PasswordTooCommon", "PasswordContainsUserIdentifier");
    }

    private static User CreateUser(string? username = null, string? email = null) =>
        new()
        {
            FirstName = "First",
            LastName = "Last",
            PassPortNumber = "12345678",
            UserName = username,
            Email = email,
        };
}
