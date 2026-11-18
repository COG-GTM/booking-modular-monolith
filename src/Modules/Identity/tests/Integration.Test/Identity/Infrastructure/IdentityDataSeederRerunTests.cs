using System;
using System.Linq;
using System.Threading.Tasks;
using BuildingBlocks.Constants;
using BuildingBlocks.Contracts.EventBus.Messages;
using FluentAssertions;
using Identity.Configurations;
using Identity.Data.Seed;
using Identity.Identity.Models;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Integration.Test.Identity.Infrastructure;

public class IdentityDataSeederRerunTests
{
    private const string AdminPassword = "configured-admin-pass";
    private const string UserPassword = "configured-user-pass";

    private static User Admin => InitialData.Users.First();
    private static User RegularUser => InitialData.Users.Last();

    private static string[] LogsAt(IdentitySeederHarness harness, LogLevel level) =>
        harness.Logger.Entries.Where(e => e.Level == level).Select(e => e.Message).ToArray();

    [Fact]
    public async Task should_seed_the_admin_on_a_later_run_when_the_admin_password_is_configured_after_the_regular_user_was_seeded()
    {
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();

        await using (
            var initialHarness = await IdentitySeederHarness.CreateAsync(
                connection,
                new IdentitySeedOptions { AdminPassword = string.Empty, UserPassword = UserPassword },
                Environments.Development
            )
        )
        {
            await initialHarness.Seeder.SeedAllAsync();

            (await initialHarness.Context.Users.Select(u => u.UserName).ToListAsync()).Should().Equal(RegularUser.UserName);
            initialHarness.Events.OfType<UserCreated>().Select(e => e.Id).Should().Equal(RegularUser.Id);
            LogsAt(initialHarness, LogLevel.Warning).Should().ContainSingle();
        }

        await using (
            var harness = await IdentitySeederHarness.CreateAsync(
                connection,
                new IdentitySeedOptions { AdminPassword = AdminPassword, UserPassword = UserPassword },
                Environments.Development
            )
        )
        {
            await harness.Seeder.SeedAllAsync();

            var admin = await harness.UserManager.FindByNameAsync(Admin.UserName!);
            admin.Should().NotBeNull();
            (await harness.UserManager.GetRolesAsync(admin!)).Should().Equal(IdentityConstant.Role.Admin);
            (await harness.UserManager.CheckPasswordAsync(admin!, AdminPassword)).Should().BeTrue();
            (await harness.Context.Users.OrderBy(u => u.UserName).Select(u => u.UserName).ToListAsync())
                .Should()
                .Equal(Admin.UserName, RegularUser.UserName);
            harness.Events.OfType<UserCreated>().Select(e => e.Id).Should().Equal(Admin.Id);
            LogsAt(harness, LogLevel.Warning).Should().BeEmpty();
        }
    }

    [Fact]
    public async Task should_seed_the_admin_on_a_later_run_once_an_invalid_admin_password_is_corrected()
    {
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();

        await using (
            var initialHarness = await IdentitySeederHarness.CreateAsync(
                connection,
                new IdentitySeedOptions { AdminPassword = "abc", UserPassword = UserPassword },
                Environments.Development
            )
        )
        {
            await initialHarness.Seeder.SeedAllAsync();

            (await initialHarness.UserManager.FindByNameAsync(Admin.UserName!)).Should().BeNull();
            LogsAt(initialHarness, LogLevel.Error).Should().ContainSingle();
        }

        await using (
            var harness = await IdentitySeederHarness.CreateAsync(
                connection,
                new IdentitySeedOptions { AdminPassword = AdminPassword, UserPassword = UserPassword },
                Environments.Development
            )
        )
        {
            await harness.Seeder.SeedAllAsync();

            var admin = await harness.UserManager.FindByNameAsync(Admin.UserName!);
            admin.Should().NotBeNull();
            (await harness.UserManager.CheckPasswordAsync(admin!, AdminPassword)).Should().BeTrue();
            (await harness.Context.Users.CountAsync()).Should().Be(2);
            LogsAt(harness, LogLevel.Error).Should().BeEmpty();
        }
    }

    [Fact]
    public async Task should_seed_the_admin_when_unrelated_users_already_exist()
    {
        await using var harness = await IdentitySeederHarness.CreateAsync(
            new IdentitySeedOptions { AdminPassword = AdminPassword },
            Environments.Production
        );
        var existing = new User
        {
            Id = Guid.NewGuid(),
            UserName = "existing-user",
            Email = "existing@test.com",
            FirstName = "Existing",
            LastName = "User",
            PassPortNumber = "999999999",
            SecurityStamp = Guid.NewGuid().ToString(),
        };
        (await harness.UserManager.CreateAsync(existing, "existing-pass")).Succeeded.Should().BeTrue();

        await harness.Seeder.SeedAllAsync();

        (await harness.Context.Users.Select(u => u.UserName).ToListAsync())
            .Should()
            .BeEquivalentTo("existing-user", Admin.UserName);
        harness.Events.OfType<UserCreated>().Select(e => e.Id).Should().Equal(Admin.Id);
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Development")]
    public async Task should_not_create_duplicate_users_or_events_when_seeding_runs_again(string environment)
    {
        await using var harness = await IdentitySeederHarness.CreateAsync(
            new IdentitySeedOptions { AdminPassword = AdminPassword, UserPassword = UserPassword },
            environment
        );

        await harness.Seeder.SeedAllAsync();
        var usersAfterFirstRun = await harness.Context.Users.CountAsync();
        var eventsAfterFirstRun = harness.Events.Count;

        await harness.Seeder.SeedAllAsync();

        (await harness.Context.Users.CountAsync()).Should().Be(usersAfterFirstRun);
        harness.Events.Should().HaveCount(eventsAfterFirstRun);
        harness.Logger.Entries.Where(e => e.Level >= LogLevel.Warning).Should().BeEmpty();
    }

    [Fact]
    public async Task should_not_warn_about_a_missing_admin_password_when_the_admin_already_exists()
    {
        var options = new IdentitySeedOptions { AdminPassword = AdminPassword };
        await using var harness = await IdentitySeederHarness.CreateAsync(options, Environments.Production);
        await harness.Seeder.SeedAllAsync();

        options.AdminPassword = string.Empty;
        await harness.Seeder.SeedAllAsync();

        (await harness.UserManager.FindByNameAsync(Admin.UserName!)).Should().NotBeNull();
        LogsAt(harness, LogLevel.Warning).Should().BeEmpty();
        harness.Events.OfType<UserCreated>().Should().ContainSingle();
    }
}
