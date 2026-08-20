using System.Threading.Tasks;
using Flight;
using Passenger;
using BuildingBlocks.Contracts.EventBus.Messages;
using BuildingBlocks.TestBase;
using FluentAssertions;
using Grpc.Core;
using Grpc.Core.Testing;
using BookingService.Integration.Test.Fakes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NSubstitute;
using Xunit;
using BookingReadDbContext = global::Booking.Data.BookingReadDbContext;
using GetByIdRequest = Flight.GetByIdRequest;

namespace BookingService.Integration.Test.Booking.Features
{
    public class CreateBookingTests : BookingServiceIntegrationTestBase
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
        public async Task should_create_booking_through_standalone_booking_service_host()
        {
            // Arrange
            var command = new FakeCreateBookingCommand().Generate();

            // Act
            var response = await Fixture.SendAsync(command);

            // Assert
            response?.Id.Should().BeGreaterThanOrEqualTo(0);

            (await Fixture.WaitForPublishing<BookingCreated>()).Should().Be(true);
        }

        private void MockPassengerGrpcServices(IServiceCollection services)
        {
            services.Replace(ServiceDescriptor.Singleton(x =>
            {
                var mockPassenger = Substitute.For<PassengerGrpcService.PassengerGrpcServiceClient>();

                mockPassenger.GetByIdAsync(Arg.Any<Passenger.GetByIdRequest>())
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
