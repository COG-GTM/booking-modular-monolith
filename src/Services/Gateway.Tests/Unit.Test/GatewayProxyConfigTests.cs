using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Yarp.ReverseProxy.Configuration;

namespace Unit.Test;

public class GatewayProxyConfigTests : IClassFixture<WebApplicationFactory<GatewayService.Program>>
{
    private readonly IProxyConfig _proxyConfig;

    public GatewayProxyConfigTests(WebApplicationFactory<GatewayService.Program> factory)
    {
        _proxyConfig = factory.Services.GetRequiredService<IProxyConfigProvider>().GetConfig();
    }

    [Theory]
    [InlineData("flight-route", "flight-cluster", "/api/{version}/flight/{**catch-all}")]
    [InlineData("passenger-route", "passenger-cluster", "/api/{version}/passenger/{**catch-all}")]
    [InlineData("identity-route", "identity-cluster", "/api/{version}/identity/{**catch-all}")]
    [InlineData("identity-connect-route", "identity-cluster", "/connect/{**catch-all}")]
    [InlineData("identity-wellknown-route", "identity-cluster", "/.well-known/{**catch-all}")]
    [InlineData("booking-route", "booking-cluster", "/api/{version}/booking/{**catch-all}")]
    public void should_map_route_to_expected_cluster_and_path(string routeId, string clusterId, string path)
    {
        var route = _proxyConfig.Routes.SingleOrDefault(r => r.RouteId == routeId);

        route.Should().NotBeNull();
        route!.ClusterId.Should().Be(clusterId);
        route.Match.Path.Should().Be(path);
        route.Transforms.Should().BeNullOrEmpty();
    }

    [Fact]
    public void should_load_exactly_the_configured_routes()
    {
        _proxyConfig
            .Routes.Select(r => r.RouteId)
            .Should()
            .BeEquivalentTo(
                "flight-route",
                "passenger-route",
                "identity-route",
                "identity-connect-route",
                "identity-wellknown-route",
                "booking-route"
            );
    }

    [Theory]
    [InlineData("flight-cluster", "https://localhost:7001")]
    [InlineData("passenger-cluster", "https://localhost:7002")]
    [InlineData("identity-cluster", "https://localhost:7003")]
    [InlineData("booking-cluster", "https://localhost:7004")]
    public void should_map_cluster_to_expected_destination(string clusterId, string address)
    {
        var cluster = _proxyConfig.Clusters.SingleOrDefault(c => c.ClusterId == clusterId);

        cluster.Should().NotBeNull();
        cluster!.Destinations.Should().NotBeNull();
        cluster.Destinations!.Values.Select(d => d.Address).Should().BeEquivalentTo(address);
    }

    [Fact]
    public void should_load_exactly_the_configured_clusters()
    {
        _proxyConfig
            .Clusters.Select(c => c.ClusterId)
            .Should()
            .BeEquivalentTo("flight-cluster", "passenger-cluster", "identity-cluster", "booking-cluster");
    }
}
