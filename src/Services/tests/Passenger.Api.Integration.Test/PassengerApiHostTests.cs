using System.Threading.Tasks;
using BuildingBlocks.Core;
using BuildingBlocks.TestBase;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Integration.Test;

using global::Passenger;
using global::Passenger.Data;

[Collection(PassengerApiTestCollection.Name)]
public class PassengerApiHostTests : TestBase<Passenger.Api.Program, PassengerDbContext, PassengerReadDbContext>
{
    public PassengerApiHostTests(
        TestFixture<Passenger.Api.Program, PassengerDbContext, PassengerReadDbContext> integrationTestFixture
    )
        : base(integrationTestFixture) { }

    [Fact]
    public async Task should_serve_root_endpoint_with_service_name()
    {
        var response = await Fixture.HttpClient.GetStringAsync("/");

        response.Should().Be("Passenger-Service");
    }

    [Fact]
    public void should_register_passenger_event_mapper_as_the_only_event_mapper()
    {
        using var scope = Fixture.ServiceProvider.CreateScope();

        var eventMappers = scope.ServiceProvider.GetServices<IEventMapper>();

        eventMappers.Should().ContainSingle().Which.Should().BeOfType<PassengerEventMapper>();
    }
}

[CollectionDefinition(Name)]
public class PassengerApiTestCollection
    : ICollectionFixture<TestFixture<Passenger.Api.Program, PassengerDbContext, PassengerReadDbContext>>
{
    public const string Name = "PassengerApi Integration Test";
}
