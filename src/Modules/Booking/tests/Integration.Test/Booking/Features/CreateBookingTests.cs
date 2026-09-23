using System;
using System.Threading;
using System.Threading.Tasks;
using Api;
using Booking.Booking.Exceptions;
using Booking.Booking.ValueObjects;
using Booking.Data;
using BookingFlight;
using BookingPassenger;
using BuildingBlocks.Contracts.EventBus.Messages;
using BuildingBlocks.EventStoreDB.Repository;
using BuildingBlocks.TestBase;
using FluentAssertions;
using Grpc.Core;
using Grpc.Core.Testing;
using Integration.Test.Fakes;
using MassTransit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NSubstitute;
using Xunit;
using BookingAggregate = Booking.Booking.Models.Booking;
using GetByIdRequest = BookingFlight.GetByIdRequest;

namespace Integration.Test.Booking.Features
{
    public class CreateBookingTests : BookingIntegrationTestBase
    {
        public CreateBookingTests(TestReadFixture<Program, BookingReadDbContext> integrationTestFixture)
            : base(integrationTestFixture) { }

        protected override void RegisterTestsServices(IServiceCollection services)
        {
            MockFlightGrpcServices(services);
            MockPassengerGrpcServices(services);
        }

        [Fact]
        public async Task should_create_booking_to_event_store_currectly()
        {
            // Arrange
            var command = new FakeCreateBookingCommand().Generate();

            // Act
            var response = await Fixture.SendAsync(command);

            // Assert
            response?.Id.Should().BeGreaterThanOrEqualTo(0);

            (await Fixture.WaitForPublishing<BookingCreated>()).Should().Be(true);
        }

        [Fact]
        public async Task should_throw_flight_not_found_exception_when_flight_does_not_exist()
        {
            // Arrange
            var command = new FakeCreateBookingCommand().Generate() with
            {
                FlightId = NewId.NextGuid(),
            };

            FlightGrpcMock
                .GetByIdAsync(Arg.Is<GetByIdRequest>(r => r.Id == command.FlightId.ToString()))
                .Returns(AsyncUnaryCall<GetFlightByIdResult>(null));

            // Act
            Func<Task> act = async () =>
            {
                await Fixture.SendAsync(command);
            };

            // Assert
            await act.Should().ThrowAsync<FlightNotFoundException>();

            FlightGrpcMock
                .DidNotReceive()
                .ReserveSeatAsync(Arg.Is<ReserveSeatRequest>(r => r.FlightId == command.FlightId.ToString()));
        }

        [Fact]
        public async Task should_throw_booking_already_exist_exception_when_booking_with_same_id_exists()
        {
            // Arrange
            var command = new FakeCreateBookingCommand().Generate();

            var existingBooking = BookingAggregate.Create(
                command.Id,
                PassengerInfo.Of("Test"),
                Trip.Of(
                    "1500B",
                    NewId.NextGuid(),
                    NewId.NextGuid(),
                    NewId.NextGuid(),
                    DateTime.UtcNow,
                    100,
                    command.Description,
                    "33F"
                )
            );
            existingBooking.Id = command.Id;

            using (var scope = Fixture.ServiceProvider.CreateScope())
            {
                var repository = scope.ServiceProvider.GetRequiredService<IEventStoreDBRepository<BookingAggregate>>();
                await repository.Add(existingBooking, CancellationToken.None);
            }

            // Act
            Func<Task> act = async () =>
            {
                await Fixture.SendAsync(command);
            };

            // Assert
            await act.Should().ThrowAsync<BookingAlreadyExistException>();
        }

        [Fact]
        public async Task should_throw_invalid_passenger_name_exception_when_passenger_name_is_empty()
        {
            // Arrange
            var command = new FakeCreateBookingCommand().Generate() with
            {
                PassengerId = NewId.NextGuid(),
            };

            var passenger = FakePassengerResponse.Generate();
            passenger.PassengerDto.Name = string.Empty;

            PassengerGrpcMock
                .GetByIdAsync(Arg.Is<BookingPassenger.GetByIdRequest>(r => r.Id == command.PassengerId.ToString()))
                .Returns(AsyncUnaryCall(passenger));

            // Act
            Func<Task> act = async () =>
            {
                await Fixture.SendAsync(command);
            };

            // Assert
            await act.Should().ThrowAsync<InvalidPassengerNameException>();
        }

