using System.Net;
using Api;
using BuildingBlocks.Constants;
using BuildingBlocks.TestBase;
using FluentAssertions;
using Integration.Test.Fakes;
using Passenger.Data;
using Xunit;

namespace Integration.Test.Passenger.Features;

public class GetPassengerAuthorizationTests(TestFixture<Program, PassengerDbContext, PassengerReadDbContext> fixture)
    : PassengerIntegrationTestBase(fixture)
{
    [Theory]
    [InlineData(IdentityConstant.Role.User, HttpStatusCode.Forbidden)]
    [InlineData(IdentityConstant.Role.Admin, HttpStatusCode.OK)]
    public async Task should_require_admin_to_get_a_passenger_by_id(string role, HttpStatusCode expectedStatus)
    {
        var passenger = new FakeCompleteRegisterPassengerMongoCommand().Generate();
        await Fixture.SendAsync(passenger);
        using var client = Fixture.CreateHttpClient(role);

        using var response = await client.GetAsync($"api/v1/passenger/{passenger.Id}");

        response.StatusCode.Should().Be(expectedStatus);
    }
}
