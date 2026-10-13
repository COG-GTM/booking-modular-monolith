using System;
using System.Threading;
using System.Threading.Tasks;
using BookingFlight;
using BookingPassenger;
using BuildingBlocks.Core;
using BuildingBlocks.Core.Event;
using BuildingBlocks.EventStoreDB.Repository;
using BuildingBlocks.Web;
using FluentAssertions;
using Grpc.Core;
using Grpc.Core.Testing;
using Integration.Test.Fakes;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;
using GetByIdRequest = BookingFlight.GetByIdRequest;

namespace Integration.Test.Booking.Features
{
    using global::Booking.Booking.Exceptions;
    using global::Booking.Booking.Features.CreatingBook.V1;
    using BookingAggregate = global::Booking.Booking.Models.Booking;

    // Handler-level tests: no TestServer / Docker, all collaborators are substituted.
    public class CreateBookingCommandHandlerTests
    {
        private readonly IEventStoreDBRepository<BookingAggregate> _eventStoreDbRepository = Substitute.For<
            IEventStoreDBRepository<BookingAggregate>
        >();

        private readonly ICurrentUserProvider _currentUserProvider = Substitute.For<ICurrentUserProvider>();
        private readonly IEventDispatcher _eventDispatcher = Substitute.For<IEventDispatcher>();

        private readonly FlightGrpcService.FlightGrpcServiceClient _flightGrpcServiceClient =
            Substitute.For<FlightGrpcService.FlightGrpcServiceClient>();

        private readonly PassengerGrpcService.PassengerGrpcServiceClient _passengerGrpcServiceClient =
            Substitute.For<PassengerGrpcService.PassengerGrpcServiceClient>();

        private readonly CreateBookingCommandHandler _handler;

