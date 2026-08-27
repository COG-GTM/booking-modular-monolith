using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Yarp.ReverseProxy.Configuration;

namespace GatewayService.Unit.Test;

public class GatewayProxyConfigTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly IProxyConfig _proxyConfig;

    public GatewayProxyConfigTests(WebApplicationFactory<Program> factory)
    {
        _proxyConfig = factory.Services.GetRequiredService<IProxyConfigProvider>().GetConfig();
    }

    [Fact]
    public void gateway_should_load_all_routes_from_configuration()
    {
        _proxyConfig.Routes.Select(r => r.RouteId)
            .Should()
            .BeEquivalentTo(
                "flight-api",
                "passenger-api",
                "identity-api",
                "identity-discovery",
                "identity-protocol",
                "booking-api");
    }

    [Fact]
    public void gateway_should_load_all_clusters_from_configuration()
    {
        _proxyConfig.Clusters.Select(c => c.ClusterId)
            .Should()
            .BeEquivalentTo("flight-cluster", "passenger-cluster", "identity-cluster", "booking-cluster");
    }

    [Theory]
    [InlineData("flight-api", "flight-cluster", "/api/{version}/flight/{**catch-all}")]
    [InlineData("passenger-api", "passenger-cluster", "/api/{version}/passenger/{**catch-all}")]
    [InlineData("identity-api", "identity-cluster", "/api/{version}/identity/{**catch-all}")]
    [InlineData("identity-discovery", "identity-cluster", "/.well-known/{**catch-all}")]
    [InlineData("identity-protocol", "identity-cluster", "/connect/{**catch-all}")]
    [InlineData("booking-api", "booking-cluster", "/api/{version}/booking/{**catch-all}")]
    public void gateway_routes_should_map_paths_to_expected_clusters(
        string routeId,
        string expectedClusterId,
        string expectedPath)
    {
        var route = _proxyConfig.Routes.Single(r => r.RouteId == routeId);

        route.ClusterId.Should().Be(expectedClusterId);
        route.Match.Path.Should().Be(expectedPath);
    }

    [Theory]
    [InlineData("flight-cluster", "http://localhost:5101")]
    [InlineData("passenger-cluster", "http://localhost:5102")]
    [InlineData("identity-cluster", "http://localhost:5103")]
    [InlineData("booking-cluster", "http://localhost:5104")]
    public void gateway_clusters_should_have_expected_destinations(string clusterId, string expectedAddress)
    {
        var cluster = _proxyConfig.Clusters.Single(c => c.ClusterId == clusterId);

        cluster.Destinations.Should().HaveCount(1);
        cluster.Destinations!.Values.Single().Address.Should().Be(expectedAddress);
    }
}
