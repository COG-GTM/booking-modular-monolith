using FluentAssertions;
using global::Identity.Configurations;
using global::Identity.Data.Seed;
using global::Identity.Extensions.Infrastructure;
using global::Identity.Identity.Models;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Integration.Test.Identity.Infrastructure;

public class IdentityPasswordPolicyTests
{
    [Fact]
    public void should_register_production_password_requirements()
    {
        using var provider = CreateServices();
        var options = provider.GetRequiredService<IOptions<IdentityOptions>>().Value.Password;

        options.RequiredLength.Should().Be(12);
        options.RequiredUniqueChars.Should().Be(4);
        options.RequireDigit.Should().BeTrue();
        options.RequireLowercase.Should().BeTrue();
        options.RequireUppercase.Should().BeTrue();
        options.RequireNonAlphanumeric.Should().BeTrue();
        using var scope = provider.CreateScope();
        scope
            .ServiceProvider.GetRequiredService<UserManager<User>>()
            .PasswordValidators.Should()
            .Contain(validator => validator is CommonPasswordValidator);
    }

    [Theory]
    [InlineData(0, "Admin@123456", false)]
    [InlineData(1, "User@123456", false)]
    [InlineData(0, "abcdef", false)]
    [InlineData(1, "abcdef", false)]
    [InlineData(0, "Admin@Booking2026!", true)]
    [InlineData(1, "User@Booking2026!", true)]
    [InlineData(0, "Flight#Deck2026", true)]
    [InlineData(1, "Flight#Deck2026", true)]
    [InlineData(0, "aaaaaaaaaaaa", false)]
    [InlineData(0, "Ab1!Ab1!Ab1!", true)]
    [InlineData(0, "Ab!Ab!Ab!Ab!", false)]
    [InlineData(0, "abcdefghijk1!", false)]
    [InlineData(0, "ABCDEFGHIJK1!", false)]
    [InlineData(0, "Abcdefghijk1", false)]
    public async Task should_enforce_full_policy_for_seeded_users(int userIndex, string password, bool succeeds)
    {
        using var provider = CreateServices();
        using var scope = provider.CreateScope();
        var manager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
        var user = InitialData.Users[userIndex];
        var results = await Task.WhenAll(
            manager.PasswordValidators.Select(validator => validator.ValidateAsync(manager, user, password))
        );

        results.All(result => result.Succeeded).Should().Be(succeeds);
    }

    private static ServiceProvider CreateServices()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Configuration.AddInMemoryCollection(
            new Dictionary<string, string?> { ["AuthOptions:IssuerUri"] = "https://identity.test" }
        );
        builder.AddCustomIdentityServer();
        builder.Services.RemoveAll<IUserStore<User>>();
        builder.Services.AddScoped(_ => Substitute.For<IUserStore<User>>());
        return builder.Services.BuildServiceProvider();
    }
}
