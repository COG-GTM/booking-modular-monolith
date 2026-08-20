using System.Net;
using System.Threading.Tasks;
using Booking.Data;
using BuildingBlocks.TestBase;
using FluentAssertions;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BookingService.Integration.Test;

[CollectionDefinition(Name)]
public class BookingServiceHostCollection
    : ICollectionFixture<TestReadFixture<BookingService.Program, BookingReadDbContext>>
{
    public const string Name = "Booking Service Host Test";
}

[Collection(BookingServiceHostCollection.Name)]
public class BookingServiceHostTests : TestReadBase<BookingService.Program, BookingReadDbContext>
{
    public BookingServiceHostTests(
        TestReadFixture<BookingService.Program, BookingReadDbContext> integrationTestFixture)
        : base(integrationTestFixture)
    {
    }

    [Fact]
    public async Task should_start_host_and_serve_http_requests()
    {
        var response = await Fixture.HttpClient.GetAsync("/");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().Be("Booking-Service");
    }

    [Fact]
    public void should_register_booking_module_services()
    {
        using var scope = Fixture.ServiceProvider.CreateScope();

        scope.ServiceProvider.GetService<BookingReadDbContext>().Should().NotBeNull();
        scope.ServiceProvider.GetService<IMediator>().Should().NotBeNull();
    }

    [Fact]
    public void should_not_register_other_module_services()
    {
        using var scope = Fixture.ServiceProvider.CreateScope();

        scope.ServiceProvider.GetService<Flight.Data.FlightDbContext>().Should().BeNull();
        scope.ServiceProvider.GetService<Passenger.Data.PassengerDbContext>().Should().BeNull();
        scope.ServiceProvider.GetService<Identity.Data.IdentityContext>().Should().BeNull();
    }
}
