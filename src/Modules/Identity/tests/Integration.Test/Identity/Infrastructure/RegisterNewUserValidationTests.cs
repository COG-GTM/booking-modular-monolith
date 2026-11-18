using BuildingBlocks.Core;
using FluentAssertions;
using global::Identity.Identity.Exceptions;
using global::Identity.Identity.Features.RegisteringNewUser.V1;
using global::Identity.Identity.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Integration.Test.Identity.Infrastructure;

public class RegisterNewUserValidationTests
{
    [Fact]
    public void should_reject_eleven_character_password()
    {
        var result = new RegisterNewUserValidator().Validate(Command("Flight#2026"));

        result.Errors.Should().ContainSingle().Which.ErrorMessage.Should().Contain("at least 12 characters");
        result.Errors.Single().PropertyName.Should().Be(nameof(RegisterNewUser.Password));
    }

    [Fact]
    public void should_accept_twelve_character_password()
    {
        var result = new RegisterNewUserValidator().Validate(Command("Flight#2026!"));

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task should_not_assign_role_when_user_creation_fails()
    {
        using var manager = Substitute.For<UserManager<User>>(
            Substitute.For<IUserStore<User>>(),
            Options.Create(new IdentityOptions()),
            Substitute.For<IPasswordHasher<User>>(),
            Array.Empty<IUserValidator<User>>(),
            Array.Empty<IPasswordValidator<User>>(),
            Substitute.For<ILookupNormalizer>(),
            new IdentityErrorDescriber(),
            Substitute.For<IServiceProvider>(),
            Substitute.For<ILogger<UserManager<User>>>()
        );
        manager
            .CreateAsync(Arg.Any<User>(), Arg.Any<string>())
            .Returns(IdentityResult.Failed(new IdentityError { Description = "Password rejected" }));
        var handler = new RegisterNewUserHandler(manager, Substitute.For<IEventDispatcher>());

        var act = () => handler.Handle(Command("Flight#Deck2026"), CancellationToken.None);

        await act.Should().ThrowAsync<RegisterIdentityUserException>().WithMessage("*Password rejected*");
        await manager.Received(1).CreateAsync(Arg.Is<User>(user => user.UserName == "traveler"), "Flight#Deck2026");
        await manager.DidNotReceive().AddToRoleAsync(Arg.Any<User>(), Arg.Any<string>());
    }

    private static RegisterNewUser Command(string password) =>
        new("First", "Last", "traveler", "traveler@test.com", password, password, "12345678");
}
