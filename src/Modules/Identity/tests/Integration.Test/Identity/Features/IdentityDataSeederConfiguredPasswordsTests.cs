using System.Linq;
using System.Threading.Tasks;
using Api;
using BuildingBlocks.Constants;
using BuildingBlocks.Core;
using BuildingBlocks.EFCore;
using BuildingBlocks.TestBase;
using FluentAssertions;
using Identity.Configurations;
using Identity.Data;
using Identity.Data.Seed;
using Identity.Identity.Constants;
using Identity.Identity.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Integration.Test.Identity.Features;

public class IdentityDataSeederConfiguredPasswordsTests : IdentityIntegrationTestBase
{
    private const string AdminPassword = "ConfiguredAdmin@123456";
    private const string UserPassword = "ConfiguredUser@123456";

    public IdentityDataSeederConfiguredPasswordsTests(
        TestWriteFixture<Program, IdentityContext> integrationTestFactory) : base(integrationTestFactory)
    {
    }

    [Fact]
    public async Task should_seed_users_with_configured_passwords_when_options_are_set()
    {
        // Arrange
        await Fixture.ExecuteDbContextAsync(db =>
        {
            db.Users.RemoveRange(db.Users);
            return db.SaveChangesAsync();
        });

        using var scope = Fixture.ServiceProvider.CreateScope();
        var seeder = CreateSeeder(scope, new IdentitySeedOptions
        {
            AdminPassword = AdminPassword,
            UserPassword = UserPassword
        });

        // Act
        await seeder.SeedAllAsync();

        // Assert
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();

        var admin = await userManager.FindByNameAsync("samh");
        admin.Should().NotBeNull();
        (await userManager.CheckPasswordAsync(admin, AdminPassword)).Should().BeTrue();
        (await userManager.IsInRoleAsync(admin, IdentityConstant.Role.Admin)).Should().BeTrue();

        var user = await userManager.FindByNameAsync(InitialData.Users.Last().UserName);
        user.Should().NotBeNull();
        (await userManager.CheckPasswordAsync(user, UserPassword)).Should().BeTrue();
        (await userManager.IsInRoleAsync(user, IdentityConstant.Role.User)).Should().BeTrue();
    }

    [Fact]
    public async Task should_seed_only_admin_user_when_only_admin_password_is_configured()
    {
        // Arrange
        await Fixture.ExecuteDbContextAsync(db =>
        {
            db.Users.RemoveRange(db.Users);
            return db.SaveChangesAsync();
        });

        using var scope = Fixture.ServiceProvider.CreateScope();
        var seeder = CreateSeeder(scope, new IdentitySeedOptions { AdminPassword = AdminPassword });

        // Act
        await seeder.SeedAllAsync();

        // Assert
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
        (await userManager.FindByNameAsync("samh")).Should().NotBeNull();
        (await userManager.FindByNameAsync(InitialData.Users.Last().UserName)).Should().BeNull();
    }

    [Fact]
    public async Task should_not_seed_users_when_users_already_exist()
    {
        // Arrange
        using var scope = Fixture.ServiceProvider.CreateScope();
        var seeder = CreateSeeder(scope, new IdentitySeedOptions
        {
            AdminPassword = AdminPassword,
            UserPassword = UserPassword
        });

        var existingUserCount = await Fixture.ExecuteDbContextAsync(db => db.Users.CountAsync());
        existingUserCount.Should().BeGreaterThan(0);

        // Act
        await seeder.SeedAllAsync();

        // Assert
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
        var admin = await userManager.FindByNameAsync("samh");
        (await userManager.CheckPasswordAsync(admin, AdminPassword)).Should().BeFalse();

        var finalUserCount = await Fixture.ExecuteDbContextAsync(db => db.Users.CountAsync());
        finalUserCount.Should().Be(existingUserCount);
    }

    private static IdentityDataSeeder CreateSeeder(IServiceScope scope, IdentitySeedOptions seedOptions)
    {
        return new IdentityDataSeeder(
            scope.ServiceProvider.GetRequiredService<UserManager<User>>(),
            scope.ServiceProvider.GetRequiredService<RoleManager<Role>>(),
            scope.ServiceProvider.GetRequiredService<IEventDispatcher>(),
            scope.ServiceProvider.GetRequiredService<IdentityContext>(),
            seedOptions,
            scope.ServiceProvider.GetRequiredService<ILogger<IdentityDataSeeder>>());
    }
}
