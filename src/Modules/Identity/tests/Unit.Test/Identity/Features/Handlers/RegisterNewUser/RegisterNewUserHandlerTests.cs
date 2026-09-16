using BuildingBlocks.Contracts.EventBus.Messages;
using BuildingBlocks.Core;
using FluentAssertions;
using Identity.Identity.Exceptions;
using Identity.Identity.Models;
using Microsoft.AspNetCore.Identity;
using NSubstitute;
using Unit.Test.Common;
using Unit.Test.Fakes;
using Xunit;

namespace Unit.Test.Identity.Features.Handlers.RegisterNewUser;

using global::Identity.Identity.Features.RegisteringNewUser.V1;

[Collection(nameof(UnitTestFixture))]
public class RegisterNewUserHandlerTests
{
    private readonly UserManager<User> _userManager;
    private readonly IEventDispatcher _eventDispatcher;
    private readonly RegisterNewUserHandler _handler;

    public RegisterNewUserHandlerTests()
    {
        _userManager = FakeUserManager.Create();
        _eventDispatcher = Substitute.For<IEventDispatcher>();
        _handler = new RegisterNewUserHandler(_userManager, _eventDispatcher);
    }

    private Task<RegisterNewUserResult> Act(
        global::Identity.Identity.Features.RegisteringNewUser.V1.RegisterNewUser command,
        CancellationToken cancellationToken
    ) => _handler.Handle(command, cancellationToken);

    [Fact]
    public async Task handler_with_valid_command_should_create_user_assign_role_and_publish_event()
    {
        // Arrange
        var command = new FakeRegisterNewUserCommand().Generate();

        // Act
        var response = await Act(command, CancellationToken.None);

        // Assert
        response.Should().NotBeNull();
        response.FirstName.Should().Be(command.FirstName);
        response.LastName.Should().Be(command.LastName);
        response.Username.Should().Be(command.Username);
        response.PassportNumber.Should().Be(command.PassportNumber);

        _userManager
            .Received(1)
            .CreateAsync(
                Arg.Is<User>(u => u.UserName == command.Username && u.Email == command.Email),
                command.Password
            );
        _userManager.Received(1).AddToRoleAsync(Arg.Any<User>(), "user");
        _eventDispatcher
            .Received(1)
            .SendAsync(
                Arg.Is<UserCreated>(e =>
                    e.Name == $"{command.FirstName} {command.LastName}" && e.PassportNumber == command.PassportNumber
                ),
                Arg.Any<Type>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task handler_should_throw_when_user_creation_fails()
    {
        // Arrange
        var command = new FakeRegisterNewUserCommand().Generate();
        _userManager
            .CreateAsync(Arg.Any<User>(), Arg.Any<string>())
            .Returns(IdentityResult.Failed(new IdentityError { Description = "Duplicate user" }));

        // Act
        Func<Task> act = async () =>
        {
            await Act(command, CancellationToken.None);
        };

        // Assert
        (await act.Should().ThrowAsync<RegisterIdentityUserException>()).WithMessage("*Duplicate user*");
        _eventDispatcher
            .DidNotReceive()
            .SendAsync(Arg.Any<UserCreated>(), Arg.Any<Type>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task handler_should_throw_when_role_assignment_fails()
    {
        // Arrange
        var command = new FakeRegisterNewUserCommand().Generate();
        _userManager
            .AddToRoleAsync(Arg.Any<User>(), Arg.Any<string>())
            .Returns(IdentityResult.Failed(new IdentityError { Description = "Role missing" }));

        // Act
        Func<Task> act = async () =>
        {
            await Act(command, CancellationToken.None);
        };

        // Assert
        (await act.Should().ThrowAsync<RegisterIdentityUserException>()).WithMessage("*Role missing*");
        _eventDispatcher
            .DidNotReceive()
            .SendAsync(Arg.Any<UserCreated>(), Arg.Any<Type>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task handler_with_null_command_should_throw_argument_exception()
    {
        // Arrange
        global::Identity.Identity.Features.RegisteringNewUser.V1.RegisterNewUser command = null;

        // Act
        Func<Task> act = async () =>
        {
            await Act(command, CancellationToken.None);
        };

        // Assert
        await act.Should().ThrowAsync<ArgumentNullException>();
    }
}
