using System.Net;
using System.Threading.Tasks;
using BuildingBlocks.TestBase;
using FluentAssertions;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Passenger.Data;
using Xunit;

namespace PassengerService.Integration.Test;

[CollectionDefinition(Name)]
public class PassengerServiceHostCollection
    : ICollectionFixture<TestFixture<PassengerService.Program, PassengerDbContext, PassengerReadDbContext>>
{
    public const string Name = "Passenger Service Host Test";
}

[Collection(PassengerServiceHostCollection.Name)]
public class PassengerServiceHostTests
    : TestBase<PassengerService.Program, PassengerDbContext, PassengerReadDbContext>
{
    public PassengerServiceHostTests(
        TestFixture<PassengerService.Program, PassengerDbContext, PassengerReadDbContext> integrationTestFixture)
        : base(integrationTestFixture)
    {
    }

    [Fact]
    public async Task should_start_host_and_serve_http_requests()
    {
        var response = await Fixture.HttpClient.GetAsync("/");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().Be("Passenger-Service");
    }

    [Fact]
    public void should_register_passenger_module_services()
    {
        using var scope = Fixture.ServiceProvider.CreateScope();

        scope.ServiceProvider.GetService<PassengerDbContext>().Should().NotBeNull();
        scope.ServiceProvider.GetService<PassengerReadDbContext>().Should().NotBeNull();
        scope.ServiceProvider.GetService<IMediator>().Should().NotBeNull();
    }

    [Fact]
    public void should_not_register_other_module_services()
    {
        using var scope = Fixture.ServiceProvider.CreateScope();

        scope.ServiceProvider.GetService<Flight.Data.FlightDbContext>().Should().BeNull();
        scope.ServiceProvider.GetService<Identity.Data.IdentityContext>().Should().BeNull();
        scope.ServiceProvider.GetService<Booking.Data.BookingReadDbContext>().Should().BeNull();
    }
}
