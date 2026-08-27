using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Yarp.ReverseProxy.Configuration;

namespace GatewayService.Unit.Test;

public class GatewayRoutingConfigurationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public GatewayRoutingConfigurationTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public void proxy_config_should_load_routes_from_reverse_proxy_section()
    {
        var config = GetProxyConfig();

        config.Routes.Select(r => r.RouteId)
            .Should().BeEquivalentTo(
                "flight-api",
                "passenger-api",
                "identity-api",
                "identity-discovery",
                "identity-protocol",
                "booking-api");
    }

    [Fact]
    public void proxy_config_should_load_clusters_from_reverse_proxy_section()
    {
        var config = GetProxyConfig();

        config.Clusters.Select(c => c.ClusterId)
            .Should().BeEquivalentTo(
                "flight-cluster",
                "passenger-cluster",
                "identity-cluster",
                "booking-cluster");
    }

    [Theory]
    [InlineData("flight-api", "flight-cluster")]
    [InlineData("passenger-api", "passenger-cluster")]
    [InlineData("identity-api", "identity-cluster")]
    [InlineData("identity-discovery", "identity-cluster")]
    [InlineData("identity-protocol", "identity-cluster")]
    [InlineData("booking-api", "booking-cluster")]
    public void each_route_should_reference_an_existing_cluster(string routeId, string expectedClusterId)
    {
        var config = GetProxyConfig();

        var route = config.Routes.Single(r => r.RouteId == routeId);

        route.ClusterId.Should().Be(expectedClusterId);
        config.Clusters.Should().Contain(c => c.ClusterId == expectedClusterId);
    }

    [Theory]
    [InlineData("flight-api", "/api/{version}/flight/{**catch-all}")]
    [InlineData("passenger-api", "/api/{version}/passenger/{**catch-all}")]
    [InlineData("identity-api", "/api/{version}/identity/{**catch-all}")]
    [InlineData("identity-discovery", "/.well-known/{**catch-all}")]
    [InlineData("identity-protocol", "/connect/{**catch-all}")]
    [InlineData("booking-api", "/api/{version}/booking/{**catch-all}")]
    public void each_route_should_match_the_expected_path_pattern(string routeId, string expectedPath)
    {
        var config = GetProxyConfig();

        var route = config.Routes.Single(r => r.RouteId == routeId);

        route.Match.Path.Should().Be(expectedPath);
    }

    [Fact]
    public void each_cluster_should_have_a_single_destination_with_an_absolute_address()
    {
        var config = GetProxyConfig();

        foreach (var cluster in config.Clusters)
        {
            cluster.Destinations.Should().HaveCount(1);
            var destination = cluster.Destinations!.Values.Single();
            Uri.IsWellFormedUriString(destination.Address, UriKind.Absolute)
                .Should().BeTrue($"cluster '{cluster.ClusterId}' should have an absolute destination address");
        }
    }

    [Fact]
    public async Task request_to_unmapped_path_should_return_not_found()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/v1/unknown/anything");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Theory]
    [InlineData("/api/v1/flight/get-available-flights")]
    [InlineData("/api/v1/passenger/get-passenger-by-id")]
    [InlineData("/api/v1/identity/login")]
    [InlineData("/api/v1/booking/get-bookings")]
    [InlineData("/.well-known/openid-configuration")]
    [InlineData("/connect/token")]
    public async Task request_to_mapped_path_should_be_proxied_to_the_backing_cluster(string path)
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync(path);

        // The destination services are not running in this test, so YARP surfaces a
        // proxy error instead of the gateway's own 404 — proving the route matched.
        response.StatusCode.Should().BeOneOf(
            HttpStatusCode.BadGateway,
            HttpStatusCode.ServiceUnavailable,
            HttpStatusCode.GatewayTimeout);
    }

    private IProxyConfig GetProxyConfig()
    {
        var provider = _factory.Services.GetRequiredService<IProxyConfigProvider>();
        return provider.GetConfig();
    }
}
