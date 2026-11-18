using System.Linq;
using System.Threading.Tasks;
using BuildingBlocks.Constants;
using BuildingBlocks.Contracts.EventBus.Messages;
using FluentAssertions;
using Identity.Configurations;
using Identity.Data.Seed;
using Identity.Identity.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Integration.Test.Identity.Infrastructure;

public class IdentityDataSeederTests
{
    private const string AdminPassword = "configured-admin-secret";
    private const string UserPassword = "configured-user-secret";
    private const string LegacyAdminPassword = "Admin@123456";
    private const string LegacyUserPassword = "User@123456";

    private static User Admin => InitialData.Users.First();
    private static User RegularUser => InitialData.Users.Last();

    private static string[] Warnings(IdentityCoreHarness harness) =>
        harness.SeederLogger.Entries.Where(e => e.Level == LogLevel.Warning).Select(e => e.Message).ToArray();

    private static string[] Errors(IdentityCoreHarness harness) =>
        harness.SeederLogger.Entries.Where(e => e.Level == LogLevel.Error).Select(e => e.Message).ToArray();

    [Fact]
    public async Task should_seed_no_users_and_warn_for_each_when_no_passwords_are_configured()
    {
        await using var harness = await IdentityCoreHarness.CreateAsync(new IdentitySeedOptions());

        await harness.Seeder.SeedAllAsync();

        (await harness.Context.Users.CountAsync()).Should().Be(0);
        harness.Events.Should().BeEmpty();
        Warnings(harness)
            .Should()
            .BeEquivalentTo(
                $"IdentitySeedOptions:AdminPassword is not configured; skipping seeding of user {Admin.UserName}",
                $"IdentitySeedOptions:UserPassword is not configured; skipping seeding of user {RegularUser.UserName}"
            );
    }

