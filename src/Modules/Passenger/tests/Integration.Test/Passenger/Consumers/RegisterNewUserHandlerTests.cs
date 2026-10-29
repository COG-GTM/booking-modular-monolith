using BuildingBlocks.Contracts.EventBus.Messages;
using BuildingBlocks.Core;
using BuildingBlocks.Core.Event;
using BuildingBlocks.Web;
using FluentAssertions;
using Integration.Test.Fakes;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Integration.Test.Passenger.Consumers;

using PassengerConsumer = global::Passenger.Identity.Consumers.RegisteringNewUser.V1;
using PassengerModule = global::Passenger;

public class RegisterNewUserHandlerTests
{
    [Fact]
    public async Task New_user_registers_passenger_with_the_message_user_id_and_dispatches_event()
    {
        var databaseName = Guid.NewGuid().ToString();
        var message = new FakeUserCreated().Generate();
        var eventDispatcher = Substitute.For<IEventDispatcher>();
        await using var context = CreateContext(databaseName);
        var handler = CreateHandler(context, eventDispatcher);
        var consumeContext = CreateConsumeContext(message);

        await handler.Consume(consumeContext);

        var passenger = await context.Passengers.SingleAsync();
        passenger.UserId.Should().Be(message.Id);
        passenger.Name.Value.Should().Be(message.Name);
        passenger.PassportNumber.Value.Should().Be(message.PassportNumber);
        await eventDispatcher
            .Received(1)
            .SendAsync(
                Arg.Is<PassengerConsumer.PassengerCreatedDomainEvent>(domainEvent =>
                    domainEvent.Name == message.Name && domainEvent.PassportNumber == message.PassportNumber
                ),
                typeof(IInternalCommand),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task Existing_passport_does_not_create_passenger_or_dispatch_event()
    {
        var databaseName = Guid.NewGuid().ToString();
        var message = new FakeUserCreated().Generate();
        var existingUserId = Guid.NewGuid();
        var existingPassenger = PassengerModule.Passengers.Models.Passenger.Create(
            PassengerModule.Passengers.ValueObjects.PassengerId.Of(Guid.NewGuid()),
            PassengerModule.Passengers.ValueObjects.Name.Of("Existing Passenger"),
            PassengerModule.Passengers.ValueObjects.PassportNumber.Of(message.PassportNumber),
            existingUserId
        );
        var eventDispatcher = Substitute.For<IEventDispatcher>();
        await using var context = CreateContext(databaseName);
        context.Passengers.Add(existingPassenger);
        await context.SaveChangesAsync();
        var handler = CreateHandler(context, eventDispatcher);
        var consumeContext = CreateConsumeContext(message);

        await handler.Consume(consumeContext);

        (await context.Passengers.CountAsync()).Should().Be(1);
        (await context.Passengers.SingleAsync()).UserId.Should().Be(existingUserId);
        await eventDispatcher
            .DidNotReceive()
            .SendAsync(
                Arg.Any<PassengerConsumer.PassengerCreatedDomainEvent>(),
                Arg.Any<Type>(),
                Arg.Any<CancellationToken>()
            );
    }

    private static PassengerConsumer.RegisterNewUserHandler CreateHandler(
        PassengerModule.Data.PassengerDbContext context,
        IEventDispatcher eventDispatcher
    ) =>
        new(
            context,
            eventDispatcher,
            NullLogger<PassengerConsumer.RegisterNewUserHandler>.Instance,
            Options.Create(new AppOptions { Name = "Passenger" })
        );

    private static ConsumeContext<UserCreated> CreateConsumeContext(UserCreated message)
    {
        var consumeContext = Substitute.For<ConsumeContext<UserCreated>>();
        consumeContext.Message.Returns(message);
        return consumeContext;
    }

    private static PassengerModule.Data.PassengerDbContext CreateContext(string databaseName)
    {
        var options = new DbContextOptionsBuilder<PassengerModule.Data.PassengerDbContext>()
            .UseInMemoryDatabase(databaseName)
            .Options;
        return new PassengerModule.Data.PassengerDbContext(options);
    }
}
