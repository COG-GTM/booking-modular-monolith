using System.Net;
using System.Threading.Tasks;
using BuildingBlocks.TestBase;
using FluentAssertions;
using Identity.Data;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace IdentityService.Integration.Test;

[CollectionDefinition(Name)]
public class IdentityServiceHostCollection
    : ICollectionFixture<TestWriteFixture<IdentityService.Program, IdentityContext>>
{
    public const string Name = "Identity Service Host Test";
}

[Collection(IdentityServiceHostCollection.Name)]
public class IdentityServiceHostTests : TestWriteBase<IdentityService.Program, IdentityContext>
{
    public IdentityServiceHostTests(
        TestWriteFixture<IdentityService.Program, IdentityContext> integrationTestFixture)
        : base(integrationTestFixture)
    {
    }

    [Fact]
    public async Task should_start_host_and_serve_http_requests()
    {
        var response = await Fixture.HttpClient.GetAsync("/");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().Be("Identity-Service");
    }

    [Fact]
    public async Task should_expose_identity_server_discovery_document()
    {
        var response = await Fixture.HttpClient.GetAsync(".well-known/openid-configuration");

        response.IsSuccessStatusCode.Should().BeTrue();
    }

    [Fact]
    public void should_register_identity_module_services()
    {
        using var scope = Fixture.ServiceProvider.CreateScope();

        scope.ServiceProvider.GetService<IdentityContext>().Should().NotBeNull();
        scope.ServiceProvider.GetService<IMediator>().Should().NotBeNull();
    }

    [Fact]
    public void should_not_register_other_module_services()
    {
        using var scope = Fixture.ServiceProvider.CreateScope();

        scope.ServiceProvider.GetService<Flight.Data.FlightDbContext>().Should().BeNull();
        scope.ServiceProvider.GetService<Passenger.Data.PassengerDbContext>().Should().BeNull();
        scope.ServiceProvider.GetService<Booking.Data.BookingReadDbContext>().Should().BeNull();
    }
}
