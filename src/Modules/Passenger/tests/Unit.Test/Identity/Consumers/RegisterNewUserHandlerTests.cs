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
using Passenger;
using Passenger.Identity.Consumers.RegisteringNewUser.V1;
using Passenger.Passengers.Exceptions;
using Passenger.Passengers.ValueObjects;
using Unit.Test.Common;
using Unit.Test.Fakes;
using Xunit;

namespace Unit.Test.Identity.Consumers;

public class RegisterNewUserHandlerTests
{
    [Fact]
    public async Task Consume_persists_a_new_passenger_and_dispatches_its_created_event()
    {
        await using var db = DbContextFactory.Create();
        var dispatcher = Substitute.For<IEventDispatcher<PassengerRoot>>();
        var message = new FakeUserCreated().Generate();
        var context = CreateContext(message);
        var handler = CreateHandler(db, dispatcher);

        await handler.Consume(context);

        var persisted = await db.Passengers.SingleAsync();
        persisted.Name.Should().Be(Name.Of(message.Name));
        persisted.PassportNumber.Should().Be(PassportNumber.Of(message.PassportNumber));
        await dispatcher
            .Received(1)
            .SendAsync(
                Arg.Is<PassengerCreatedDomainEvent>(@event =>
                    @event.Id == persisted.Id
                    && @event.Name == message.Name
                    && @event.PassportNumber == message.PassportNumber
                ),
                typeof(IInternalCommand),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task Consume_does_not_insert_or_dispatch_for_an_existing_passport()
    {
        await using var db = DbContextFactory.Create();
        db.Passengers.Add(
            global::Passenger.Passengers.Models.Passenger.Create(
                PassengerId.Of(Guid.NewGuid()),
                Name.Of("Existing Passenger"),
                PassportNumber.Of("123456789")
            )
        );
        await db.SaveChangesAsync();
        var dispatcher = Substitute.For<IEventDispatcher<PassengerRoot>>();
        var handler = CreateHandler(db, dispatcher);

        await handler.Consume(CreateContext(new UserCreated(Guid.NewGuid(), "Another Name", "123456789")));

        (await db.Passengers.CountAsync()).Should().Be(1);
        await dispatcher
            .DidNotReceiveWithAnyArgs()
            .SendAsync(Arg.Any<IDomainEvent>(), Arg.Any<Type>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Consume_rejects_a_blank_name_without_persisting()
    {
        await using var db = DbContextFactory.Create();
        var dispatcher = Substitute.For<IEventDispatcher<PassengerRoot>>();
        var handler = CreateHandler(db, dispatcher);

        await FluentActions
            .Invoking(() => handler.Consume(CreateContext(new UserCreated(Guid.NewGuid(), "  ", "123456789"))))
            .Should()
            .ThrowAsync<InvalidNameException>();

        (await db.Passengers.CountAsync()).Should().Be(0);
        await dispatcher
            .DidNotReceiveWithAnyArgs()
            .SendAsync(Arg.Any<IDomainEvent>(), Arg.Any<Type>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Consume_rejects_a_null_message()
    {
        await using var db = DbContextFactory.Create();
        var handler = CreateHandler(db, Substitute.For<IEventDispatcher<PassengerRoot>>());
        var context = Substitute.For<ConsumeContext<UserCreated>>();
        context.Message.Returns((UserCreated)null!);

        await FluentActions.Invoking(() => handler.Consume(context)).Should().ThrowAsync<ArgumentNullException>();
    }

    private static RegisterNewUserHandler CreateHandler(
        Passenger.Data.PassengerDbContext db,
        IEventDispatcher<PassengerRoot> dispatcher
    ) =>
        new(
            db,
            dispatcher,
            NullLogger<RegisterNewUserHandler>.Instance,
            Options.Create(new AppOptions { Name = "Unit Test" })
        );

    private static ConsumeContext<UserCreated> CreateContext(UserCreated message)
    {
        var context = Substitute.For<ConsumeContext<UserCreated>>();
        context.Message.Returns(message);
        return context;
    }
}
