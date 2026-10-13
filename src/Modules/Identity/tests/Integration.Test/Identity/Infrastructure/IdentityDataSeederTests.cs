using System.Linq;
using System.Threading.Tasks;
using BuildingBlocks.Constants;
using BuildingBlocks.Contracts.EventBus.Messages;
using FluentAssertions;
using Identity.Configurations;
using Identity.Identity.Models;
using MassTransit;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Integration.Test.Identity.Infrastructure;

public class IdentityDataSeederTests
{
    private const string AdminPassword = "admin-pass-from-config";

    private static IdentitySeedOptions EnabledOptions(string? password = AdminPassword) =>
        new()
        {
            Enabled = true,
            AdminUserName = "ops-admin",
            AdminEmail = "ops-admin@example.com",
            AdminFirstName = "Ops",
            AdminLastName = "Admin",
            AdminPassPortNumber = "A1234567",
            AdminPassword = password,
        };

    [Fact]
    public async Task should_create_no_users_when_seeding_is_disabled()
    {
        await using var harness = await IdentityCoreHarness.CreateAsync(
            new IdentitySeedOptions
            {
                Enabled = false,
                AdminUserName = "x",
                AdminEmail = "x@x",
                AdminPassword = AdminPassword,
            }
        );

        await harness.Seeder.SeedAllAsync();

        (await harness.Context.Users.CountAsync()).Should().Be(0);
        harness.Events.Should().BeEmpty();
        harness
            .SeederLogger.Entries.Should()
            .ContainSingle(e => e.Level == LogLevel.Information)
            .Which.Message.Should()
            .Contain("seeding is disabled");
        harness.SeederLogger.Entries.Should().NotContain(e => e.Level >= LogLevel.Warning);
    }

    [Fact]
    public async Task should_default_to_disabled_when_options_are_not_configured()
    {
        await using var harness = await IdentityCoreHarness.CreateAsync(new IdentitySeedOptions());

        await harness.Seeder.SeedAllAsync();

        (await harness.Context.Users.CountAsync()).Should().Be(0);
        harness.Events.Should().BeEmpty();
    }

