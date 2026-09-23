using System;
using System.Linq;
using System.Threading.Tasks;
using Api;
using BuildingBlocks.Contracts.EventBus.Messages;
using BuildingBlocks.Exception;
using BuildingBlocks.PersistMessageProcessor;
using BuildingBlocks.TestBase;
using FluentAssertions;
using Identity.Data;
using Identity.Identity.Exceptions;
using Integration.Test.Fakes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Integration.Test.Identity.Features;

public class RegisterNewUserTests : IdentityIntegrationTestBase
{
    public RegisterNewUserTests(TestWriteFixture<Program, IdentityContext> integrationTestFactory)
        : base(integrationTestFactory) { }

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
    public async Task should_throw_register_identity_user_exception_and_not_publish_message_when_username_is_duplicate()
    {
        // Arrange
        var firstCommand = new FakeRegisterNewUserCommand().Generate() with
        {
            PassportNumber = Guid.NewGuid().ToString("N"),
        };
        await Fixture.SendAsync(firstCommand);

        var duplicateCommand = new FakeRegisterNewUserCommand().Generate() with
        {
            Email = "duplicate@test.com",
            PassportNumber = Guid.NewGuid().ToString("N"),
        };

        // Act
        Func<Task> act = async () => await Fixture.SendAsync(duplicateCommand);

        // Assert
        (await act.Should().ThrowAsync<RegisterIdentityUserException>())
            .Which.Message.Should()
            .Contain(duplicateCommand.Username);

        (await CountUsersByUsernameAsync(duplicateCommand.Username)).Should().Be(1);

        (await CountOutboxUserCreatedByPassportNumberAsync(firstCommand.PassportNumber)).Should().Be(1);
        (await CountOutboxUserCreatedByPassportNumberAsync(duplicateCommand.PassportNumber)).Should().Be(0);
    }

    [Fact]
    public async Task should_throw_register_identity_user_exception_and_not_publish_message_when_password_violates_policy()
    {
        // Arrange
        var command = new FakeRegisterNewUserCommand().Generate() with
        {
            Password = "12345",
            ConfirmPassword = "12345",
            PassportNumber = Guid.NewGuid().ToString("N"),
        };

        // Act
        Func<Task> act = async () => await Fixture.SendAsync(command);

        // Assert
        (await act.Should().ThrowAsync<RegisterIdentityUserException>())
            .Which.Message.Should()
            .Contain("Passwords must be at least 6 characters");

        (await CountUsersByUsernameAsync(command.Username)).Should().Be(0);
        (await CountOutboxUserCreatedByPassportNumberAsync(command.PassportNumber)).Should().Be(0);
    }

    [Fact]
    public async Task should_throw_validation_exception_and_not_create_user_when_command_is_invalid()
    {
        // Arrange
        var command = new FakeRegisterNewUserCommand().Generate() with
        {
            ConfirmPassword = "Mismatch@123",
            PassportNumber = Guid.NewGuid().ToString("N"),
        };

        // Act
        Func<Task> act = async () => await Fixture.SendAsync(command);

        // Assert
        (await act.Should().ThrowAsync<ValidationException>())
            .Which.Message.Should()
            .Be("Passwords should match");

        (await CountUsersByUsernameAsync(command.Username)).Should().Be(0);
        (await CountOutboxUserCreatedByPassportNumberAsync(command.PassportNumber)).Should().Be(0);
    }

    private Task<int> CountUsersByUsernameAsync(string username)
    {
        return Fixture.ExecuteDbContextAsync(db => db.Users.CountAsync(x => x.UserName == username));
    }

    private async Task<int> CountOutboxUserCreatedByPassportNumberAsync(string passportNumber)
    {
        using var scope = Fixture.ServiceProvider.CreateScope();
        var persistMessageDbContext = scope.ServiceProvider.GetRequiredService<PersistMessageDbContext>();

        var dataType = typeof(UserCreated).ToString();

        return await persistMessageDbContext
            .PersistMessage.Where(x => x.DeliveryType == MessageDeliveryType.Outbox && x.DataType == dataType)
            .CountAsync(x => x.Data.Contains(passportNumber));
    }
}
