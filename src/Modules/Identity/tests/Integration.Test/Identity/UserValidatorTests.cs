using Duende.IdentityServer.Validation;
using FluentAssertions;
using global::Identity.Configurations;
using global::Identity.Identity.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;

namespace Integration.Test.Identity;

public class UserValidatorTests
{
    [Fact]
    public async Task should_reject_password_grant_when_user_does_not_exist()
    {
        var userStore = Substitute.For<IUserStore<User>>();
        userStore.FindByNameAsync("UNSEEDED-ADMIN", Arg.Any<CancellationToken>()).Returns(Task.FromResult<User?>(null));
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddIdentity<User, Role>();
        services.AddSingleton(userStore);
        services.AddSingleton(Substitute.For<IRoleStore<Role>>());
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var validator = ActivatorUtilities.CreateInstance<UserValidator>(scope.ServiceProvider);
        var context = new ResourceOwnerPasswordValidationContext
        {
            UserName = "unseeded-admin",
            Password = "Not-A-Seed-Password1",
        };

        await validator.ValidateAsync(context);

        context.Result.IsError.Should().BeTrue();
        context.Result.Error.Should().Be("unauthorized_client");
        context.Result.ErrorDescription.Should().Be("Invalid Credentials");
    }
}
