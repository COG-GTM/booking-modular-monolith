using System;
using System.Linq;
using System.Threading.Tasks;
using Api;
using Booking.Data;
using BookingFlight;
using BookingPassenger;
using BuildingBlocks.Web;
using BuildingBlocks.Contracts.EventBus.Messages;
using BuildingBlocks.TestBase;
using FluentAssertions;
using Grpc.Core;
using Grpc.Core.Testing;
using global::Booking.Booking.Exceptions;
using Integration.Test.Fakes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NSubstitute;
using Xunit;
using GetByIdRequest = BookingFlight.GetByIdRequest;

namespace Integration.Test.Booking.Features
{
    public class CreateBookingTests : BookingIntegrationTestBase
    {
        private static readonly Guid OtherUsersPassengerId = new("9d1f2a77-4b6e-4c1a-8f3e-5a2b7c9d0e12");
        private static readonly Guid OtherUserId = new("1c2d3e4f-5a6b-4c7d-8e9f-0a1b2c3d4e5f");

        public CreateBookingTests(TestReadFixture<Program, BookingReadDbContext> integrationTestFixture) : base(
            integrationTestFixture)
        {
        }

        protected override void RegisterTestsServices(IServiceCollection services)
        {
            MockFlightGrpcServices(services);
            MockPassengerGrpcServices(services);
            services.Replace(ServiceDescriptor.Scoped<ICurrentUserProvider, FakeCurrentUserProvider>());
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
        public async Task should_reject_booking_for_passenger_owned_by_another_user_without_reserving_seat()
        {
            // Arrange
            var command = new FakeCreateBookingCommand().Generate() with { PassengerId = OtherUsersPassengerId };
            var flightClient = Fixture.ServiceProvider.GetRequiredService<FlightGrpcService.FlightGrpcServiceClient>();
            var reserveCallsBefore = CountReserveSeatCalls(flightClient);

            // Act
            var act = () => Fixture.SendAsync(command);

            // Assert
            await act.Should().ThrowAsync<PassengerAccessDeniedException>();
            CountReserveSeatCalls(flightClient).Should().Be(reserveCallsBefore);
        }

        private static int CountReserveSeatCalls(FlightGrpcService.FlightGrpcServiceClient flightClient) =>
            flightClient.ReceivedCalls().Count(c => c.GetMethodInfo().Name == nameof(flightClient.ReserveSeatAsync));


        private void MockPassengerGrpcServices(IServiceCollection services)
        {
            services.Replace(ServiceDescriptor.Singleton(x =>
            {
                var mockPassenger = Substitute.For<PassengerGrpcService.PassengerGrpcServiceClient>();

                mockPassenger.GetByIdAsync(Arg.Any<BookingPassenger.GetByIdRequest>())
                    .Returns(TestCalls.AsyncUnaryCall(Task.FromResult(FakePassengerResponse.Generate()),
                        Task.FromResult(new Metadata()), () => Status.DefaultSuccess, () => new Metadata(), () => { }));

                mockPassenger.GetByIdAsync(Arg.Is<BookingPassenger.GetByIdRequest>(r => r.Id == OtherUsersPassengerId.ToString()))
                    .Returns(TestCalls.AsyncUnaryCall(Task.FromResult(FakePassengerResponse.Generate(OtherUserId)),
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