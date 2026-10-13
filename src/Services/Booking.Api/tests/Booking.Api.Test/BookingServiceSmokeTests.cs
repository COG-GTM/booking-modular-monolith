using System.Net;
using System.Threading.Tasks;
using Booking.Data;
using BuildingBlocks.Contracts.Grpc.Flight;
using BuildingBlocks.Contracts.Grpc.Passenger;
using BuildingBlocks.Core;
using BuildingBlocks.TestBase;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Booking.Api.Test;

public class BookingServiceSmokeTests : BookingApiTestBase
{
    public BookingServiceSmokeTests(TestReadFixture<Program, BookingReadDbContext> integrationTestFixture)
        : base(integrationTestFixture) { }

    [Fact]
    public async Task root_should_return_the_service_name()
    {
        var response = await Fixture.HttpClient.GetAsync("/");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().Be("Booking-Service");
    }

    [Fact]
    public void host_should_resolve_grpc_clients_for_the_flight_and_passenger_services()
    {
        using var scope = Fixture.ServiceProvider.CreateScope();

        scope.ServiceProvider.GetRequiredService<FlightGrpcService.FlightGrpcServiceClient>().Should().NotBeNull();
        scope
            .ServiceProvider.GetRequiredService<PassengerGrpcService.PassengerGrpcServiceClient>()
            .Should()
            .NotBeNull();
    }

    [Fact]
    public void host_should_only_wire_the_booking_event_mapper()
    {
        using var scope = Fixture.ServiceProvider.CreateScope();

        scope.ServiceProvider.GetRequiredService<IEventMapper>().Should().BeOfType<BookingEventMapper>();
    }
}
