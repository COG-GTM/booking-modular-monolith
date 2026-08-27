using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Yarp.ReverseProxy.Configuration;

namespace Integration.Test;

public class GatewayTests : IClassFixture<WebApplicationFactory<Gateway.Program>>
{
    private readonly WebApplicationFactory<Gateway.Program> _factory;

    public GatewayTests(WebApplicationFactory<Gateway.Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task root_endpoint_should_return_gateway_name()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/");

        response.EnsureSuccessStatusCode();
        Assert.Equal("Booking-Gateway", await response.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData("flight", "flight", "/api/{version}/flight/{**catch-all}")]
    [InlineData("passenger", "passenger", "/api/{version}/passenger/{**catch-all}")]
    [InlineData("booking", "booking", "/api/{version}/booking/{**catch-all}")]
    [InlineData("identity-api", "identity", "/api/{version}/identity/{**catch-all}")]
    [InlineData("identity-connect", "identity", "/connect/{**catch-all}")]
    [InlineData("identity-discovery", "identity", "/.well-known/{**catch-all}")]
    public void reverse_proxy_config_should_route_path_to_expected_cluster(
        string routeId,
        string clusterId,
        string path
    )
    {
        var configProvider = _factory.Services.GetRequiredService<IProxyConfigProvider>();
        var config = configProvider.GetConfig();

        var route = Assert.Single(config.Routes, r => r.RouteId == routeId);
        Assert.Equal(clusterId, route.ClusterId);
        Assert.Equal(path, route.Match.Path);
    }

    [Theory]
    [InlineData("flight")]
    [InlineData("passenger")]
    [InlineData("booking")]
    [InlineData("identity")]
    public void reverse_proxy_config_should_define_cluster_with_destination_address(string clusterId)
    {
        var configProvider = _factory.Services.GetRequiredService<IProxyConfigProvider>();
        var config = configProvider.GetConfig();

        var cluster = Assert.Single(config.Clusters, c => c.ClusterId == clusterId);
        Assert.NotNull(cluster.Destinations);
        var destination = Assert.Single(cluster.Destinations!.Values);
        Assert.False(string.IsNullOrWhiteSpace(destination.Address));
    }
}
