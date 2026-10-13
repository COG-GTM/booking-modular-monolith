using System.Security.Claims;
using System.Threading.Tasks;
using Duende.IdentityServer.Validation;
using FluentAssertions;
using Identity.Identity.Models;
using MassTransit;
using Xunit;

namespace Integration.Test.Identity.Infrastructure;

public class UserValidatorTests
{
    private const string Password = "correct-horse-battery";

    private static async Task<User> CreateUserAsync(IdentityCoreHarness harness, string userName)
    {
        var user = new User
        {
            Id = NewId.NextGuid(),
            UserName = userName,
            Email = $"{userName}@example.com",
            FirstName = "Known",
            LastName = "User",
            PassPortNumber = "12345678",
        };
        (await harness.UserManager.CreateAsync(user, Password)).Succeeded.Should().BeTrue();
        return user;
    }

    [Fact]
    public async Task should_return_invalid_grant_instead_of_throwing_when_user_does_not_exist()
    {
        await using var harness = await IdentityCoreHarness.CreateAsync();
        var context = new ResourceOwnerPasswordValidationContext { UserName = "samh", Password = "Admin@123456" };

        var act = () => harness.UserValidator.ValidateAsync(context);

        await act.Should().NotThrowAsync();
        context.Result.IsError.Should().BeTrue();
        context.Result.Error.Should().Be("invalid_grant");
        context.Result.ErrorDescription.Should().Be("Invalid Credentials");
        context.Result.Subject.Should().BeNull();
    }

    [Fact]
    public async Task should_return_unauthorized_client_when_password_is_wrong_for_existing_user()
    {
        await using var harness = await IdentityCoreHarness.CreateAsync();
        await CreateUserAsync(harness, "known");
        var context = new ResourceOwnerPasswordValidationContext { UserName = "known", Password = "wrong-password" };

        await harness.UserValidator.ValidateAsync(context);

        context.Result.IsError.Should().BeTrue();
        context.Result.Error.Should().Be("unauthorized_client");
        context.Result.ErrorDescription.Should().Be("Invalid Credentials");
    }

    [Fact]
    public async Task should_succeed_with_subject_and_name_claims_for_valid_credentials()
    {
        await using var harness = await IdentityCoreHarness.CreateAsync();
        var user = await CreateUserAsync(harness, "known");
        var context = new ResourceOwnerPasswordValidationContext { UserName = "known", Password = Password };

        await harness.UserValidator.ValidateAsync(context);

        context.Result.IsError.Should().BeFalse();
        context.Result.Subject.Should().NotBeNull();
        context.Result.Subject!.FindFirst("sub")!.Value.Should().Be(user.Id.ToString());
        context.Result.Subject.FindFirst(ClaimTypes.NameIdentifier)!.Value.Should().Be(user.Id.ToString());
        context.Result.Subject.FindFirst(ClaimTypes.Name)!.Value.Should().Be("known");
    }
}
