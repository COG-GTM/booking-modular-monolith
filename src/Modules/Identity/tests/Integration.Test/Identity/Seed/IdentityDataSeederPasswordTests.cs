using System;
using System.Threading.Tasks;
using Api;
using BuildingBlocks.Constants;
using BuildingBlocks.Core;
using BuildingBlocks.TestBase;
using FluentAssertions;
using Identity.Configurations;
using Identity.Data;
using Identity.Data.Seed;
using Identity.Identity.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Integration.Test.Identity.Seed;

public class IdentityDataSeederPasswordTests : IdentityIntegrationTestBase
{
    public IdentityDataSeederPasswordTests(TestWriteFixture<Program, IdentityContext> integrationTestFactory)
        : base(integrationTestFactory) { }

    [Fact]
    public async Task should_create_optional_user_with_configured_password_and_user_role()
    {
        await DeleteAllUsers();
        var userPassword = Guid.NewGuid().ToString("N");

        await RunSeeder(
            new IdentitySeedOptions { AdminPassword = Guid.NewGuid().ToString("N"), UserPassword = userPassword }
        );

        using var scope = Fixture.ServiceProvider.CreateScope();
        var manager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
        var user = await manager.FindByNameAsync("samh2");

        user.Should().NotBeNull();
        (await manager.IsInRoleAsync(user!, IdentityConstant.Role.User)).Should().BeTrue();
        (await manager.CheckPasswordAsync(user!, userPassword)).Should().BeTrue();
    }

    [Theory]
    [InlineData("a")]
    [InlineData("12345")]
    public async Task should_throw_naming_the_user_when_admin_password_is_invalid(string password)
    {
        await DeleteAllUsers();

        var act = () => RunSeeder(new IdentitySeedOptions { AdminPassword = password });

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*Seeding user 'samh' failed:*");
        (await Fixture.ExecuteDbContextAsync(db => db.Users.CountAsync())).Should().Be(0);
    }

    private Task DeleteAllUsers() =>
        Fixture.ExecuteDbContextAsync(
            async Task (IdentityContext db) =>
            {
                await db.UserRoles.ExecuteDeleteAsync();
                await db.UserClaims.ExecuteDeleteAsync();
                await db.UserLogins.ExecuteDeleteAsync();
                await db.UserTokens.ExecuteDeleteAsync();
                await db.Users.ExecuteDeleteAsync();
            }
        );

    private async Task RunSeeder(IdentitySeedOptions options)
    {
        using var scope = Fixture.ServiceProvider.CreateScope();
        var sp = scope.ServiceProvider;
        var seeder = new IdentityDataSeeder(
            sp.GetRequiredService<UserManager<User>>(),
            sp.GetRequiredService<RoleManager<Role>>(),
            sp.GetRequiredService<IEventDispatcher>(),
            sp.GetRequiredService<IdentityContext>(),
            Options.Create(options)
        );
        await seeder.SeedAllAsync();
    }
}
