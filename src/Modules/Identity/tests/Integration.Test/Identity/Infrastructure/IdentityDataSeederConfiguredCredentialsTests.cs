using System.Linq;
using System.Threading.Tasks;
using BuildingBlocks.Constants;
using BuildingBlocks.Contracts.EventBus.Messages;
using FluentAssertions;
using Identity.Configurations;
using Identity.Data.Seed;
using Identity.Identity.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Integration.Test.Identity.Infrastructure;

public class IdentityDataSeederConfiguredCredentialsTests
{
    private const string AdminPassword = "configured-admin-pass";
    private const string UserPassword = "configured-user-pass";
    private const string LegacyAdminPassword = "Admin@123456";
    private const string LegacyUserPassword = "User@123456";

    private static User Admin => InitialData.Users.First();
    private static User RegularUser => InitialData.Users.Last();

    private static string[] LogsAt(IdentitySeederHarness harness, LogLevel level) =>
        harness.Logger.Entries.Where(e => e.Level == level).Select(e => e.Message).ToArray();

    [Theory]
    [InlineData("Production")]
    [InlineData("docker")]
    [InlineData("Development")]
    public async Task should_skip_admin_and_log_a_warning_when_admin_password_is_not_configured(string environment)
    {
        await using var harness = await IdentitySeederHarness.CreateAsync(new IdentitySeedOptions(), environment);

        await harness.Seeder.SeedAllAsync();

        (await harness.Context.Users.CountAsync()).Should().Be(0);
        harness.Events.Should().BeEmpty();
        LogsAt(harness, LogLevel.Warning)
            .Should()
            .ContainSingle()
            .Which.Should()
            .Contain("IdentitySeedOptions:AdminPassword is not configured");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task should_treat_a_blank_admin_password_as_not_configured(string blank)
    {
        await using var harness = await IdentitySeederHarness.CreateAsync(
            new IdentitySeedOptions { AdminPassword = blank },
            Environments.Production
        );

        await harness.Seeder.SeedAllAsync();

        (await harness.Context.Users.CountAsync()).Should().Be(0);
        LogsAt(harness, LogLevel.Warning).Should().ContainSingle();
    }

    [Fact]
    public async Task should_still_seed_roles_when_no_passwords_are_configured()
    {
        await using var harness = await IdentitySeederHarness.CreateAsync(
            new IdentitySeedOptions(),
            Environments.Production
        );

        await harness.Seeder.SeedAllAsync();

        (await harness.Context.Roles.Select(r => r.Name).ToListAsync())
            .Should()
            .BeEquivalentTo(IdentityConstant.Role.Admin, IdentityConstant.Role.User);
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("docker")]
    public async Task should_seed_only_the_admin_outside_development_even_when_a_user_password_is_configured(
        string environment
    )
    {
        await using var harness = await IdentitySeederHarness.CreateAsync(
            new IdentitySeedOptions { AdminPassword = AdminPassword, UserPassword = UserPassword },
            environment
        );

        await harness.Seeder.SeedAllAsync();

        var users = await harness.Context.Users.ToListAsync();
        users.Select(u => u.UserName).Should().Equal(Admin.UserName);
        var admin = users.Single();
        (await harness.UserManager.GetRolesAsync(admin)).Should().Equal(IdentityConstant.Role.Admin);
        (await harness.UserManager.CheckPasswordAsync(admin, AdminPassword)).Should().BeTrue();
        (await harness.UserManager.CheckPasswordAsync(admin, LegacyAdminPassword)).Should().BeFalse();
        (await harness.UserManager.FindByNameAsync(RegularUser.UserName!)).Should().BeNull();

        harness
            .Events.Should()
            .ContainSingle()
            .Which.Should()
            .BeOfType<UserCreated>()
            .Which.Should()
            .Match<UserCreated>(e =>
                e.Id == Admin.Id
                && e.Name == $"{Admin.FirstName} {Admin.LastName}"
                && e.PassportNumber == Admin.PassPortNumber
            );
        harness.Logger.Entries.Where(e => e.Level >= LogLevel.Warning).Should().BeEmpty();
    }

    [Fact]
    public async Task should_seed_admin_and_regular_user_with_configured_passwords_in_development()
    {
        await using var harness = await IdentitySeederHarness.CreateAsync(
            new IdentitySeedOptions { AdminPassword = AdminPassword, UserPassword = UserPassword },
            Environments.Development
        );

        await harness.Seeder.SeedAllAsync();

        var admin = await harness.UserManager.FindByNameAsync(Admin.UserName!);
        var user = await harness.UserManager.FindByNameAsync(RegularUser.UserName!);
        admin.Should().NotBeNull();
        user.Should().NotBeNull();
        (await harness.UserManager.GetRolesAsync(admin!)).Should().Equal(IdentityConstant.Role.Admin);
        (await harness.UserManager.GetRolesAsync(user!)).Should().Equal(IdentityConstant.Role.User);
        (await harness.UserManager.CheckPasswordAsync(admin!, AdminPassword)).Should().BeTrue();
        (await harness.UserManager.CheckPasswordAsync(user!, UserPassword)).Should().BeTrue();
        (await harness.UserManager.CheckPasswordAsync(user!, LegacyUserPassword)).Should().BeFalse();
        (await harness.UserManager.CheckPasswordAsync(user!, AdminPassword)).Should().BeFalse();

        harness.Events.OfType<UserCreated>().Select(e => e.Id).Should().Equal(Admin.Id, RegularUser.Id);
    }

    [Fact]
    public async Task should_seed_the_regular_user_in_development_even_when_admin_password_is_missing()
    {
        await using var harness = await IdentitySeederHarness.CreateAsync(
            new IdentitySeedOptions { UserPassword = UserPassword },
            Environments.Development
        );

        await harness.Seeder.SeedAllAsync();

        (await harness.Context.Users.Select(u => u.UserName).ToListAsync()).Should().Equal(RegularUser.UserName);
        harness.Events.OfType<UserCreated>().Select(e => e.Id).Should().Equal(RegularUser.Id);
        LogsAt(harness, LogLevel.Warning).Should().ContainSingle();
    }

    [Fact]
    public async Task should_seed_admin_on_a_later_startup_when_admin_password_is_configured()
    {
        var seedOptions = new IdentitySeedOptions { UserPassword = UserPassword };
        await using var harness = await IdentitySeederHarness.CreateAsync(seedOptions, Environments.Development);

        await harness.Seeder.SeedAllAsync();

        (await harness.Context.Users.Select(u => u.UserName).ToListAsync()).Should().Equal(RegularUser.UserName);

        seedOptions.AdminPassword = AdminPassword;
        await harness.Seeder.SeedAllAsync();

        (await harness.Context.Users.OrderBy(u => u.UserName).Select(u => u.UserName).ToListAsync())
            .Should()
            .Equal(Admin.UserName, RegularUser.UserName);
        var admin = await harness.UserManager.FindByNameAsync(Admin.UserName!);
        admin.Should().NotBeNull();
        (await harness.UserManager.CheckPasswordAsync(admin!, AdminPassword)).Should().BeTrue();
        harness.Events.OfType<UserCreated>().Select(e => e.Id).Should().Equal(RegularUser.Id, Admin.Id);
        LogsAt(harness, LogLevel.Warning).Should().ContainSingle();
    }

    [Fact]
    public async Task should_skip_the_regular_user_in_development_when_user_password_is_not_configured()
    {
        await using var harness = await IdentitySeederHarness.CreateAsync(
            new IdentitySeedOptions { AdminPassword = AdminPassword },
            Environments.Development
        );

        await harness.Seeder.SeedAllAsync();

        (await harness.Context.Users.Select(u => u.UserName).ToListAsync()).Should().Equal(Admin.UserName);
    }

    [Fact]
    public async Task should_log_an_error_and_not_seed_the_admin_when_the_configured_password_violates_policy()
    {
        await using var harness = await IdentitySeederHarness.CreateAsync(
            new IdentitySeedOptions { AdminPassword = "abc" },
            Environments.Production
        );

        await harness.Seeder.SeedAllAsync();

        (await harness.Context.Users.CountAsync()).Should().Be(0);
        harness.Events.Should().BeEmpty();
        LogsAt(harness, LogLevel.Error)
            .Should()
            .ContainSingle()
            .Which.Should()
            .Contain($"Failed to create identity user {Admin.UserName}");
    }

    [Fact]
    public async Task should_not_seed_when_migrations_are_pending()
    {
        await using var harness = await IdentitySeederHarness.CreateAsync(
            new IdentitySeedOptions { AdminPassword = AdminPassword, UserPassword = UserPassword },
            Environments.Development,
            pendingMigrations: true
        );

        await harness.Seeder.SeedAllAsync();

        (await harness.Context.Users.CountAsync()).Should().Be(0);
        (await harness.Context.Roles.CountAsync()).Should().Be(0);
        harness.Events.Should().BeEmpty();
    }
}
