using System.Threading.Tasks;
using Api;
using Booking.Data;
using BookingFlight;
using BookingPassenger;
using System.Linq;
using System.Threading;
using BuildingBlocks.Contracts.EventBus.Messages;
using BuildingBlocks.EventStoreDB.Repository;
using BuildingBlocks.PersistMessageProcessor;
using BuildingBlocks.TestBase;
using FluentAssertions;
using Grpc.Core;
using Grpc.Core.Testing;
using Integration.Test.Fakes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NSubstitute;
using Xunit;
using GetByIdRequest = BookingFlight.GetByIdRequest;

namespace Integration.Test.Booking.Features
{
    public class CreateBookingTests : BookingIntegrationTestBase
    {
        public CreateBookingTests(TestReadFixture<Program, BookingReadDbContext> integrationTestFixture) : base(
            integrationTestFixture)
        {
        }

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
        public async Task should_not_create_booking_when_seat_reservation_fails()
        {
            // Arrange
            var command = new FakeCreateBookingCommand().Generate();
            var flightGrpcClient = Fixture.ServiceProvider.GetRequiredService<FlightGrpcService.FlightGrpcServiceClient>();

            flightGrpcClient.ReserveSeatAsync(Arg.Any<ReserveSeatRequest>())
                .Returns(_ => throw new RpcException(new Status(StatusCode.FailedPrecondition, "Seat is already reserved!")));

            try
            {
                // Act
                var act = async () => { await Fixture.SendAsync(command); };

                // Assert
                await act.Should().ThrowAsync<RpcException>();

                using var scope = Fixture.ServiceProvider.CreateScope();

                var booking = await scope.ServiceProvider
                    .GetRequiredService<IEventStoreDBRepository<global::Booking.Booking.Models.Booking>>()
                    .Find(command.Id, CancellationToken.None);

                booking.Should().BeNull();

                var persistedEvents = await scope.ServiceProvider
                    .GetRequiredService<IPersistMessageDbContext>().PersistMessage
                    .Where(x => x.DataType == typeof(BookingCreated).ToString())
                    .ToListAsync();

                persistedEvents.Should().BeEmpty();
            }
            finally
            {
                flightGrpcClient.ReserveSeatAsync(Arg.Any<ReserveSeatRequest>())
                    .Returns(TestCalls.AsyncUnaryCall(Task.FromResult(FakeReserveSeatResponse.Generate()),
                        Task.FromResult(new Metadata()), () => Status.DefaultSuccess, () => new Metadata(), () => { }));
            }
        }


        private void MockPassengerGrpcServices(IServiceCollection services)
        {
            services.Replace(ServiceDescriptor.Singleton(x =>
            {
                var mockPassenger = Substitute.For<PassengerGrpcService.PassengerGrpcServiceClient>();

                mockPassenger.GetByIdAsync(Arg.Any<BookingPassenger.GetByIdRequest>())
                    .Returns(TestCalls.AsyncUnaryCall(Task.FromResult(FakePassengerResponse.Generate()),
                        Task.FromResult(new Metadata()), () => Status.DefaultSuccess, () => new Metadata(), () => { }));

                return mockPassenger;
            }));
        }

        private void MockFlightGrpcServices(IServiceCollection services)
        {
            services.Replace(ServiceDescriptor.Singleton(x =>
            {
                var mockFlight = Substitute.For<FlightGrpcService.FlightGrpcServiceClient>();

                mockFlight.GetByIdAsync(Arg.Any<GetByIdRequest>())
                    .Returns(TestCalls.AsyncUnaryCall(Task.FromResult(FakeFlightResponse.Generate()),
                        Task.FromResult(new Metadata()), () => Status.DefaultSuccess, () => new Metadata(), () => { }));

                mockFlight.GetAvailableSeatsAsync(Arg.Any<GetAvailableSeatsRequest>())
                    .Returns(TestCalls.AsyncUnaryCall(Task.FromResult(FakeGetAvailableSeatsResponse.Generate()),
                        Task.FromResult(new Metadata()), () => Status.DefaultSuccess, () => new Metadata(), () => { }));

                mockFlight.ReserveSeatAsync(Arg.Any<ReserveSeatRequest>())
                    .Returns(TestCalls.AsyncUnaryCall(Task.FromResult(FakeReserveSeatResponse.Generate()),
                        Task.FromResult(new Metadata()), () => Status.DefaultSuccess, () => new Metadata(), () => { }));

                return mockFlight;
            }));
        }
    }
}