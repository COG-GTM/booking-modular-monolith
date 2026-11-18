using System.Net;
using System.Net.Http.Json;
using Api;
using BuildingBlocks.Constants;
using BuildingBlocks.TestBase;
using Flight.Data;
using FluentAssertions;
using Xunit;

namespace EndToEnd.Test.Flight.Features;

public class RemainingAdminAuthorizationTests : FlightEndToEndTestBase
{
    public RemainingAdminAuthorizationTests(TestFixture<Program, FlightDbContext, FlightReadDbContext> fixture)
        : base(fixture) { }

    [Theory]
    [InlineData("PUT", "api/v1.0/flight")]
    [InlineData("POST", "api/v1.0/flight/airport")]
    [InlineData("POST", "api/v1.0/flight/seat")]
    [InlineData("POST", "api/v1.0/flight/reserve-seat")]
    public async Task user_should_be_forbidden_from_admin_endpoint(string method, string route)
    {
        var client = Fixture.CreateHttpClient(IdentityConstant.Role.User, Guid.NewGuid());
        using var request = new HttpRequestMessage(new HttpMethod(method), route)
        {
            Content = JsonContent.Create(new { }),
        };
        var response = await client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