    [Fact]
    public async Task should_still_seed_roles_when_no_passwords_are_configured()
    {
        await using var harness = await IdentityCoreHarness.CreateAsync(new IdentitySeedOptions());

        await harness.Seeder.SeedAllAsync();

        (await harness.Context.Roles.Select(r => r.Name).ToListAsync())
            .Should()
            .BeEquivalentTo(IdentityConstant.Role.Admin, IdentityConstant.Role.User);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task should_treat_blank_passwords_as_not_configured(string blank)
    {
        await using var harness = await IdentityCoreHarness.CreateAsync(
            new IdentitySeedOptions { AdminPassword = blank, UserPassword = blank }
        );

        await harness.Seeder.SeedAllAsync();

        (await harness.Context.Users.CountAsync()).Should().Be(0);
        Warnings(harness).Should().HaveCount(2);
    }

    [Fact]
    public async Task should_seed_only_admin_with_configured_password_when_user_password_is_missing()
    {
        await using var harness = await IdentityCoreHarness.CreateAsync(
            new IdentitySeedOptions { AdminPassword = AdminPassword }
        );

        await harness.Seeder.SeedAllAsync();

        var users = await harness.Context.Users.ToListAsync();
        users.Select(u => u.UserName).Should().Equal(Admin.UserName);
        var admin = users.Single();
        (await harness.UserManager.IsInRoleAsync(admin, IdentityConstant.Role.Admin)).Should().BeTrue();
        (await harness.UserManager.CheckPasswordAsync(admin, AdminPassword)).Should().BeTrue();
        (await harness.UserManager.CheckPasswordAsync(admin, LegacyAdminPassword)).Should().BeFalse();

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
        Warnings(harness)
            .Should()
            .Equal(
                $"IdentitySeedOptions:UserPassword is not configured; skipping seeding of user {RegularUser.UserName}"
            );
    }

    [Fact]
    public async Task should_seed_only_regular_user_with_configured_password_when_admin_password_is_missing()
    {
        await using var harness = await IdentityCoreHarness.CreateAsync(
            new IdentitySeedOptions { UserPassword = UserPassword }
        );

        await harness.Seeder.SeedAllAsync();

        var users = await harness.Context.Users.ToListAsync();
        users.Select(u => u.UserName).Should().Equal(RegularUser.UserName);
        var user = users.Single();
        (await harness.UserManager.IsInRoleAsync(user, IdentityConstant.Role.User)).Should().BeTrue();
        (await harness.UserManager.IsInRoleAsync(user, IdentityConstant.Role.Admin)).Should().BeFalse();
        (await harness.UserManager.CheckPasswordAsync(user, UserPassword)).Should().BeTrue();
        (await harness.UserManager.CheckPasswordAsync(user, LegacyUserPassword)).Should().BeFalse();

        harness
            .Events.Should()
            .ContainSingle()
            .Which.Should()
            .BeOfType<UserCreated>()
            .Which.Should()
            .Match<UserCreated>(e =>
                e.Id == RegularUser.Id
                && e.Name == $"{RegularUser.FirstName} {RegularUser.LastName}"
                && e.PassportNumber == RegularUser.PassPortNumber
            );
        Warnings(harness)
            .Should()
            .Equal($"IdentitySeedOptions:AdminPassword is not configured; skipping seeding of user {Admin.UserName}");
    }

    [Fact]
    public async Task should_seed_both_users_with_their_own_roles_and_passwords_when_both_are_configured()
    {
        await using var harness = await IdentityCoreHarness.CreateAsync(
            new IdentitySeedOptions { AdminPassword = AdminPassword, UserPassword = UserPassword }
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
        (await harness.UserManager.CheckPasswordAsync(user!, AdminPassword)).Should().BeFalse();

        harness.Events.OfType<UserCreated>().Select(e => e.Id).Should().Equal(Admin.Id, RegularUser.Id);
        Warnings(harness).Should().BeEmpty();
    }

    [Fact]
    public async Task should_skip_seeding_when_migrations_are_pending()
    {
        await using var harness = await IdentityCoreHarness.CreateAsync(
            new IdentitySeedOptions { AdminPassword = AdminPassword, UserPassword = UserPassword },
            pendingMigrations: true
        );

        await harness.Seeder.SeedAllAsync();

        (await harness.Context.Users.CountAsync()).Should().Be(0);
        (await harness.Context.Roles.CountAsync()).Should().Be(0);
        harness.Events.Should().BeEmpty();
    }

    [Fact]
    public async Task should_log_error_and_not_create_user_when_configured_password_violates_policy()
    {
        await using var harness = await IdentityCoreHarness.CreateAsync(
            new IdentitySeedOptions { AdminPassword = "abc" }
        );

        await harness.Seeder.SeedAllAsync();

        (await harness.Context.Users.CountAsync()).Should().Be(0);
        harness.Events.Should().BeEmpty();
        Errors(harness)
            .Should()
            .ContainSingle()
            .Which.Should()
            .Contain($"Failed to seed user {Admin.UserName}: ")
            .And.Contain("6 characters");
    }

    [Fact]
    public async Task should_reject_the_legacy_admin_password_with_unauthorized_client_and_accept_the_configured_password()
    {
        await using var harness = await IdentityCoreHarness.CreateAsync(
            new IdentitySeedOptions { AdminPassword = AdminPassword }
        );
        await harness.Seeder.SeedAllAsync();

        var legacy = new Duende.IdentityServer.Validation.ResourceOwnerPasswordValidationContext
        {
            UserName = Admin.UserName,
            Password = LegacyAdminPassword,
        };
        await harness.UserValidator.ValidateAsync(legacy);

        var configured = new Duende.IdentityServer.Validation.ResourceOwnerPasswordValidationContext
        {
            UserName = Admin.UserName,
            Password = AdminPassword,
        };
        await harness.UserValidator.ValidateAsync(configured);

        legacy.Result.Error.Should().Be("unauthorized_client");
        configured.Result.IsError.Should().BeFalse();
        configured.Result.Subject!.FindFirst("sub")!.Value.Should().Be(Admin.Id.ToString());
    }

    [Fact]
    public async Task should_reject_the_unseeded_regular_user_with_unauthorized_client()
    {
        await using var harness = await IdentityCoreHarness.CreateAsync(
            new IdentitySeedOptions { AdminPassword = AdminPassword }
        );
        await harness.Seeder.SeedAllAsync();

        var context = new Duende.IdentityServer.Validation.ResourceOwnerPasswordValidationContext
        {
            UserName = RegularUser.UserName,
            Password = LegacyUserPassword,
        };
        await harness.UserValidator.ValidateAsync(context);

        context.Result.Error.Should().Be("unauthorized_client");
    }
}
