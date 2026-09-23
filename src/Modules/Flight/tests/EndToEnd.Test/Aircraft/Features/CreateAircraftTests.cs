using System.Net;
using System.Net.Http.Json;
using Api;
using BuildingBlocks.TestBase;
using EndToEnd.Test.Fakes;
using EndToEnd.Test.Routes;
using Flight.Data;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace EndToEnd.Test.Aircraft.Features;

using global::Flight.Aircrafts.Features.CreatingAircraft.V1;

public class CreateAircraftTests : FlightEndToEndTestBase
{
    public CreateAircraftTests(TestFixture<Program, FlightDbContext, FlightReadDbContext> integrationTestFixture) : base(integrationTestFixture)
    {
    }


    [Fact]
    public async Task should_create_new_aircraft_and_return_ok_with_created_id()
    {
        //Arrange
        var command = new FakeCreateAircraftCommand().Generate();

        // Act
        var route = ApiRoutes.Aircraft.CreateAircraft;
        var result = await Fixture.HttpClient.PostAsJsonAsync(route, command);

        // Assert
        result.StatusCode.Should().Be(HttpStatusCode.OK);

        var response = await result.Content.ReadFromJsonAsync<CreateAircraftResponseDto>();
        response.Should().NotBeNull();
        response!.Id.Should().NotBeEmpty();

        var entity = await Fixture.ExecuteDbContextAsync(db =>
            db.Aircraft.SingleOrDefaultAsync(a => a.Model.Value == command.Model));

        entity.Should().NotBeNull();
        entity!.Id.Value.Should().Be(response.Id);
        entity.Name.Value.Should().Be(command.Name);
        entity.ManufacturingYear.Value.Should().Be(command.ManufacturingYear);
    }

    [Fact]
    public async Task should_return_unauthorized_when_request_has_no_bearer_token()
    {
        //Arrange
        var command = new FakeCreateAircraftCommand().Generate();
        var httpClient = Fixture.HttpClient;
        httpClient.DefaultRequestHeaders.Authorization = null;

        // Act
        var route = ApiRoutes.Aircraft.CreateAircraft;
        var result = await httpClient.PostAsJsonAsync(route, command);

        // Assert
        result.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var entity = await Fixture.ExecuteDbContextAsync(db =>
            db.Aircraft.SingleOrDefaultAsync(a => a.Model.Value == command.Model));

        entity.Should().BeNull();
    }
}
