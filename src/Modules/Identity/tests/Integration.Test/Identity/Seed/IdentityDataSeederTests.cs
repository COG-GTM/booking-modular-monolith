using System;
using System.Linq;
using System.Threading.Tasks;
using Api;
using BuildingBlocks.Core;
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
using Microsoft.Extensions.Options;
using Xunit;

namespace Integration.Test.Identity.Seed;

public class IdentityDataSeederTests : IdentityIntegrationTestBase
{
    public IdentityDataSeederTests(
        TestWriteFixture<Program, IdentityContext> integrationTestFactory) : base(integrationTestFactory)
    {
    }

    [Fact]
    public async Task should_fail_and_create_no_users_when_admin_password_is_not_configured()
    {
        await DeleteAllUsers();

        var act = () => RunSeeder(new IdentitySeedOptions());

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*IdentitySeedOptions:AdminPassword*");

        (await Fixture.ExecuteDbContextAsync(db => db.Users.CountAsync())).Should().Be(0);
    }

    [Fact]
    public async Task should_create_admin_with_configured_password_only()
    {
        await DeleteAllUsers();
        var adminPassword = Guid.NewGuid().ToString("N");

        await RunSeeder(new IdentitySeedOptions { AdminPassword = adminPassword });

        using var scope = Fixture.ServiceProvider.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
        var admin = await userManager.FindByNameAsync(InitialData.Users.First().UserName!);

        admin.Should().NotBeNull();
        (await userManager.IsInRoleAsync(admin!, IdentityConstant.Role.Admin)).Should().BeTrue();
        (await userManager.CheckPasswordAsync(admin!, adminPassword)).Should().BeTrue();
        (await userManager.FindByNameAsync(InitialData.Users.Last().UserName!)).Should().BeNull();
    }

    [Fact]
    public async Task should_not_require_admin_password_when_users_already_exist()
    {
        var act = () => RunSeeder(new IdentitySeedOptions());

        await act.Should().NotThrowAsync();
    }

    private Task DeleteAllUsers() =>
        Fixture.ExecuteDbContextAsync(async db =>
        {
            await db.UserRoles.ExecuteDeleteAsync();
            await db.UserClaims.ExecuteDeleteAsync();
            await db.UserLogins.ExecuteDeleteAsync();
            await db.UserTokens.ExecuteDeleteAsync();
            await db.Users.ExecuteDeleteAsync();
        });

    private async Task RunSeeder(IdentitySeedOptions options)
    {
        using var scope = Fixture.ServiceProvider.CreateScope();
        var sp = scope.ServiceProvider;

        var seeder = new IdentityDataSeeder(
            sp.GetRequiredService<UserManager<User>>(),
            sp.GetRequiredService<RoleManager<Role>>(),
            sp.GetRequiredService<IEventDispatcher>(),
            sp.GetRequiredService<IdentityContext>(),
            Options.Create(options));

        await seeder.SeedAllAsync();
    }
}
