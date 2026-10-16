using Booking;
using Booking.Booking.Exceptions;
using Booking.Booking.Features.CreatingBook.V1;
using BuildingBlocks.Core;
using BuildingBlocks.Core.Event;
using BuildingBlocks.EventStoreDB.Repository;
using BuildingBlocks.Web;
using Contracts.Grpc.Flight;
using Contracts.Grpc.Passenger;
using FluentAssertions;
using Grpc.Core;
using NSubstitute;
using Unit.Test.Fakes;
using Xunit;
using BookingAggregate = global::Booking.Booking.Models.Booking;

namespace Unit.Test.Features.CreatingBook.V1;

public class CreateBookingCommandHandlerTests
{
    [Fact]
    public async Task handle_creates_booking_dispatches_reserves_and_persists_in_order()
    {
        var fixture = CreateFixture();
        var command = CreateCommand();
        using var cancellation = new CancellationTokenSource();

        var result = await fixture.Handler.Handle(command, cancellation.Token);

        result.Should().Be(new CreateBookingResult(5UL));
        await fixture
            .Repository.Received(1)
            .Add(
                Arg.Is<BookingAggregate>(booking =>
                    booking.Id == command.Id
                    && booking.PassengerInfo.Name == "Jane Doe"
                    && booking.Trip.FlightNumber == "BA123"
                    && booking.Trip.AircraftId == BookingGrpcFakes.AircraftId
                    && booking.Trip.DepartureAirportId == BookingGrpcFakes.DepartureAirportId
                    && booking.Trip.ArriveAirportId == BookingGrpcFakes.ArriveAirportId
                    && booking.Trip.FlightDate == BookingGrpcFakes.FlightDate
                    && booking.Trip.Price == 199.99m
                    && booking.Trip.Description == command.Description
                    && booking.Trip.SeatNumber == "12A"
                ),
                cancellation.Token
            );

        var dispatchedEvents = fixture
            .Dispatcher.ReceivedCalls()
            .Single(call => call.GetMethodInfo().Name == nameof(IEventDispatcher.SendAsync))
            .GetArguments()[0]
            .Should()
            .BeAssignableTo<IReadOnlyList<IDomainEvent>>()
            .Subject;
        dispatchedEvents
            .Should()
            .ContainSingle()
            .Which.Should()
            .BeOfType<BookingCreatedDomainEvent>()
            .Which.CreatedBy.Should()
            .Be(42L);

        fixture
            .FlightClient.Received(1)
            .ReserveSeatAsync(
                Arg.Is<ReserveSeatRequest>(request =>
                    request.FlightId == BookingGrpcFakes.FlightId.ToString() && request.SeatNumber == "12A"
                ),
                Arg.Any<Metadata>(),
                Arg.Any<DateTime?>(),
                cancellation.Token
            );

        fixture
            .FlightClient.Received(1)
            .GetByIdAsync(
                Arg.Is<Contracts.Grpc.Flight.GetByIdRequest>(request => request.Id == command.FlightId.ToString()),
                Arg.Any<Metadata>(),
                Arg.Any<DateTime?>(),
                cancellation.Token
            );
        fixture
            .PassengerClient.Received(1)
            .GetByIdAsync(
                Arg.Is<Contracts.Grpc.Passenger.GetByIdRequest>(request =>
                    request.Id == command.PassengerId.ToString()
                ),
                Arg.Any<Metadata>(),
                Arg.Any<DateTime?>(),
                cancellation.Token
            );

        Received.InOrder(() =>
        {
            fixture.Dispatcher.SendAsync<IDomainEvent>(
                Arg.Is<IReadOnlyList<IDomainEvent>>(events => events.Count == 1),
                Arg.Any<Type>(),
                cancellation.Token
            );
            fixture.FlightClient.ReserveSeatAsync(
                Arg.Any<ReserveSeatRequest>(),
                Arg.Any<Metadata>(),
                Arg.Any<DateTime?>(),
                cancellation.Token
            );
            fixture.Repository.Add(Arg.Any<BookingAggregate>(), cancellation.Token);
        });
    }

