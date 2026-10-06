using BuildingBlocks.Constants;
using BuildingBlocks.Contracts.EventBus.Messages;
using FluentAssertions;
using Identity.Configurations;
using Identity.Data.Seed;
using Identity.Identity.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Unit.Test.Common;
using Xunit;

namespace Unit.Test.Data.Seed;

public class IdentityDataSeederSideEffectTests
{
    private const string AdminPassword = "configured-admin-pass";
    private const string UserPassword = "configured-user-pass";

    [Fact]
    public async Task should_publish_user_created_for_each_seeded_user_in_development()
    {
        await using var host = await SeederTestHost.CreateAsync(
            Environments.Development,
            new IdentitySeedOptions { AdminPassword = AdminPassword, UserPassword = UserPassword }
        );

        await host.Seeder.SeedAllAsync();

        var admin = InitialData.Users.First();
        var demoUser = InitialData.Users.Last();

        await host
            .EventDispatcher.Received(1)
            .SendAsync(
                Arg.Is<UserCreated>(e =>
                    e.Id == admin.Id
                    && e.Name == admin.FirstName + " " + admin.LastName
                    && e.PassportNumber == admin.PassPortNumber
                ),
                Arg.Any<Type>(),
                Arg.Any<CancellationToken>()
            );

        await host
            .EventDispatcher.Received(1)
            .SendAsync(
                Arg.Is<UserCreated>(e =>
                    e.Id == demoUser.Id
                    && e.Name == demoUser.FirstName + " " + demoUser.LastName
                    && e.PassportNumber == demoUser.PassPortNumber
                ),
                Arg.Any<Type>(),
                Arg.Any<CancellationToken>()
            );

        await host
            .EventDispatcher.Received(2)
            .SendAsync(Arg.Any<UserCreated>(), Arg.Any<Type>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task should_publish_single_user_created_for_admin_outside_development()
    {
        await using var host = await SeederTestHost.CreateAsync(
            Environments.Production,
            new IdentitySeedOptions { AdminPassword = AdminPassword, UserPassword = UserPassword }
        );

        await host.Seeder.SeedAllAsync();

        await host
            .EventDispatcher.Received(1)
            .SendAsync(
                Arg.Is<UserCreated>(e => e.Id == InitialData.Users.First().Id),
                Arg.Any<Type>(),
                Arg.Any<CancellationToken>()
            );
        await host
            .EventDispatcher.DidNotReceive()
            .SendAsync(
                Arg.Is<UserCreated>(e => e.Id == InitialData.Users.Last().Id),
                Arg.Any<Type>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task should_log_warning_and_publish_nothing_when_admin_password_is_missing()
    {
        await using var host = await SeederTestHost.CreateAsync(Environments.Production, new IdentitySeedOptions());

        await host.Seeder.SeedAllAsync();

        await host
            .EventDispatcher.DidNotReceiveWithAnyArgs()
            .SendAsync(Arg.Any<UserCreated>(), Arg.Any<Type>(), Arg.Any<CancellationToken>());

        host.Logger.Entries.Should()
            .ContainSingle(e =>
                e.Level == LogLevel.Warning
                && e.Message.Contains(nameof(IdentitySeedOptions))
                && e.Message.Contains(nameof(IdentitySeedOptions.AdminPassword))
            );
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task should_treat_blank_admin_password_as_not_configured(string blankPassword)
    {
        await using var host = await SeederTestHost.CreateAsync(
            Environments.Production,
            new IdentitySeedOptions { AdminPassword = blankPassword }
        );

        await host.Seeder.SeedAllAsync();

        (await host.Context.Users.AnyAsync()).Should().BeFalse();
        host.Logger.Entries.Should().ContainSingle(e => e.Level == LogLevel.Warning);
    }

    [Fact]
    public async Task should_not_log_warning_when_admin_password_is_configured()
    {
        await using var host = await SeederTestHost.CreateAsync(
            Environments.Production,
            new IdentitySeedOptions { AdminPassword = AdminPassword }
        );

        await host.Seeder.SeedAllAsync();

        host.Logger.Entries.Should().NotContain(e => e.Level >= LogLevel.Warning);
    }

    [Fact]
    public async Task should_seed_only_admin_in_development_when_user_password_is_missing()
    {
        await using var host = await SeederTestHost.CreateAsync(
            Environments.Development,
            new IdentitySeedOptions { AdminPassword = AdminPassword, UserPassword = "  " }
        );

        await host.Seeder.SeedAllAsync();

        (await host.UserManager.FindByNameAsync(InitialData.Users.First().UserName!)).Should().NotBeNull();
        (await host.UserManager.FindByNameAsync(InitialData.Users.Last().UserName!)).Should().BeNull();
        (await host.Context.Users.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task should_still_seed_demo_user_in_development_when_only_user_password_is_configured()
    {
        await using var host = await SeederTestHost.CreateAsync(
            Environments.Development,
            new IdentitySeedOptions { UserPassword = UserPassword }
        );

        await host.Seeder.SeedAllAsync();

        (await host.UserManager.FindByNameAsync(InitialData.Users.First().UserName!)).Should().BeNull();

        var demoUser = await host.UserManager.FindByNameAsync(InitialData.Users.Last().UserName!);
        demoUser.Should().NotBeNull();
        (await host.UserManager.IsInRoleAsync(demoUser!, IdentityConstant.Role.User)).Should().BeTrue();
        (await host.UserManager.IsInRoleAsync(demoUser!, IdentityConstant.Role.Admin)).Should().BeFalse();
        (await host.Context.Users.CountAsync()).Should().Be(1);
        host.Logger.Entries.Should().ContainSingle(e => e.Level == LogLevel.Warning);
    }

    [Fact]
    public async Task should_seed_initial_users_even_when_other_users_already_exist()
    {
        await using var host = await SeederTestHost.CreateAsync(
            Environments.Development,
            new IdentitySeedOptions { AdminPassword = AdminPassword, UserPassword = UserPassword }
        );

        var existing = NewUser("existing", "00000000");
        (await host.UserManager.CreateAsync(existing, "existing-pass")).Succeeded.Should().BeTrue();

        await host.Seeder.SeedAllAsync();

        (await host.Context.Users.CountAsync()).Should().Be(3);
        (await host.UserManager.FindByNameAsync(InitialData.Users.First().UserName!)).Should().NotBeNull();
        (await host.UserManager.FindByNameAsync(InitialData.Users.Last().UserName!)).Should().NotBeNull();
        await host
            .EventDispatcher.Received(2)
            .SendAsync(Arg.Any<UserCreated>(), Arg.Any<Type>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task should_not_overwrite_or_republish_an_initial_user_that_already_exists()
    {
        await using var host = await SeederTestHost.CreateAsync(
            Environments.Production,
            new IdentitySeedOptions { AdminPassword = AdminPassword }
        );

        var preExistingAdmin = NewUser(InitialData.Users.First().UserName!, "11111111");
        (await host.UserManager.CreateAsync(preExistingAdmin, "pre-existing-pass")).Succeeded.Should().BeTrue();

        await host.Seeder.SeedAllAsync();

        (await host.Context.Users.CountAsync()).Should().Be(1);
        var admin = await host.UserManager.FindByNameAsync(InitialData.Users.First().UserName!);
        (await host.UserManager.CheckPasswordAsync(admin!, "pre-existing-pass")).Should().BeTrue();
        (await host.UserManager.CheckPasswordAsync(admin!, AdminPassword)).Should().BeFalse();
        (await host.UserManager.IsInRoleAsync(admin!, IdentityConstant.Role.Admin)).Should().BeFalse();
        await host
            .EventDispatcher.DidNotReceiveWithAnyArgs()
            .SendAsync(Arg.Any<UserCreated>(), Arg.Any<Type>(), Arg.Any<CancellationToken>());
        host.Logger.Entries.Should().NotContain(e => e.Level >= LogLevel.Warning);
    }

    [Fact]
    public async Task should_log_error_and_publish_nothing_when_configured_password_is_rejected()
    {
        await using var host = await SeederTestHost.CreateAsync(
            Environments.Production,
            new IdentitySeedOptions { AdminPassword = "short" }
        );

        await host.Seeder.SeedAllAsync();

        (await host.Context.Users.AnyAsync()).Should().BeFalse();
        (await host.Context.UserRoles.AnyAsync()).Should().BeFalse();
        await host
            .EventDispatcher.DidNotReceiveWithAnyArgs()
            .SendAsync(Arg.Any<UserCreated>(), Arg.Any<Type>(), Arg.Any<CancellationToken>());
        host.Logger.Entries.Should()
            .ContainSingle(e => e.Level == LogLevel.Error && e.Message.Contains(InitialData.Users.First().UserName!));
    }

    [Fact]
    public async Task should_be_idempotent_when_run_twice()
    {
        await using var host = await SeederTestHost.CreateAsync(
            Environments.Development,
            new IdentitySeedOptions { AdminPassword = AdminPassword, UserPassword = UserPassword }
        );

        await host.Seeder.SeedAllAsync();
        await host.Seeder.SeedAllAsync();

        (await host.Context.Users.CountAsync()).Should().Be(2);
        (await host.Context.Roles.CountAsync()).Should().Be(2);
        await host
            .EventDispatcher.Received(2)
            .SendAsync(Arg.Any<UserCreated>(), Arg.Any<Type>(), Arg.Any<CancellationToken>());
    }

    private static User NewUser(string userName, string passportNumber) =>
        new()
        {
            Id = Guid.NewGuid(),
            FirstName = "Existing",
            LastName = "User",
            UserName = userName,
            PassPortNumber = passportNumber,
            Email = $"{userName}@test.com",
            SecurityStamp = Guid.NewGuid().ToString(),
        };
}
