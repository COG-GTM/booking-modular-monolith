using BuildingBlocks.Contracts.EventBus.Messages;
using BuildingBlocks.Core;
using BuildingBlocks.Core.Event;
using BuildingBlocks.Web;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Passenger.Data;
using Passenger.Identity.Consumers.RegisteringNewUser.V1;
using Xunit;

namespace Integration.Test;

public class RegisterNewUserOwnershipTests
{
    [Fact]
    public async Task should_persist_and_publish_the_identity_subject_as_the_passenger_id()
    {
        await using var db = CreateContext();
        var dispatcher = Substitute.For<IEventDispatcher>();
        var message = new UserCreated(Guid.NewGuid(), "Test Passenger", "123456789");
        var consumer = CreateConsumer(db, dispatcher);

        await consumer.Consume(CreateMessageContext(message));
        db.ChangeTracker.Clear();

        var passenger = await db.Passengers.SingleAsync();
        Assert.Equal(message.Id, passenger.Id.Value);
        Assert.Equal(message.Name, passenger.Name.Value);
        Assert.Equal(message.PassportNumber, passenger.PassportNumber.Value);
        await dispatcher
            .Received(1)
            .SendAsync(
                Arg.Is<PassengerCreatedDomainEvent>(e =>
                    e.Id == message.Id && e.Name == message.Name && e.PassportNumber == message.PassportNumber
                ),
                typeof(IInternalCommand),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task should_not_reassign_ownership_or_republish_for_an_existing_passport()
    {
        await using var db = CreateContext();
        var dispatcher = Substitute.For<IEventDispatcher>();
        var consumer = CreateConsumer(db, dispatcher);
        var owner = new UserCreated(Guid.NewGuid(), "Test Passenger", "123456789");
        await consumer.Consume(CreateMessageContext(owner));
        dispatcher.ClearReceivedCalls();

        await consumer.Consume(CreateMessageContext(owner with { Id = Guid.NewGuid() }));

        Assert.Equal(owner.Id, (await db.Passengers.SingleAsync()).Id.Value);
        await dispatcher
            .DidNotReceive()
            .SendAsync(Arg.Any<PassengerCreatedDomainEvent>(), Arg.Any<Type>(), Arg.Any<CancellationToken>());
    }

    private static PassengerDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<PassengerDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static RegisterNewUserHandler CreateConsumer(PassengerDbContext db, IEventDispatcher dispatcher) =>
        new(
            db,
            dispatcher,
            NullLogger<RegisterNewUserHandler>.Instance,
            Options.Create(new AppOptions { Name = "Test" })
        );

    private static ConsumeContext<UserCreated> CreateMessageContext(UserCreated message)
    {
        var context = Substitute.For<ConsumeContext<UserCreated>>();
        context.Message.Returns(message);
        return context;
    }
}