        public CreateBookingCommandHandlerTests()
        {
            _currentUserProvider.GetCurrentUserId().Returns(1);
            _eventStoreDbRepository
                .Find(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
                .Returns(Task.FromResult<BookingAggregate>(null));
            _eventStoreDbRepository
                .Add(Arg.Any<BookingAggregate>(), Arg.Any<CancellationToken>())
                .Returns(Task.FromResult(0UL));

            _flightGrpcServiceClient
                .GetByIdAsync(
                    Arg.Any<GetByIdRequest>(),
                    Arg.Any<Metadata>(),
                    Arg.Any<DateTime?>(),
                    Arg.Any<CancellationToken>()
                )
                .Returns(UnaryCall(FakeFlightResponse.Generate()));
            _flightGrpcServiceClient
                .GetAvailableSeatsAsync(
                    Arg.Any<GetAvailableSeatsRequest>(),
                    Arg.Any<Metadata>(),
                    Arg.Any<DateTime?>(),
                    Arg.Any<CancellationToken>()
                )
                .Returns(UnaryCall(FakeGetAvailableSeatsResponse.Generate()));
            _flightGrpcServiceClient
                .ReserveSeatAsync(
                    Arg.Any<ReserveSeatRequest>(),
                    Arg.Any<Metadata>(),
                    Arg.Any<DateTime?>(),
                    Arg.Any<CancellationToken>()
                )
                .Returns(UnaryCall(FakeReserveSeatResponse.Generate()));

            _passengerGrpcServiceClient
                .GetByIdAsync(
                    Arg.Any<BookingPassenger.GetByIdRequest>(),
                    Arg.Any<Metadata>(),
                    Arg.Any<DateTime?>(),
                    Arg.Any<CancellationToken>()
                )
                .Returns(UnaryCall(FakePassengerResponse.Generate()));

            _handler = new CreateBookingCommandHandler(
                _eventStoreDbRepository,
                _currentUserProvider,
                _eventDispatcher,
                _flightGrpcServiceClient,
                _passengerGrpcServiceClient
            );
        }

        [Fact]
        public async Task handler_should_reserve_the_first_available_seat_before_persisting_the_booking()
        {
            // Arrange
            var command = new FakeCreateBookingCommand().Generate();
            var flight = FakeFlightResponse.Generate().FlightDto;
            var expectedSeat = FakeGetAvailableSeatsResponse.Generate().SeatDtos[0];

            // Act
            var result = await _handler.Handle(command, CancellationToken.None);

            // Assert
            result.Should().NotBeNull();

            _ = _flightGrpcServiceClient
                .Received(1)
                .ReserveSeatAsync(
                    Arg.Is<ReserveSeatRequest>(r => r.FlightId == flight.Id && r.SeatNumber == expectedSeat.SeatNumber),
                    Arg.Any<Metadata>(),
                    Arg.Any<DateTime?>(),
                    Arg.Any<CancellationToken>()
                );

            await _eventStoreDbRepository
                .Received(1)
                .Add(
                    Arg.Is<BookingAggregate>(b => b.Trip.SeatNumber == expectedSeat.SeatNumber),
                    Arg.Any<CancellationToken>()
                );

            Received.InOrder(() =>
            {
                _flightGrpcServiceClient.ReserveSeatAsync(
                    Arg.Any<ReserveSeatRequest>(),
                    Arg.Any<Metadata>(),
                    Arg.Any<DateTime?>(),
                    Arg.Any<CancellationToken>()
                );
                _eventDispatcher.SendAsync(
                    Arg.Any<System.Collections.Generic.IReadOnlyList<IDomainEvent>>(),
                    Arg.Any<Type>(),
                    Arg.Any<CancellationToken>()
                );
                _eventStoreDbRepository.Add(Arg.Any<BookingAggregate>(), Arg.Any<CancellationToken>());
            });
        }

        [Fact]
        public async Task handler_should_throw_no_available_seat_exception_when_flight_has_no_free_seat()
        {
            // Arrange
            var command = new FakeCreateBookingCommand().Generate();
            _flightGrpcServiceClient
                .GetAvailableSeatsAsync(
                    Arg.Any<GetAvailableSeatsRequest>(),
                    Arg.Any<Metadata>(),
                    Arg.Any<DateTime?>(),
                    Arg.Any<CancellationToken>()
                )
                .Returns(UnaryCall(new GetAvailableSeatsResult()));

            // Act
            var act = async () => await _handler.Handle(command, CancellationToken.None);

            // Assert
            await act.Should().ThrowAsync<NoAvailableSeatException>();
            await AssertNothingWasPersisted();
            _ = _flightGrpcServiceClient
                .DidNotReceive()
                .ReserveSeatAsync(
                    Arg.Any<ReserveSeatRequest>(),
                    Arg.Any<Metadata>(),
                    Arg.Any<DateTime?>(),
                    Arg.Any<CancellationToken>()
                );
        }

        [Fact]
        public async Task handler_should_not_persist_booking_when_seat_reservation_fails()
        {
            // Arrange: the Flight module rejects the claim (e.g. a concurrent booking already took the seat)
            var command = new FakeCreateBookingCommand().Generate();
            _flightGrpcServiceClient
                .ReserveSeatAsync(
                    Arg.Any<ReserveSeatRequest>(),
                    Arg.Any<Metadata>(),
                    Arg.Any<DateTime?>(),
                    Arg.Any<CancellationToken>()
                )
                .Throws(new RpcException(new Status(StatusCode.Aborted, "Seat is already reserved!")));

            // Act
            var act = async () => await _handler.Handle(command, CancellationToken.None);

            // Assert
            await act.Should().ThrowAsync<RpcException>();
            await AssertNothingWasPersisted();
        }

        [Fact]
        public async Task handler_should_throw_booking_already_exist_exception_before_touching_the_seat()
        {
            // Arrange
            var command = new FakeCreateBookingCommand().Generate();
            var existing = BookingAggregate.Create(
                command.Id,
                global::Booking.Booking.ValueObjects.PassengerInfo.Of("Test"),
                global::Booking.Booking.ValueObjects.Trip.Of(
                    "1500B",
                    Guid.NewGuid(),
                    Guid.NewGuid(),
                    Guid.NewGuid(),
                    DateTime.Now,
                    100,
                    "desc",
                    "33F"
                )
            );
            _eventStoreDbRepository.Find(command.Id, Arg.Any<CancellationToken>()).Returns(existing);

            // Act
            var act = async () => await _handler.Handle(command, CancellationToken.None);

            // Assert
            await act.Should().ThrowAsync<BookingAlreadyExistException>();
            await AssertNothingWasPersisted();
            _ = _flightGrpcServiceClient
                .DidNotReceive()
                .ReserveSeatAsync(
                    Arg.Any<ReserveSeatRequest>(),
                    Arg.Any<Metadata>(),
                    Arg.Any<DateTime?>(),
                    Arg.Any<CancellationToken>()
                );
        }

        private async Task AssertNothingWasPersisted()
        {
            await _eventStoreDbRepository
                .DidNotReceive()
                .Add(Arg.Any<BookingAggregate>(), Arg.Any<CancellationToken>());
            await _eventDispatcher
                .DidNotReceiveWithAnyArgs()
                .SendAsync(
                    Arg.Any<System.Collections.Generic.IReadOnlyList<IDomainEvent>>(),
                    Arg.Any<Type>(),
                    Arg.Any<CancellationToken>()
                );
        }

        private static AsyncUnaryCall<T> UnaryCall<T>(T response)
        {
            return TestCalls.AsyncUnaryCall(
                Task.FromResult(response),
                Task.FromResult(new Metadata()),
                () => Status.DefaultSuccess,
                () => new Metadata(),
                () => { }
            );
        }
    }
}
