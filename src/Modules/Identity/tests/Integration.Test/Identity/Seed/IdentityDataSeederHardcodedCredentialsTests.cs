using System.Threading.Tasks;
using Api;
using BuildingBlocks.TestBase;
using FluentAssertions;
using Identity.Data;
using Identity.Data.Seed;
using Identity.Identity.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Integration.Test.Identity.Seed;

public class IdentityDataSeederHardcodedCredentialsTests : IdentityIntegrationTestBase
{
    public IdentityDataSeederHardcodedCredentialsTests(
        TestWriteFixture<Program, IdentityContext> integrationTestFactory) : base(integrationTestFactory)
    {
    }

    [Fact]
    public async Task should_not_seed_users_when_seed_passwords_are_not_configured()
    {
        using var scope = Fixture.ServiceProvider.CreateScope();
        await ClearUsersAsync(scope.ServiceProvider);

        var seeder = ActivatorUtilities.CreateInstance<IdentityDataSeeder>(
            scope.ServiceProvider, Options.Create(new IdentitySeedOptions()));

        await seeder.SeedAllAsync();

        var context = scope.ServiceProvider.GetRequiredService<IdentityContext>();
        (await context.Users.AnyAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task should_seed_users_with_configured_passwords_only()
    {
        using var scope = Fixture.ServiceProvider.CreateScope();
        await ClearUsersAsync(scope.ServiceProvider);

        var options = new IdentitySeedOptions { AdminPassword = "Configured-Admin-Pw1", UserPassword = "Configured-User-Pw1" };
        var seeder = ActivatorUtilities.CreateInstance<IdentityDataSeeder>(scope.ServiceProvider, Options.Create(options));

        await seeder.SeedAllAsync();

        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
        var admin = await userManager.FindByNameAsync("samh");
        admin.Should().NotBeNull();
        (await userManager.CheckPasswordAsync(admin!, options.AdminPassword)).Should().BeTrue();
        (await userManager.CheckPasswordAsync(admin!, "Admin@123456")).Should().BeFalse();

        var user = await userManager.FindByNameAsync("samh2");
        user.Should().NotBeNull();
        (await userManager.CheckPasswordAsync(user!, options.UserPassword)).Should().BeTrue();
        (await userManager.CheckPasswordAsync(user!, "User@123456")).Should().BeFalse();
    }

    private static async Task ClearUsersAsync(System.IServiceProvider serviceProvider)
    {
        var context = serviceProvider.GetRequiredService<IdentityContext>();
        context.UserRoles.RemoveRange(context.UserRoles);
        context.UserClaims.RemoveRange(context.UserClaims);
        context.Users.RemoveRange(context.Users);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
    }
}
