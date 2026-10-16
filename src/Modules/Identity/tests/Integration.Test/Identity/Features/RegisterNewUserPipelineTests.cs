using System;
using System.Linq;
using System.Threading.Tasks;
using BuildingBlocks.Constants;
using BuildingBlocks.EFCore;
using BuildingBlocks.TestBase;
using FluentAssertions;
using global::Identity.Data;
using global::Identity.Data.Seed;
using global::Identity.Host;
using global::Identity.Identity.Exceptions;
using global::Identity.Identity.Models;
using Integration.Test.Fakes;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using ValidationException = BuildingBlocks.Exception.ValidationException;

namespace Integration.Test.Identity.Features;

public class RegisterNewUserPipelineTests : IdentityIntegrationTestBase
{
    public RegisterNewUserPipelineTests(TestWriteFixture<Program, IdentityContext> integrationTestFactory)
        : base(integrationTestFactory) { }

    [Fact]
    public async Task should_throw_register_identity_user_exception_for_duplicate_username()
    {
        // samh is re-seeded by IdentityTestDataSeeder before every test
        var command = new FakeRegisterNewUserCommand().Generate() with
        {
            Username = "samh",
        };

        var act = () => Fixture.SendAsync(command);

        var exception = await act.Should().ThrowAsync<RegisterIdentityUserException>();
        exception.Which.Message.Should().Contain("already taken");

        var matchingUsers = await Fixture.ExecuteDbContextAsync(db => db.Users.CountAsync(u => u.UserName == "samh"));
        matchingUsers.Should().Be(1);
    }

    [Fact]
    public async Task should_throw_validation_exception_and_not_persist_when_command_is_invalid()
    {
        var command = new FakeRegisterNewUserCommand().Generate() with { ConfirmPassword = "Different@123" };

        var act = () => Fixture.SendAsync(command);

        await act.Should().ThrowAsync<ValidationException>();

        var persisted = await Fixture.ExecuteDbContextAsync(db =>
            db.Users.AnyAsync(u => u.UserName == command.Username)
        );
        persisted.Should().BeFalse();
    }

    [Fact]
    public async Task should_assign_user_role_and_store_hashed_password()
    {
        var command = new FakeRegisterNewUserCommand().Generate() with { Username = "role-user-1" };

        var result = await Fixture.SendAsync(command);

        result.Should().NotBeNull();

        using var scope = Fixture.ServiceProvider.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();

        var user = await userManager.FindByNameAsync(command.Username);
        user.Should().NotBeNull();
        user!.PasswordHash.Should().NotBe(command.Password);
        (await userManager.CheckPasswordAsync(user, command.Password)).Should().BeTrue();
        (await userManager.IsInRoleAsync(user, IdentityConstant.Role.User)).Should().BeTrue();
    }

    [Fact]
    public async Task should_seed_default_roles_and_users_idempotently()
    {
        await Fixture.ExecuteDbContextAsync(
            (Func<IdentityContext, Task>)(
                async db =>
                {
                    db.Users.RemoveRange(db.Users);
                    db.Roles.RemoveRange(db.Roles);
                    await db.SaveChangesAsync();
                }
            )
        );

        using var scope = Fixture.ServiceProvider.CreateScope();
        var seeder = scope.ServiceProvider.GetRequiredService<IDataSeeder>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<Role>>();

        await seeder.SeedAllAsync();

        (await roleManager.RoleExistsAsync(IdentityConstant.Role.Admin)).Should().BeTrue();
        (await roleManager.RoleExistsAsync(IdentityConstant.Role.User)).Should().BeTrue();

        var samh = await userManager.FindByNameAsync("samh");
        samh.Should().NotBeNull();
        (await userManager.CheckPasswordAsync(samh!, "Admin@123456")).Should().BeTrue();
        (await userManager.IsInRoleAsync(samh!, IdentityConstant.Role.Admin)).Should().BeTrue();

        var samh2 = await userManager.FindByNameAsync("samh2");
        samh2.Should().NotBeNull();
        (await userManager.IsInRoleAsync(samh2!, IdentityConstant.Role.User)).Should().BeTrue();

        await seeder.SeedAllAsync();

        (await userManager.Users.CountAsync()).Should().Be(2);
        (await roleManager.Roles.CountAsync()).Should().Be(2);
    }
}