    [Fact]
    public async Task should_still_seed_roles_when_user_seeding_is_disabled()
    {
        await using var harness = await IdentityCoreHarness.CreateAsync(new IdentitySeedOptions { Enabled = false });

        await harness.Seeder.SeedAllAsync();

        (await harness.Context.Roles.Select(r => r.Name).ToListAsync())
            .Should()
            .BeEquivalentTo(IdentityConstant.Role.Admin, IdentityConstant.Role.User);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task should_skip_and_warn_when_enabled_but_admin_password_is_missing(string? password)
    {
        await using var harness = await IdentityCoreHarness.CreateAsync(EnabledOptions(password));

        await harness.Seeder.SeedAllAsync();

        (await harness.Context.Users.CountAsync()).Should().Be(0);
        harness.Events.Should().BeEmpty();
        harness
            .SeederLogger.Entries.Should()
            .ContainSingle(e => e.Level == LogLevel.Warning)
            .Which.Message.Should()
            .Contain("IdentitySeedOptions is incomplete")
            .And.Contain("AdminPassword")
            .And.Contain("skipping initial admin creation");
    }

    [Theory]
    [InlineData(nameof(IdentitySeedOptions.AdminUserName))]
    [InlineData(nameof(IdentitySeedOptions.AdminEmail))]
    [InlineData(nameof(IdentitySeedOptions.AdminFirstName))]
    [InlineData(nameof(IdentitySeedOptions.AdminLastName))]
    [InlineData(nameof(IdentitySeedOptions.AdminPassPortNumber))]
    public async Task should_skip_and_warn_when_enabled_but_a_required_field_is_blank(string blankField)
    {
        var options = EnabledOptions();
        typeof(IdentitySeedOptions).GetProperty(blankField)!.SetValue(options, " ");
        await using var harness = await IdentityCoreHarness.CreateAsync(options);

        await harness.Seeder.SeedAllAsync();

        (await harness.Context.Users.CountAsync()).Should().Be(0);
        harness.Events.Should().BeEmpty();
        harness
            .SeederLogger.Entries.Should()
            .ContainSingle(e => e.Level == LogLevel.Warning)
            .Which.Message.Should()
            .Contain(blankField);
    }

    [Fact]
    public async Task should_create_single_admin_from_configuration_when_enabled_and_fully_configured()
    {
        await using var harness = await IdentityCoreHarness.CreateAsync(EnabledOptions());

        await harness.Seeder.SeedAllAsync();

        (await harness.Context.Users.CountAsync()).Should().Be(1);

        var admin = await harness.UserManager.FindByNameAsync("ops-admin");
        admin.Should().NotBeNull();
        admin!.Email.Should().Be("ops-admin@example.com");
        admin.FirstName.Should().Be("Ops");
        admin.LastName.Should().Be("Admin");
        admin.PassPortNumber.Should().Be("A1234567");
        admin.SecurityStamp.Should().NotBeNullOrEmpty();
        (await harness.UserManager.CheckPasswordAsync(admin, AdminPassword)).Should().BeTrue();
        (await harness.UserManager.GetRolesAsync(admin)).Should().BeEquivalentTo(IdentityConstant.Role.Admin);

        var userCreated = harness.Events.Should().ContainSingle().Which.Should().BeOfType<UserCreated>().Subject;
        userCreated.Id.Should().Be(admin.Id);
        userCreated.Name.Should().Be("Ops Admin");
        userCreated.PassportNumber.Should().Be("A1234567");

        harness.SeederLogger.Entries.Should().NotContain(e => e.Level >= LogLevel.Warning);
        harness
            .SeederLogger.Entries.Should()
            .ContainSingle(e => e.Level == LogLevel.Information)
            .Which.Message.Should()
            .Contain("ops-admin")
            .And.Contain("created from configuration");
    }

    [Fact]
    public async Task should_never_create_the_previously_hardcoded_accounts()
    {
        await using var harness = await IdentityCoreHarness.CreateAsync(EnabledOptions());

        await harness.Seeder.SeedAllAsync();

        (await harness.UserManager.FindByNameAsync("samh")).Should().BeNull();
        (await harness.UserManager.FindByNameAsync("samh2")).Should().BeNull();
        (await harness.UserManager.FindByNameAsync("meysamh2")).Should().BeNull();
    }

    [Fact]
    public async Task should_not_seed_admin_when_users_already_exist()
    {
        await using var harness = await IdentityCoreHarness.CreateAsync(EnabledOptions());
        var existing = new User
        {
            Id = NewId.NextGuid(),
            UserName = "already-there",
            Email = "already@example.com",
            FirstName = "Already",
            LastName = "There",
            PassPortNumber = "00000001",
        };
        (await harness.UserManager.CreateAsync(existing, "existing-pass")).Succeeded.Should().BeTrue();

        await harness.Seeder.SeedAllAsync();

        (await harness.Context.Users.CountAsync()).Should().Be(1);
        (await harness.UserManager.FindByNameAsync("ops-admin")).Should().BeNull();
        harness.Events.Should().BeEmpty();
    }

    [Fact]
    public async Task should_be_idempotent_when_run_twice()
    {
        await using var harness = await IdentityCoreHarness.CreateAsync(EnabledOptions());

        await harness.Seeder.SeedAllAsync();
        await harness.Seeder.SeedAllAsync();

        (await harness.Context.Users.CountAsync()).Should().Be(1);
        (await harness.Context.Roles.CountAsync()).Should().Be(2);
        harness.Events.OfType<UserCreated>().Should().ContainSingle();
    }

    [Fact]
    public async Task should_log_error_and_not_assign_role_or_dispatch_event_when_password_violates_policy()
    {
        await using var harness = await IdentityCoreHarness.CreateAsync(EnabledOptions("short"));

        await harness.Seeder.SeedAllAsync();

        (await harness.Context.Users.CountAsync()).Should().Be(0);
        (await harness.Context.UserRoles.CountAsync()).Should().Be(0);
        harness.Events.Should().BeEmpty();
        harness
            .SeederLogger.Entries.Should()
            .ContainSingle(e => e.Level == LogLevel.Error)
            .Which.Message.Should()
            .Contain("Failed to create initial admin user ops-admin")
            .And.Contain("Passwords must be at least 6 characters");
    }

    [Fact]
    public async Task should_create_missing_role_even_when_another_role_already_exists()
    {
        await using var harness = await IdentityCoreHarness.CreateAsync(new IdentitySeedOptions());
        var roleManager = harness.Services.GetRequiredService<RoleManager<Role>>();
        (await roleManager.CreateAsync(new Role { Name = IdentityConstant.Role.Admin })).Succeeded.Should().BeTrue();

        await harness.Seeder.SeedAllAsync();

        (await harness.Context.Roles.Select(r => r.Name).ToListAsync())
            .Should()
            .BeEquivalentTo(IdentityConstant.Role.Admin, IdentityConstant.Role.User);
    }

    [Fact]
    public async Task should_remove_user_log_error_and_skip_event_when_admin_role_assignment_fails()
    {
        var updateBlocker = new RejectUpdatesUserValidator();
        await using var harness = await IdentityCoreHarness.CreateAsync(
            EnabledOptions(),
            configure: services => services.AddSingleton<IUserValidator<User>>(updateBlocker)
        );

        await harness.Seeder.SeedAllAsync();

        (await harness.Context.Users.CountAsync()).Should().Be(0);
        (await harness.Context.UserRoles.CountAsync()).Should().Be(0);
        harness.Events.Should().BeEmpty();
        harness
            .SeederLogger.Entries.Should()
            .ContainSingle(e => e.Level == LogLevel.Error)
            .Which.Message.Should()
            .Contain("Failed to assign the admin role to initial admin user ops-admin")
            .And.Contain("the user was removed so seeding can be retried")
            .And.Contain(RejectUpdatesUserValidator.Description);
    }

    [Fact]
    public async Task should_seed_admin_on_retry_after_a_failed_role_assignment()
    {
        var updateBlocker = new RejectUpdatesUserValidator();
        await using var harness = await IdentityCoreHarness.CreateAsync(
            EnabledOptions(),
            configure: services => services.AddSingleton<IUserValidator<User>>(updateBlocker)
        );

        await harness.Seeder.SeedAllAsync();
        updateBlocker.Enabled = false;
        await harness.Seeder.SeedAllAsync();

        var admin = await harness.UserManager.FindByNameAsync("ops-admin");
        admin.Should().NotBeNull();
        (await harness.UserManager.GetRolesAsync(admin!)).Should().BeEquivalentTo(IdentityConstant.Role.Admin);
        harness.Events.OfType<UserCreated>().Should().ContainSingle().Which.Id.Should().Be(admin!.Id);
    }

    [Fact]
    public async Task should_not_seed_anything_when_migrations_are_pending()
    {
        await using var harness = await IdentityCoreHarness.CreateAsync(EnabledOptions(), pendingMigrations: true);

        await harness.Seeder.SeedAllAsync();

        (await harness.Context.Users.CountAsync()).Should().Be(0);
        (await harness.Context.Roles.CountAsync()).Should().Be(0);
        harness.Events.Should().BeEmpty();
        harness.SeederLogger.Entries.Should().BeEmpty();
    }

    /// <summary>
    /// Passes on <c>CreateAsync</c> (user not yet persisted) and fails on any later
    /// <c>UpdateAsync</c>, which is what <c>AddToRoleAsync</c> goes through.
    /// </summary>
    private sealed class RejectUpdatesUserValidator : IUserValidator<User>
    {
        public const string Description = "updates rejected by test validator";

        public bool Enabled { get; set; } = true;

        public async Task<IdentityResult> ValidateAsync(UserManager<User> manager, User user)
        {
            if (!Enabled || await manager.FindByIdAsync(user.Id.ToString()) == null)
            {
                return IdentityResult.Success;
            }

            return IdentityResult.Failed(new IdentityError { Code = "TestReject", Description = Description });
        }
    }
}