    [Fact]
    public async Task handle_throws_when_flight_response_is_null_without_calling_other_dependencies()
    {
        var fixture = CreateFixture(nullFlight: true);

        await FluentActions
            .Invoking(() => fixture.Handler.Handle(CreateCommand(), CancellationToken.None))
            .Should()
            .ThrowAsync<FlightNotFoundException>();

        fixture
            .PassengerClient.DidNotReceive()
            .GetByIdAsync(
                Arg.Any<Contracts.Grpc.Passenger.GetByIdRequest>(),
                Arg.Any<Metadata>(),
                Arg.Any<DateTime?>(),
                Arg.Any<CancellationToken>()
            );
        fixture
            .FlightClient.DidNotReceive()
            .GetAvailableSeatsAsync(
                Arg.Any<GetAvailableSeatsRequest>(),
                Arg.Any<Metadata>(),
                Arg.Any<DateTime?>(),
                Arg.Any<CancellationToken>()
            );
        fixture.Repository.DidNotReceive().Find(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        fixture
            .Dispatcher.DidNotReceiveWithAnyArgs()
            .SendAsync<IDomainEvent>(default(IReadOnlyList<IDomainEvent>)!, default, default);
    }

    [Fact]
    public async Task handle_rejects_existing_non_deleted_booking()
    {
        var fixture = CreateFixture();
        fixture
            .Repository.Find(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(
                BookingAggregate.Create(
                    Guid.NewGuid(),
                    global::Booking.Booking.ValueObjects.PassengerInfo.Of("Existing"),
                    CreateTrip()
                )
            );

        await FluentActions
            .Invoking(() => fixture.Handler.Handle(CreateCommand(), CancellationToken.None))
            .Should()
            .ThrowAsync<BookingAlreadyExistException>();

        AssertNoReservationOrPersistence(fixture);
    }

    [Fact]
    public async Task handle_reuses_deleted_booking_id()
    {
        var fixture = CreateFixture();
        fixture
            .Repository.Find(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(
                BookingAggregate.Create(
                    Guid.NewGuid(),
                    global::Booking.Booking.ValueObjects.PassengerInfo.Of("Deleted"),
                    CreateTrip(),
                    isDeleted: true
                )
            );

        var command = CreateCommand();

        await fixture.Handler.Handle(command, CancellationToken.None);

        await fixture
            .Repository.Received(1)
            .Add(Arg.Is<BookingAggregate>(booking => booking.Id == command.Id), CancellationToken.None);
    }

    [Fact]
    public async Task handle_rejects_booking_when_no_seats_are_available()
    {
        var fixture = CreateFixture(seats: BookingGrpcFakes.AvailableSeats());

        await FluentActions
            .Invoking(() => fixture.Handler.Handle(CreateCommand(), CancellationToken.None))
            .Should()
            .ThrowAsync<SeatNumberException>();

        AssertNoReservationOrPersistence(fixture);
    }

    [Fact]
    public async Task handle_rejects_passenger_response_without_passenger_details()
    {
        var fixture = CreateFixture(passenger: new GetPassengerByIdResult());

        await FluentActions
            .Invoking(() => fixture.Handler.Handle(CreateCommand(), CancellationToken.None))
            .Should()
            .ThrowAsync<InvalidPassengerNameException>();

        fixture
            .Dispatcher.DidNotReceiveWithAnyArgs()
            .SendAsync<IDomainEvent>(default(IReadOnlyList<IDomainEvent>)!, default, default);
        fixture
            .FlightClient.DidNotReceive()
            .ReserveSeatAsync(
                Arg.Any<ReserveSeatRequest>(),
                Arg.Any<Metadata>(),
                Arg.Any<DateTime?>(),
                Arg.Any<CancellationToken>()
            );
        fixture.Repository.DidNotReceive().Add(Arg.Any<BookingAggregate>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task handle_rejects_null_command()
    {
        var fixture = CreateFixture();

        await FluentActions
            .Invoking(() => fixture.Handler.Handle(null!, CancellationToken.None))
            .Should()
            .ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task handle_passes_cancellation_token_to_repository_find_and_add()
    {
        var fixture = CreateFixture();
        using var cancellation = new CancellationTokenSource();

        await fixture.Handler.Handle(CreateCommand(), cancellation.Token);

        await fixture.Repository.Received(1).Find(Arg.Any<Guid>(), cancellation.Token);
        await fixture.Repository.Received(1).Add(Arg.Any<BookingAggregate>(), cancellation.Token);
    }

    private static void AssertNoReservationOrPersistence(Fixture fixture)
    {
        fixture
            .Dispatcher.DidNotReceiveWithAnyArgs()
            .SendAsync<IDomainEvent>(default(IReadOnlyList<IDomainEvent>)!, default, default);
        fixture
            .FlightClient.DidNotReceive()
            .ReserveSeatAsync(
                Arg.Any<ReserveSeatRequest>(),
                Arg.Any<Metadata>(),
                Arg.Any<DateTime?>(),
                Arg.Any<CancellationToken>()
            );
        fixture.Repository.DidNotReceive().Add(Arg.Any<BookingAggregate>(), Arg.Any<CancellationToken>());
    }

    private static CreateBooking CreateCommand() =>
        new(Guid.NewGuid(), Guid.NewGuid(), "Window seat") { Id = Guid.NewGuid() };

    private static global::Booking.Booking.ValueObjects.Trip CreateTrip() =>
        global::Booking.Booking.ValueObjects.Trip.Of(
            "BA123",
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            BookingGrpcFakes.FlightDate,
            199.99m,
            "Existing",
            "12A"
        );

    private static Fixture CreateFixture(
        GetFlightByIdResult? flight = null,
        GetPassengerByIdResult? passenger = null,
        GetAvailableSeatsResult? seats = null,
        bool nullFlight = false
    )
    {
        var repository = Substitute.For<IEventStoreDBRepository<BookingAggregate>>();
        var currentUser = Substitute.For<ICurrentUserProvider>();
        var dispatcher = Substitute.For<IEventDispatcher<BookingRoot>>();
        var flightClient = Substitute.For<FlightGrpcService.FlightGrpcServiceClient>();
        var passengerClient = Substitute.For<PassengerGrpcService.PassengerGrpcServiceClient>();

        currentUser.GetCurrentUserId().Returns(42L);
        repository.Find(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((BookingAggregate)null);
        repository.Add(Arg.Any<BookingAggregate>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult(5UL));
        dispatcher
            .SendAsync<IDomainEvent>(
                Arg.Any<IReadOnlyList<IDomainEvent>>(),
                Arg.Any<Type>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Task.CompletedTask);

        flightClient
            .GetByIdAsync(
                Arg.Any<Contracts.Grpc.Flight.GetByIdRequest>(),
                Arg.Any<Metadata>(),
                Arg.Any<DateTime?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(_ => GrpcCallFactory.Create(nullFlight ? null : flight ?? BookingGrpcFakes.Flight()));
        passengerClient
            .GetByIdAsync(
                Arg.Any<Contracts.Grpc.Passenger.GetByIdRequest>(),
                Arg.Any<Metadata>(),
                Arg.Any<DateTime?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(_ => GrpcCallFactory.Create(passenger ?? BookingGrpcFakes.Passenger()));
        flightClient
            .GetAvailableSeatsAsync(
                Arg.Any<GetAvailableSeatsRequest>(),
                Arg.Any<Metadata>(),
                Arg.Any<DateTime?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(_ => GrpcCallFactory.Create(seats ?? BookingGrpcFakes.AvailableSeats("12A", "14C")));
        flightClient
            .ReserveSeatAsync(
                Arg.Any<ReserveSeatRequest>(),
                Arg.Any<Metadata>(),
                Arg.Any<DateTime?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(_ => GrpcCallFactory.Create(new ReserveSeatResult { Id = Guid.NewGuid().ToString() }));

        var handler = new CreateBookingCommandHandler(
            repository,
            currentUser,
            dispatcher,
            flightClient,
            passengerClient
        );
        return new Fixture(repository, currentUser, dispatcher, flightClient, passengerClient, handler);
    }

    private sealed record Fixture(
        IEventStoreDBRepository<BookingAggregate> Repository,
        ICurrentUserProvider CurrentUser,
        IEventDispatcher<BookingRoot> Dispatcher,
        FlightGrpcService.FlightGrpcServiceClient FlightClient,
        PassengerGrpcService.PassengerGrpcServiceClient PassengerClient,
        CreateBookingCommandHandler Handler
    );
}
