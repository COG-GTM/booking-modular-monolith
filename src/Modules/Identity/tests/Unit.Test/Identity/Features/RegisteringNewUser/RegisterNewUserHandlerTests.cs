namespace Unit.Test.Identity.Features.RegisteringNewUser;

using global::Identity;
using BuildingBlocks.Constants;
using BuildingBlocks.Contracts.EventBus.Messages;
using BuildingBlocks.Core;
using BuildingBlocks.Exception;
using FluentAssertions;
using global::Identity.Identity.Exceptions;
using global::Identity.Identity.Features.RegisteringNewUser.V1;
using global::Identity.Identity.Models;
using Microsoft.AspNetCore.Identity;
using NSubstitute;
using Unit.Test.Fakes;
using Xunit;

public class RegisterNewUserHandlerTests
{
    private readonly UserManager<User> _userManager;
    private readonly IEventDispatcher<IdentityRoot> _eventDispatcher;
    private readonly RegisterNewUserHandler _handler;

    public RegisterNewUserHandlerTests()
    {
        _userManager = Substitute.For<UserManager<User>>(
            Substitute.For<IUserStore<User>>(),
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null
        );
        _eventDispatcher = Substitute.For<IEventDispatcher<IdentityRoot>>();
        _handler = new RegisterNewUserHandler(_userManager, _eventDispatcher);
    }

    [Fact]
    public async Task should_create_user_assign_role_dispatch_event_and_return_result()
    {
        var request = new FakeRegisterNewUser().Generate();
        _userManager.CreateAsync(Arg.Any<User>(), Arg.Any<string>()).Returns(IdentityResult.Success);
        _userManager.AddToRoleAsync(Arg.Any<User>(), Arg.Any<string>()).Returns(IdentityResult.Success);

        var result = await _handler.Handle(request, CancellationToken.None);

        await _userManager
            .Received(1)
            .CreateAsync(
                Arg.Is<User>(u =>
                    u.FirstName == request.FirstName
                    && u.LastName == request.LastName
                    && u.UserName == request.Username
                    && u.Email == request.Email
                    && u.PassPortNumber == request.PassportNumber
                ),
                request.Password
            );
        await _userManager.Received(1).AddToRoleAsync(Arg.Any<User>(), IdentityConstant.Role.User);
        await _eventDispatcher
            .Received(1)
            .SendAsync(
                Arg.Is<UserCreated>(e =>
                    e.Name == $"{request.FirstName} {request.LastName}" && e.PassportNumber == request.PassportNumber
                ),
                Arg.Any<Type>(),
                Arg.Any<CancellationToken>()
            );

        result.FirstName.Should().Be(request.FirstName);
        result.LastName.Should().Be(request.LastName);
        result.Username.Should().Be(request.Username);
        result.PassportNumber.Should().Be(request.PassportNumber);
    }

    [Fact]
    public async Task should_throw_register_identity_user_exception_when_user_creation_fails()
    {
        var request = new FakeRegisterNewUser().Generate();
        _userManager
            .CreateAsync(Arg.Any<User>(), Arg.Any<string>())
            .Returns(
                IdentityResult.Failed(new IdentityError { Description = "a" }, new IdentityError { Description = "b" })
            );
        _userManager.AddToRoleAsync(Arg.Any<User>(), Arg.Any<string>()).Returns(IdentityResult.Success);

        var act = () => _handler.Handle(request, CancellationToken.None);

        var exception = await act.Should().ThrowAsync<RegisterIdentityUserException>();
        exception.Which.Message.Should().Be("a,b");

        await _eventDispatcher
            .DidNotReceiveWithAnyArgs()
            .SendAsync(Arg.Any<UserCreated>(), Arg.Any<Type>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task should_throw_register_identity_user_exception_when_role_assignment_fails()
    {
        var request = new FakeRegisterNewUser().Generate();
        _userManager.CreateAsync(Arg.Any<User>(), Arg.Any<string>()).Returns(IdentityResult.Success);
        _userManager
            .AddToRoleAsync(Arg.Any<User>(), Arg.Any<string>())
            .Returns(
                IdentityResult.Failed(
                    new IdentityError { Description = "role error 1" },
                    new IdentityError { Description = "role error 2" }
                )
            );

        var act = () => _handler.Handle(request, CancellationToken.None);

        var exception = await act.Should().ThrowAsync<RegisterIdentityUserException>();
        exception.Which.Message.Should().Be("role error 1,role error 2");
        await _eventDispatcher
            .DidNotReceiveWithAnyArgs()
            .SendAsync(Arg.Any<UserCreated>(), Arg.Any<Type>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task should_throw_argument_null_exception_when_request_is_null()
    {
        var act = () => _handler.Handle(null!, CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }
}
