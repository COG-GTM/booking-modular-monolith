using System.Net;
using System.Net.Http.Json;
using BuildingBlocks.TestBase;
using FluentAssertions;
using Integration.Test.Fakes;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Passenger.Data;
using Passenger.Passengers.Features.GettingPassengerById.V1;
using Xunit;

namespace Host.Test.Features;

public class GetPassengerByIdRestHostTests : PassengerHostTestBase
{
    public GetPassengerByIdRestHostTests(
        TestFixture<global::Passenger.Host.Program, PassengerDbContext, PassengerReadDbContext> integrationTestFactory
    )
        : base(integrationTestFactory) { }

    [Fact]
    public async Task should_serve_rest_get_passenger_by_id_from_passenger_host()
    {
        // Arrange
        var command = new FakeCompleteRegisterPassengerMongoCommand().Generate();

        await Fixture.SendAsync(command);

        // Act
        var response = await Fixture.HttpClient.GetAsync($"api/v1/passenger/{command.Id}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<GetPassengerByIdResponseDto>();
        body.Should().NotBeNull();
        body!.PassengerDto.Id.Should().Be(command.Id);
        body.PassengerDto.Name.Should().Be(command.Name);
    }

    [Fact]
    public async Task rest_get_passenger_by_id_should_require_authentication()
    {
        // Arrange — plain client without the fake bearer token
        using var anonymousClient = ((TestServer)Fixture.ServiceProvider.GetRequiredService<IServer>()).CreateClient();

        // Act
        var response = await anonymousClient.GetAsync($"api/v1/passenger/{Guid.NewGuid()}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
