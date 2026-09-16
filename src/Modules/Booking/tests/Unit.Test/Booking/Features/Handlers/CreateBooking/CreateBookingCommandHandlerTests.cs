namespace Unit.Test.Booking.Features.Handlers.CreateBooking;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BookingFlight;
using BookingPassenger;
using BuildingBlocks.Core;
using BuildingBlocks.Core.Event;
using BuildingBlocks.EventStoreDB.Repository;
using BuildingBlocks.Web;
using FluentAssertions;
using global::Booking.Booking.Exceptions;
using global::Booking.Booking.Features.CreatingBook.V1;
using Grpc.Core;
using Grpc.Core.Testing;
using NSubstitute;
using Unit.Test.Common;
using Unit.Test.Fakes;
using Xunit;
using BookingModel = global::Booking.Booking.Models.Booking;
using CreateBookingCommand = global::Booking.Booking.Features.CreatingBook.V1.CreateBooking;
using GetByIdRequest = BookingFlight.GetByIdRequest;

[Collection(nameof(UnitTestFixture))]
public class CreateBookingCommandHandlerTests
{
    private readonly IEventStoreDBRepository<BookingModel> _repository;
    private readonly IEventDispatcher _eventDispatcher;
    private readonly FlightGrpcService.FlightGrpcServiceClient _flightClient;
    private readonly CreateBookingCommandHandler _handler;

    public CreateBookingCommandHandlerTests()
    {
        _repository = Substitute.For<IEventStoreDBRepository<BookingModel>>();
        _repository.Add(Arg.Any<BookingModel>(), Arg.Any<CancellationToken>()).Returns(1UL);

        var currentUserProvider = Substitute.For<ICurrentUserProvider>();
        currentUserProvider.GetCurrentUserId().Returns(1);

        _eventDispatcher = Substitute.For<IEventDispatcher>();

        _flightClient = Substitute.For<FlightGrpcService.FlightGrpcServiceClient>();
        _flightClient
            .GetByIdAsync(Arg.Any<GetByIdRequest>(), cancellationToken: Arg.Any<CancellationToken>())
            .Returns(AsyncCall(FakeFlightResponse.Generate()));
        _flightClient
            .GetAvailableSeatsAsync(
                Arg.Any<GetAvailableSeatsRequest>(),
                cancellationToken: Arg.Any<CancellationToken>()
            )
            .Returns(AsyncCall(FakeGetAvailableSeatsResponse.Generate()));
        _flightClient
            .ReserveSeatAsync(Arg.Any<ReserveSeatRequest>(), cancellationToken: Arg.Any<CancellationToken>())
            .Returns(AsyncCall(FakeReserveSeatResponse.Generate()));

        var passengerClient = Substitute.For<PassengerGrpcService.PassengerGrpcServiceClient>();
        passengerClient
            .GetByIdAsync(Arg.Any<BookingPassenger.GetByIdRequest>(), cancellationToken: Arg.Any<CancellationToken>())
            .Returns(AsyncCall(FakePassengerResponse.Generate()));

        _handler = new CreateBookingCommandHandler(
            _repository,
            currentUserProvider,
            _eventDispatcher,
            _flightClient,
            passengerClient
        );
    }

    private Task<CreateBookingResult> Act(CreateBookingCommand command, CancellationToken cancellationToken) =>
        _handler.Handle(command, cancellationToken);

    [Fact]
    public async Task handler_with_valid_command_should_create_booking_and_return_stream_revision()
    {
        // Arrange
        var command = new FakeCreateBookingCommand().Generate();

        // Act
        var response = await Act(command, CancellationToken.None);

        // Assert
        response.Should().NotBeNull();
        response.Id.Should().Be(1UL);

        _repository
            .Received(1)
            .Add(
                Arg.Is<BookingModel>(b =>
                    b.Id == command.Id && b.Trip.SeatNumber == "33F" && b.PassengerInfo.Name == "Test"
                ),
                Arg.Any<CancellationToken>()
            );

        _eventDispatcher
            .Received(1)
            .SendAsync(
                Arg.Is<IReadOnlyList<IDomainEvent>>(events =>
                    events.Count == 1 && events[0] is BookingCreatedDomainEvent
                ),
                Arg.Any<Type>(),
                Arg.Any<CancellationToken>()
            );

        _flightClient
            .Received(1)
            .ReserveSeatAsync(
                Arg.Is<ReserveSeatRequest>(r => r.SeatNumber == "33F"),
                cancellationToken: Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task handler_with_existing_booking_should_throw_booking_already_exist_exception()
    {
        // Arrange
        var command = new FakeCreateBookingCommand().Generate();
        _repository.Find(command.Id, Arg.Any<CancellationToken>()).Returns(FakeBookingCreate.Generate());

        // Act
        Func<Task> act = async () =>
        {
            await Act(command, CancellationToken.None);
        };

        // Assert
        await act.Should().ThrowAsync<BookingAlreadyExistException>();
        _repository.DidNotReceive().Add(Arg.Any<BookingModel>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task handler_with_null_command_should_throw_argument_exception()
    {
        // Arrange
        CreateBookingCommand command = null;

        // Act
        Func<Task> act = async () =>
        {
            await Act(command, CancellationToken.None);
        };

        // Assert
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    private static AsyncUnaryCall<T> AsyncCall<T>(T response) =>
        TestCalls.AsyncUnaryCall(
            Task.FromResult(response),
            Task.FromResult(new Metadata()),
            () => Status.DefaultSuccess,
            () => new Metadata(),
            () => { }
        );
}
