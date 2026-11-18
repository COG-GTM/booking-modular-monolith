using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using Api;
using BuildingBlocks.Constants;
using BuildingBlocks.TestBase;
using Flight.Data;
using FluentAssertions;
using Xunit;

namespace EndToEnd.Test.Flight.Features;

public class FlightManagementAuthorizationTests(TestFixture<Program, FlightDbContext, FlightReadDbContext> fixture)
    : FlightEndToEndTestBase(fixture)
{
    [Theory]
    [InlineData("PUT", "api/v1.0/flight")]
    [InlineData("POST", "api/v1.0/flight/aircraft")]
    [InlineData("POST", "api/v1.0/flight/airport")]
    [InlineData("POST", "api/v1.0/flight/seat")]
    public async Task should_forbid_users_with_the_api_scope_from_managing_flights(string method, string route)
    {
        using var client = Fixture.CreateHttpClient(IdentityConstant.Role.User);
        using var request = new HttpRequestMessage(new HttpMethod(method), route)
        {
            Content = JsonContent.Create(new { }),
        };

        using var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task should_forbid_admins_without_the_api_scope_from_creating_an_aircraft()
    {
        using var client = Fixture.CreateHttpClient(IdentityConstant.Role.Admin);
        client.SetFakeBearerToken(new Dictionary<string, object> { [ClaimTypes.Role] = IdentityConstant.Role.Admin });

        using var response = await client.PostAsJsonAsync("api/v1.0/flight/aircraft", new { });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
