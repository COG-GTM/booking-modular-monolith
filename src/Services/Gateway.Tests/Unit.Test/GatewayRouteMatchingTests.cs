using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Unit.Test;

public class GatewayRouteMatchingTests : IClassFixture<WebApplicationFactory<GatewayService.Program>>
{
    private readonly HttpClient _client;

    public GatewayRouteMatchingTests(WebApplicationFactory<GatewayService.Program> factory)
    {
        _client = factory.CreateClient();
    }

    [Theory]
    [InlineData("/api/v1/flight/get-available-flights")]
    [InlineData("/api/v1/passenger/get-passenger-by-id/1")]
    [InlineData("/api/v1/identity/register-user")]
    [InlineData("/api/v1/booking")]
    [InlineData("/connect/token")]
    [InlineData("/.well-known/openid-configuration")]
    public async Task should_forward_matched_paths_to_a_cluster(string path)
    {
        var response = await _client.GetAsync(new Uri(path, UriKind.Relative));

        // No destination is listening in tests, so a matched route surfaces as a
        // proxy error rather than a routing miss.
        response.StatusCode.Should().Be(HttpStatusCode.BadGateway);
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/api/v1/unknown")]
    [InlineData("/flight/get-available-flights")]
    [InlineData("/api/flight")]
    public async Task should_return_not_found_for_unmatched_paths(string path)
    {
        var response = await _client.GetAsync(new Uri(path, UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
