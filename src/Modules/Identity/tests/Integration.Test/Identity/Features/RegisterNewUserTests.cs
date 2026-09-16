using System.Threading.Tasks;
using Api;
using BuildingBlocks.Contracts.EventBus.Messages;
using BuildingBlocks.Exception;
using BuildingBlocks.TestBase;
using FluentAssertions;
using Identity.Data;
using Identity.Identity.Exceptions;
using Identity.Identity.Models;
using Microsoft.EntityFrameworkCore;
using Integration.Test.Fakes;
using Xunit;

namespace Integration.Test.Identity.Features;

public class RegisterNewUserTests : IdentityIntegrationTestBase
{
    public RegisterNewUserTests(
        TestWriteFixture<Program, IdentityContext> integrationTestFactory) : base(integrationTestFactory)
    {
    }

    [Fact]
    public async Task should_create_new_user_to_db_and_publish_message_to_broker()
    {
        // Arrange
        var command = new FakeRegisterNewUserCommand().Generate();

        // Act
        var response = await Fixture.SendAsync(command);

        // Assert
        response?.Should().NotBeNull();
        response?.Username.Should().Be(command.Username);

        (await Fixture.WaitForPublishing<UserCreated>()).Should().Be(true);
    }

    [Fact]
    public async Task should_persist_user_with_user_role()
    {
        // Arrange
        var command = new FakeRegisterNewUserCommand().Generate() with
        {
            Username = "role_" + Guid.NewGuid().ToString("N")[..8],
            Email = Guid.NewGuid().ToString("N")[..8] + "@test.com"
        };

        // Act
        var response = await Fixture.SendAsync(command);

        // Assert
        var user = await Fixture.ExecuteDbContextAsync(db =>
            db.Users.SingleOrDefaultAsync(x => x.Id == response.Id));

        user.Should().NotBeNull();
        user!.UserName.Should().Be(command.Username);
        user.PassPortNumber.Should().Be(command.PassportNumber);

        var hasUserRole = await Fixture.ExecuteDbContextAsync(db =>
            (from ur in db.UserRoles
             join r in db.Roles on ur.RoleId equals r.Id
             where ur.UserId == response.Id && r.Name == BuildingBlocks.Constants.IdentityConstant.Role.User
             select ur).AnyAsync());

        hasUserRole.Should().BeTrue();
    }

    [Fact]
    public async Task should_throw_when_username_already_exists()
    {
        // Arrange
        var command = new FakeRegisterNewUserCommand().Generate() with
        {
            Username = "dup_" + Guid.NewGuid().ToString("N")[..8]
        };

        await Fixture.SendAsync(command);

        var duplicate = command with { Email = "other_" + command.Email, PassportNumber = "X" + command.PassportNumber };

        // Act
        Func<Task> act = async () => await Fixture.SendAsync(duplicate);

        // Assert
        await act.Should().ThrowAsync<RegisterIdentityUserException>();
    }

    [Fact]
    public async Task should_throw_validation_exception_when_passwords_do_not_match()
    {
        // Arrange
        var command = new FakeRegisterNewUserCommand().Generate() with { ConfirmPassword = "Different@123" };

        // Act
        Func<Task> act = async () => await Fixture.SendAsync(command);

        // Assert
        await act.Should().ThrowAsync<ValidationException>();
    }
}
