using System.Net;
using System.Threading.Tasks;
using BuildingBlocks.TestBase;
using Flight.Data;
using FluentAssertions;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FlightService.Integration.Test;

[CollectionDefinition(Name)]
public class FlightServiceHostCollection
    : ICollectionFixture<TestFixture<FlightService.Program, FlightDbContext, FlightReadDbContext>>
{
    public const string Name = "Flight Service Host Test";
}

[Collection(FlightServiceHostCollection.Name)]
public class FlightServiceHostTests : TestBase<FlightService.Program, FlightDbContext, FlightReadDbContext>
{
    public FlightServiceHostTests(
        TestFixture<FlightService.Program, FlightDbContext, FlightReadDbContext> integrationTestFixture)
        : base(integrationTestFixture)
    {
    }

    [Fact]
    public async Task should_start_host_and_serve_http_requests()
    {
        var response = await Fixture.HttpClient.GetAsync("/");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().Be("Flight-Service");
    }

    [Fact]
    public async Task should_serve_flight_module_endpoint()
    {
        var response = await Fixture.HttpClient.GetAsync("api/v1.0/flight/get-available-flights");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("Flight not found!");
    }

    [Fact]
    public void should_register_flight_module_services()
    {
        using var scope = Fixture.ServiceProvider.CreateScope();

        scope.ServiceProvider.GetService<FlightDbContext>().Should().NotBeNull();
        scope.ServiceProvider.GetService<FlightReadDbContext>().Should().NotBeNull();
        scope.ServiceProvider.GetService<IMediator>().Should().NotBeNull();
    }

    [Fact]
    public void should_not_register_other_module_services()
    {
        using var scope = Fixture.ServiceProvider.CreateScope();

        scope.ServiceProvider.GetService<Passenger.Data.PassengerDbContext>().Should().BeNull();
        scope.ServiceProvider.GetService<Identity.Data.IdentityContext>().Should().BeNull();
        scope.ServiceProvider.GetService<Booking.Data.BookingReadDbContext>().Should().BeNull();
    }
}
