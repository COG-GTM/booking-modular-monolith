using System.Threading.Tasks;
using Api;
using Booking.Data;
using BookingFlight;
using BookingPassenger;
using BuildingBlocks.Constants;
using BuildingBlocks.Contracts.EventBus.Messages;
using BuildingBlocks.TestBase;
using BuildingBlocks.Web;
using Booking.Booking.Exceptions;
using FluentAssertions;
using Grpc.Core;
using Grpc.Core.Testing;
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
        public CreateBookingTests(TestReadFixture<Program, BookingReadDbContext> integrationTestFixture) : base(
            integrationTestFixture)
        {
        }

        protected override void RegisterTestsServices(IServiceCollection services)
        {
            services.Replace(ServiceDescriptor.Singleton<ICurrentUserProvider>(_ => Substitute.For<ICurrentUserProvider>()));
            MockFlightGrpcServices(services);
            MockPassengerGrpcServices(services);
        }

        [Fact]
        public async Task should_create_booking_to_event_store_currectly()
        {
            // Arrange
            var command = new FakeCreateBookingCommand().Generate() with { Id = Guid.NewGuid() };
            var currentUserProvider = Fixture.ServiceProvider.GetRequiredService<ICurrentUserProvider>();
            currentUserProvider.GetCurrentUserGuid().Returns(command.PassengerId);
            currentUserProvider.IsInRole(IdentityConstant.Role.Admin).Returns(false);

            // Act
            var response = await Fixture.SendAsync(command);

            // Assert
            response?.Id.Should().BeGreaterThanOrEqualTo(0);

            (await Fixture.WaitForPublishing<BookingCreated>()).Should().Be(true);
        }

        [Fact]
        public async Task should_throw_forbidden_when_booking_for_another_passenger()
        {
            var command = new FakeCreateBookingCommand().Generate();
            var currentUserProvider = Fixture.ServiceProvider.GetRequiredService<ICurrentUserProvider>();
            currentUserProvider.GetCurrentUserGuid().Returns(Guid.NewGuid());
            currentUserProvider.IsInRole(IdentityConstant.Role.Admin).Returns(false);

            var act = () => Fixture.SendAsync(command);

            await act.Should().ThrowAsync<PassengerBookingForbiddenException>();
        }

        [Fact]
        public async Task should_throw_forbidden_when_user_is_unauthenticated()
        {
            var command = new FakeCreateBookingCommand().Generate();
            var currentUserProvider = Fixture.ServiceProvider.GetRequiredService<ICurrentUserProvider>();
            currentUserProvider.GetCurrentUserGuid().Returns((Guid?)null);
            currentUserProvider.IsInRole(IdentityConstant.Role.Admin).Returns(false);

            var act = () => Fixture.SendAsync(command);

            await act.Should().ThrowAsync<PassengerBookingForbiddenException>();
        }

        [Fact]
        public async Task should_allow_admin_to_create_booking_for_another_passenger()
        {
            var command = new FakeCreateBookingCommand().Generate() with { Id = Guid.NewGuid() };
            var currentUserProvider = Fixture.ServiceProvider.GetRequiredService<ICurrentUserProvider>();
            currentUserProvider.GetCurrentUserGuid().Returns(Guid.NewGuid());
            currentUserProvider.IsInRole(IdentityConstant.Role.Admin).Returns(true);

            var response = await Fixture.SendAsync(command);

            response?.Id.Should().BeGreaterThanOrEqualTo(0);
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