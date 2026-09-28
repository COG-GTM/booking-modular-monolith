using System.Net;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Contract.Test.Gateway;

/// <summary>
/// Boots the real gateway host (<see cref="global::Api.Program"/>) and checks how requests are dispatched at runtime.
/// The route table itself is pinned by <c>GatewayRouteConfigurationTests</c>; this covers what the host actually does
/// with a request: serves its own root, proxies every service/IdentityServer path, and refuses anything else.
/// </summary>
public class GatewayProxyBehaviorTests : IClassFixture<WebApplicationFactory<global::Api.Program>>
{
    private readonly WebApplicationFactory<global::Api.Program> _factory;

    public GatewayProxyBehaviorTests(WebApplicationFactory<global::Api.Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task root_reports_the_gateway_name()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().Be("Booking-Gateway");
    }

    [Theory]
    [InlineData("/api/v1/flight/get-available-flights")]
    [InlineData("/api/v1/passenger/get-passenger-by-id/1")]
    [InlineData("/api/v1/booking/create-booking")]
    [InlineData("/api/v1/identity/register-user")]
    [InlineData("/connect/token")]
    [InlineData("/.well-known/openid-configuration")]
    public async Task module_paths_are_proxied_rather_than_handled_locally(string path)
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync(path);

        // No downstream service is running in this test, so a proxied request surfaces as a gateway error; a 404
        // would mean the path never matched a YARP route and was swallowed by the gateway's own routing.
        response.StatusCode.Should().Be(HttpStatusCode.BadGateway);
    }

    [Theory]
    [InlineData("/api/v1/unknown/thing")]
    [InlineData("/flight/get-available-flights")]
    [InlineData("/swagger")]
    public async Task paths_outside_the_service_prefixes_are_not_proxied(string path)
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync(path);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
