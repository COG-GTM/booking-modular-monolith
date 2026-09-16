using BuildingBlocks.Contracts.EventBus.Messages;
using BuildingBlocks.Core;
using BuildingBlocks.Core.Event;
using BuildingBlocks.Web;
using FluentAssertions;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Unit.Test.Common;
using Unit.Test.Fakes;
using Xunit;

namespace Unit.Test.Passenger.Features.Handlers.RegisterNewUser;

using global::Passenger.Identity.Consumers.RegisteringNewUser.V1;

[Collection(nameof(UnitTestFixture))]
public class RegisterNewUserConsumerTests
{
    private readonly UnitTestFixture _fixture;
    private readonly IEventDispatcher _eventDispatcher;
    private readonly RegisterNewUserHandler _consumer;

    public RegisterNewUserConsumerTests(UnitTestFixture fixture)
    {
        _fixture = fixture;
        _eventDispatcher = Substitute.For<IEventDispatcher>();
        _consumer = new RegisterNewUserHandler(
            _fixture.DbContext,
            _eventDispatcher,
            NullLogger<RegisterNewUserHandler>.Instance,
            Options.Create(new AppOptions { Name = "Passenger" })
        );
    }

    private Task Act(UserCreated message)
    {
        var context = Substitute.For<ConsumeContext<UserCreated>>();
        context.Message.Returns(message);
        return _consumer.Consume(context);
    }

    [Fact]
    public async Task consumer_should_create_passenger_and_dispatch_internal_command_for_new_passport()
    {
        // Arrange
        var message = new FakeUserCreated().Generate();

        // Act
        await Act(message);

        // Assert
        var passenger = await _fixture.DbContext.Passengers.SingleOrDefaultAsync(x =>
            x.PassportNumber.Value == message.PassportNumber
        );
        passenger.Should().NotBeNull();
        passenger!.Name.Value.Should().Be(message.Name);

        _ = _eventDispatcher
            .Received(1)
            .SendAsync(
                Arg.Is<PassengerCreatedDomainEvent>(e => e.PassportNumber == message.PassportNumber),
                typeof(IInternalCommand),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task consumer_should_skip_when_passport_already_registered()
    {
        // Arrange
        var message = new FakeUserCreated(FakePassengerCreate.SeededPassportNumber).Generate();
        var countBefore = await _fixture.DbContext.Passengers.CountAsync();

        // Act
        await Act(message);

        // Assert
        (await _fixture.DbContext.Passengers.CountAsync())
            .Should()
            .Be(countBefore);
        _ = _eventDispatcher
            .DidNotReceive()
            .SendAsync(Arg.Any<PassengerCreatedDomainEvent>(), Arg.Any<Type>(), Arg.Any<CancellationToken>());
    }
}
