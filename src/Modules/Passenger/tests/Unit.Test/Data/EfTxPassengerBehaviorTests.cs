using BuildingBlocks.Core;
using BuildingBlocks.Core.Event;
using BuildingBlocks.PersistMessageProcessor;
using FluentAssertions;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Passenger;
using Passenger.Data;
using Passenger.Passengers.Enums;
using Passenger.Passengers.Features.CompletingRegisterPassenger.V1;
using Passenger.Passengers.ValueObjects;
using Unit.Test.Common;
using Xunit;

namespace Unit.Test.Data;

public class EfTxPassengerBehaviorTests
{
    [Fact]
    public async Task Handle_returns_the_next_response_without_dispatch_when_there_are_no_domain_events()
    {
        await using var db = DbContextFactory.Create();
        var dispatcher = Substitute.For<IEventDispatcher<PassengerRoot>>();
        var persistContext = Substitute.For<IPersistMessageDbContext<PassengerRoot>>();
        var behavior = CreateBehavior(db, persistContext, dispatcher);
        var request = new TestRequest();

        var response = await behavior.Handle(
            request,
            cancellationToken => Task.FromResult("response"),
            CancellationToken.None
        );

        response.Should().Be("response");
        await dispatcher
            .DidNotReceiveWithAnyArgs()
            .SendAsync(Arg.Any<IReadOnlyList<IDomainEvent>>(), Arg.Any<Type>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_dispatches_tracked_events_and_saves_both_contexts()
    {
        await using var db = DbContextFactory.Create();
        var dispatcher = Substitute.For<IEventDispatcher<PassengerRoot>>();
        var persistContext = Substitute.For<IPersistMessageDbContext<PassengerRoot>>();
        persistContext.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult(1));
        var behavior = CreateBehavior(db, persistContext, dispatcher);
        var passenger = global::Passenger.Passengers.Models.Passenger.Create(
            PassengerId.Of(Guid.NewGuid()),
            Name.Of("Sam"),
            PassportNumber.Of("123456789")
        );
        db.Passengers.Add(passenger);
        passenger.CompleteRegistrationPassenger(
            passenger.Id,
            passenger.Name,
            passenger.PassportNumber,
            PassengerType.Male,
            Age.Of(33)
        );

        var response = await behavior.Handle(
            new TestRequest(),
            cancellationToken => Task.FromResult("response"),
            CancellationToken.None
        );

        response.Should().Be("response");
        passenger.DomainEvents.Should().BeEmpty();
        (await db.Passengers.SingleAsync()).Age.Should().Be(Age.Of(33));
        await dispatcher
            .Received(1)
            .SendAsync(
                Arg.Is<IReadOnlyList<IDomainEvent>>(events =>
                    events.Count == 2
                    && events[0]
                        is global::Passenger.Identity.Consumers.RegisteringNewUser.V1.PassengerCreatedDomainEvent
                    && events[1] is PassengerRegistrationCompletedDomainEvent
                ),
                typeof(TestRequest),
                Arg.Any<CancellationToken>()
            );
        await persistContext.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    private static EfTxPassengerBehavior<TestRequest, string> CreateBehavior(
        PassengerDbContext db,
        IPersistMessageDbContext<PassengerRoot> persistContext,
        IEventDispatcher<PassengerRoot> dispatcher
    ) => new(NullLogger<EfTxPassengerBehavior<TestRequest, string>>.Instance, db, persistContext, dispatcher);

    private sealed record TestRequest : IRequest<string>;
}
