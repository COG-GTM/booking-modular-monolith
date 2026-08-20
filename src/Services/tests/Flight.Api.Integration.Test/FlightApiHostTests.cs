using System.Threading.Tasks;
using BuildingBlocks.Core;
using BuildingBlocks.TestBase;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Integration.Test;

using global::Flight;
using global::Flight.Data;

[Collection(FlightApiTestCollection.Name)]
public class FlightApiHostTests : TestBase<Flight.Api.Program, FlightDbContext, FlightReadDbContext>
{
    public FlightApiHostTests(
        TestFixture<Flight.Api.Program, FlightDbContext, FlightReadDbContext> integrationTestFixture
    )
        : base(integrationTestFixture) { }

    [Fact]
    public async Task should_serve_root_endpoint_with_service_name()
    {
        var response = await Fixture.HttpClient.GetStringAsync("/");

        response.Should().Be("Flight-Service");
    }

    [Fact]
    public void should_register_flight_event_mapper_as_the_only_event_mapper()
    {
        using var scope = Fixture.ServiceProvider.CreateScope();

        var eventMappers = scope.ServiceProvider.GetServices<IEventMapper>();

        eventMappers.Should().ContainSingle().Which.Should().BeOfType<FlightEventMapper>();
    }
}

[CollectionDefinition(Name)]
public class FlightApiTestCollection
    : ICollectionFixture<TestFixture<Flight.Api.Program, FlightDbContext, FlightReadDbContext>>
{
    public const string Name = "FlightApi Integration Test";
}
