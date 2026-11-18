using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Duende.IdentityServer.Validation;
using FluentAssertions;
using Identity.Configurations;
using Identity.Identity.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Integration.Test.Identity.Infrastructure;

public class UserValidatorTests
{
    private const string Password = "validator-test-password";

    [Fact]
    public async Task should_reject_an_unknown_username_without_throwing()
    {
        await using var harness = await IdentitySeederHarness.CreateAsync(
            new IdentitySeedOptions(),
            Environments.Production
        );
        await using var services = CreateSignInServices(harness);
        var validator = new UserValidator(services.GetRequiredService<SignInManager<User>>(), harness.UserManager);
        var context = new ResourceOwnerPasswordValidationContext { UserName = "unknown-user", Password = Password };

        Func<Task> validate = () => validator.ValidateAsync(context);

        await validate.Should().NotThrowAsync();
        context.Result.IsError.Should().BeTrue();
        context.Result.Error.Should().Be("invalid_grant");
        context.Result.ErrorDescription.Should().Be("Invalid Credentials");
    }

    [Fact]
    public async Task should_reject_an_existing_user_with_the_wrong_password()
    {
        await using var harness = await IdentitySeederHarness.CreateAsync(
            new IdentitySeedOptions(),
            Environments.Production
        );
        var user = await CreateUserAsync(harness);
        await using var services = CreateSignInServices(harness);
        var validator = new UserValidator(services.GetRequiredService<SignInManager<User>>(), harness.UserManager);
        var context = new ResourceOwnerPasswordValidationContext
        {
            UserName = user.UserName,
            Password = "wrong-password",
        };

        await validator.ValidateAsync(context);

        context.Result.IsError.Should().BeTrue();
        context.Result.Error.Should().Be("unauthorized_client");
        context.Result.ErrorDescription.Should().Be("Invalid Credentials");
    }

    [Fact]
    public async Task should_accept_an_existing_user_with_the_correct_password_and_use_the_user_id_as_subject()
    {
        await using var harness = await IdentitySeederHarness.CreateAsync(
            new IdentitySeedOptions(),
            Environments.Production
        );
        var user = await CreateUserAsync(harness);
        await using var services = CreateSignInServices(harness);
        var validator = new UserValidator(services.GetRequiredService<SignInManager<User>>(), harness.UserManager);
        var context = new ResourceOwnerPasswordValidationContext { UserName = user.UserName, Password = Password };

        await validator.ValidateAsync(context);

        context.Result.IsError.Should().BeFalse();
        context.Result.Subject.FindFirst("sub")!.Value.Should().Be(user.Id.ToString());
        context.Result.Subject.FindFirst("amr")!.Value.Should().Be("custom");
        context.Result.Subject.FindFirst(ClaimTypes.NameIdentifier)!.Value.Should().Be(user.Id.ToString());
        context.Result.Subject.FindFirst(ClaimTypes.Name)!.Value.Should().Be(user.UserName);
    }

    private static async Task<User> CreateUserAsync(IdentitySeederHarness harness)
    {
        var user = new User
        {
            Id = Guid.NewGuid(),
            UserName = "validator-user",
            FirstName = "Validator",
            LastName = "User",
            PassPortNumber = "validator-passport",
        };
        var result = await harness.UserManager.CreateAsync(user, Password);
        result.Succeeded.Should().BeTrue();
        return user;
    }

    private static ServiceProvider CreateSignInServices(IdentitySeederHarness harness)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHttpContextAccessor();
        services.AddIdentityCore<User>().AddSignInManager();
        services.AddSingleton(harness.UserManager);
        services.AddAuthentication(IdentityConstants.ApplicationScheme).AddIdentityCookies();
        var provider = services.BuildServiceProvider();
        provider.GetRequiredService<IHttpContextAccessor>().HttpContext = new DefaultHttpContext
        {
            RequestServices = provider,
        };
        return provider;
    }
}