        [Fact]
        public async Task should_throw_invalid_passenger_name_exception_when_passenger_dto_is_missing()
        {
            // Arrange
            var command = new FakeCreateBookingCommand().Generate() with
            {
                PassengerId = NewId.NextGuid(),
            };

            PassengerGrpcMock
                .GetByIdAsync(Arg.Is<BookingPassenger.GetByIdRequest>(r => r.Id == command.PassengerId.ToString()))
                .Returns(AsyncUnaryCall(new GetPassengerByIdResult { PassengerDto = null }));

            // Act
            Func<Task> act = async () =>
            {
                await Fixture.SendAsync(command);
            };

            // Assert
            await act.Should().ThrowAsync<InvalidPassengerNameException>();
        }

        [Fact]
        public async Task should_throw_seat_number_exception_when_flight_has_no_available_seats()
        {
            // Arrange
            var command = new FakeCreateBookingCommand().Generate() with
            {
                FlightId = NewId.NextGuid(),
            };

            var flight = FakeFlightResponse.Generate();
            flight.FlightDto.Id = command.FlightId.ToString();

            FlightGrpcMock
                .GetByIdAsync(Arg.Is<GetByIdRequest>(r => r.Id == command.FlightId.ToString()))
                .Returns(AsyncUnaryCall(flight));

            FlightGrpcMock
                .GetAvailableSeatsAsync(
                    Arg.Is<GetAvailableSeatsRequest>(r => r.FlightId == command.FlightId.ToString())
                )
                .Returns(AsyncUnaryCall(new GetAvailableSeatsResult()));

            // Act
            Func<Task> act = async () =>
            {
                await Fixture.SendAsync(command);
            };

            // Assert
            await act.Should().ThrowAsync<SeatNumberException>();

            FlightGrpcMock
                .DidNotReceive()
                .ReserveSeatAsync(Arg.Is<ReserveSeatRequest>(r => r.FlightId == command.FlightId.ToString()));
        }

        private FlightGrpcService.FlightGrpcServiceClient FlightGrpcMock =>
            Fixture.ServiceProvider.GetRequiredService<FlightGrpcService.FlightGrpcServiceClient>();

        private PassengerGrpcService.PassengerGrpcServiceClient PassengerGrpcMock =>
            Fixture.ServiceProvider.GetRequiredService<PassengerGrpcService.PassengerGrpcServiceClient>();

        private static AsyncUnaryCall<TResponse> AsyncUnaryCall<TResponse>(TResponse response)
        {
            return TestCalls.AsyncUnaryCall(
                Task.FromResult(response),
                Task.FromResult(new Metadata()),
                () => Status.DefaultSuccess,
                () => new Metadata(),
                () => { }
            );
        }

        private void MockPassengerGrpcServices(IServiceCollection services)
        {
            services.Replace(
                ServiceDescriptor.Singleton(x =>
                {
                    var mockPassenger = Substitute.For<PassengerGrpcService.PassengerGrpcServiceClient>();

                    mockPassenger
                        .GetByIdAsync(Arg.Any<BookingPassenger.GetByIdRequest>())
                        .Returns(AsyncUnaryCall(FakePassengerResponse.Generate()));

                    return mockPassenger;
                })
            );
        }

        private void MockFlightGrpcServices(IServiceCollection services)
        {
            services.Replace(
                ServiceDescriptor.Singleton(x =>
                {
                    var mockFlight = Substitute.For<FlightGrpcService.FlightGrpcServiceClient>();

                    mockFlight
                        .GetByIdAsync(Arg.Any<GetByIdRequest>())
                        .Returns(AsyncUnaryCall(FakeFlightResponse.Generate()));

                    mockFlight
                        .GetAvailableSeatsAsync(Arg.Any<GetAvailableSeatsRequest>())
                        .Returns(AsyncUnaryCall(FakeGetAvailableSeatsResponse.Generate()));

                    mockFlight
                        .ReserveSeatAsync(Arg.Any<ReserveSeatRequest>())
                        .Returns(AsyncUnaryCall(FakeReserveSeatResponse.Generate()));

                    return mockFlight;
                })
            );
        }
    }
}
