using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Threading.Tasks;
using Api;
using BuildingBlocks.Constants;
using BuildingBlocks.TestBase;
using Flight.Data;
using FluentAssertions;
using Xunit;

namespace Integration.Test.Flight.Features;

public class FlightManagementAuthorizationTests(TestFixture<Program, FlightDbContext, FlightReadDbContext> fixture)
    : FlightIntegrationTestBase(fixture)
{
    [Theory]
    [InlineData("POST", "api/v1/flight")]
    [InlineData("PUT", "api/v1/flight")]
    [InlineData("POST", "api/v1/flight/aircraft")]
    [InlineData("POST", "api/v1/flight/airport")]
    [InlineData("POST", "api/v1/flight/seat")]
    [InlineData("POST", "api/v1/flight/reserve-seat")]
    public async Task should_require_admin_for_management_routes(string method, string path)
    {
        using var client = Fixture.CreateHttpClient(IdentityConstant.Role.User);
        using var request = new HttpRequestMessage(new HttpMethod(method), path)
        {
            Content = JsonContent.Create(new { }),
        };

        using var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task should_require_api_scope_even_for_an_admin()
    {
        using var client = Fixture.CreateHttpClient(IdentityConstant.Role.Admin);
        client.SetFakeBearerToken(new Dictionary<string, object> { [ClaimTypes.Role] = IdentityConstant.Role.Admin });

        using var response = await client.PostAsJsonAsync("api/v1/flight/aircraft", new { });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
