namespace Unit.Test.Configurations;

using System.Security.Claims;
using Duende.IdentityServer.Validation;
using FluentAssertions;
using global::Identity.Configurations;
using global::Identity.Data;
using global::Identity.Identity.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

public class UserValidatorTests
{
    private const string Username = "validator-user";
    private const string Password = "Password@123";

    private static ServiceProvider CreateProvider(string databaseName)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<IdentityContext>(options => options.UseInMemoryDatabase(databaseName));
        services
            .AddIdentity<User, Role>(options =>
            {
                options.Password.RequiredLength = 6;
                options.Password.RequireDigit = false;
                options.Password.RequireNonAlphanumeric = false;
                options.Password.RequireUppercase = false;
            })
            .AddEntityFrameworkStores<IdentityContext>()
            .AddDefaultTokenProviders();

        var httpContextAccessor = new HttpContextAccessor();
        services.AddSingleton<IHttpContextAccessor>(httpContextAccessor);

        var provider = services.BuildServiceProvider();
        httpContextAccessor.HttpContext = new DefaultHttpContext { RequestServices = provider };

        return provider;
    }

    private static async Task<(UserValidator Validator, User User)> SeedUserAsync(ServiceProvider provider)
    {
        var userManager = provider.GetRequiredService<UserManager<User>>();
        var signInManager = provider.GetRequiredService<SignInManager<User>>();

        var user = new User
        {
            Id = Guid.NewGuid(),
            UserName = Username,
            FirstName = "Val",
            LastName = "Idator",
            PassPortNumber = "P123456",
            Email = "validator@test.com",
        };

        (await userManager.CreateAsync(user, Password)).Succeeded.Should().BeTrue();

        return (new UserValidator(signInManager, userManager), user);
    }

    [Fact]
    public async Task should_return_success_result_for_valid_credentials()
    {
        await using var provider = CreateProvider(Guid.NewGuid().ToString());
        var (validator, user) = await SeedUserAsync(provider);
        var context = new ResourceOwnerPasswordValidationContext { UserName = Username, Password = Password };

        await validator.ValidateAsync(context);

        context.Result.IsError.Should().BeFalse();
        context.Result.Subject.Should().NotBeNull();
        context.Result.Subject.FindFirst(ClaimTypes.NameIdentifier)?.Value.Should().Be(user.Id.ToString());
        context.Result.Subject.FindFirst(ClaimTypes.Name)?.Value.Should().Be(Username);
    }

    [Fact]
    public async Task should_return_error_result_for_invalid_password()
    {
        await using var provider = CreateProvider(Guid.NewGuid().ToString());
        var (validator, _) = await SeedUserAsync(provider);
        var context = new ResourceOwnerPasswordValidationContext { UserName = Username, Password = "WrongPassword" };

        await validator.ValidateAsync(context);

        context.Result.IsError.Should().BeTrue();
        context.Result.ErrorDescription.Should().Be("Invalid Credentials");
    }

    [Fact]
    public async Task should_lock_out_user_after_max_failed_attempts()
    {
        await using var provider = CreateProvider(Guid.NewGuid().ToString());
        var (validator, _) = await SeedUserAsync(provider);

        for (var i = 0; i < 5; i++)
        {
            await validator.ValidateAsync(
                new ResourceOwnerPasswordValidationContext { UserName = Username, Password = "WrongPassword" }
            );
        }

        var context = new ResourceOwnerPasswordValidationContext { UserName = Username, Password = Password };
        await validator.ValidateAsync(context);

        context.Result.IsError.Should().BeTrue("the account is locked out after 5 failed attempts");
        context.Result.ErrorDescription.Should().Be("Invalid Credentials");
    }
}
