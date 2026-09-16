using System.Net;
using System.Net.Http.Json;
using Api;
using BuildingBlocks.TestBase;
using EndToEnd.Test.Fakes;
using EndToEnd.Test.Routes;
using FluentAssertions;
using Passenger.Data;
using Passenger.Passengers.Features.GettingPassengerById.V1;
using Xunit;

namespace EndToEnd.Test.Passenger.Features;

public class GetPassengerByIdTests : PassengerEndToEndTestBase
{
    public GetPassengerByIdTests(
        TestFixture<Program, PassengerDbContext, PassengerReadDbContext> integrationTestFixture
    )
        : base(integrationTestFixture) { }

    [Fact]
    public async Task should_retrieve_a_passenger_by_id_through_http()
    {
        // Arrange
        var command = new FakeCompleteRegisterPassengerMongoCommand().Generate();
        await Fixture.SendAsync(command);

        // Act
        var route = ApiRoutes.Passenger.GetPassengerById.Replace(
            ApiRoutes.Passenger.Id,
            command.Id.ToString(),
            StringComparison.Ordinal
        );
        var result = await Fixture.HttpClient.GetAsync(route);

        // Assert
        result.StatusCode.Should().Be(HttpStatusCode.OK);

        var response = await result.Content.ReadFromJsonAsync<GetPassengerByIdResponseDto>();
        response.Should().NotBeNull();
        response!.PassengerDto.Id.Should().Be(command.Id);
        response.PassengerDto.PassportNumber.Should().Be(command.PassportNumber);
    }

    [Fact]
    public async Task should_return_not_found_for_unknown_passenger()
    {
        // Act
        var route = ApiRoutes.Passenger.GetPassengerById.Replace(
            ApiRoutes.Passenger.Id,
            Guid.NewGuid().ToString(),
            StringComparison.Ordinal
        );
        var result = await Fixture.HttpClient.GetAsync(route);

        // Assert
        result.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
