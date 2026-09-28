using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Yarp.ReverseProxy.Configuration;

namespace Gateway.Unit.Test;

public class GatewayRouteConfigTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly IProxyConfig _config;

    public GatewayRouteConfigTests(WebApplicationFactory<Program> factory)
    {
        var app = factory.WithWebHostBuilder(builder => builder.UseEnvironment("test"));
        _config = app.Services.GetRequiredService<IProxyConfigProvider>().GetConfig();
    }

    [Theory]
    [InlineData("flight")]
    [InlineData("passenger")]
    [InlineData("booking")]
    [InlineData("identity")]
    public void module_route_is_bound_to_its_own_cluster(string module)
    {
        var route = Assert.Single(_config.Routes, r => r.RouteId == module);

        Assert.Equal(module, route.ClusterId);
        Assert.Equal($"/api/{{version}}/{module}/{{**catch-all}}", route.Match.Path);
        Assert.Equal("ApiScope", route.AuthorizationPolicy);
        Assert.Equal("ingress", route.RateLimiterPolicy);
    }

    [Theory]
    [InlineData("identity-connect", "/connect/{**catch-all}")]
    [InlineData("identity-discovery", "/.well-known/{**catch-all}")]
    public void identity_server_protocol_routes_are_anonymous(string routeId, string path)
    {
        var route = Assert.Single(_config.Routes, r => r.RouteId == routeId);

        Assert.Equal("identity", route.ClusterId);
        Assert.Equal(path, route.Match.Path);
        Assert.Equal("Anonymous", route.AuthorizationPolicy);
    }

    [Fact]
    public void fallback_route_forwards_everything_else_to_the_monolith_with_lowest_priority()
    {
        var fallback = Assert.Single(_config.Routes, r => r.RouteId == "monolith-fallback");

        Assert.Equal("monolith", fallback.ClusterId);
        Assert.Equal("/{**catch-all}", fallback.Match.Path);
        Assert.Equal("Anonymous", fallback.AuthorizationPolicy);
        Assert.All(
            _config.Routes.Where(r => r.RouteId != fallback.RouteId),
            r => Assert.True((r.Order ?? 0) < fallback.Order)
        );
    }

    [Fact]
    public void every_cluster_initially_targets_the_monolith()
    {
        Assert.Equal(
            ["booking", "flight", "identity", "monolith", "passenger"],
            _config.Clusters.Select(c => c.ClusterId).Order()
        );
        Assert.All(
            _config.Clusters,
            cluster =>
            {
                var destination = Assert.Single(cluster.Destinations!);
                Assert.Equal("monolith", destination.Key);
                Assert.Equal("http://localhost:3001", destination.Value.Address);
            }
        );
    }

    [Fact]
    public void every_route_references_a_configured_cluster()
    {
        var clusterIds = _config.Clusters.Select(c => c.ClusterId).ToHashSet();

        Assert.All(_config.Routes, route => Assert.Contains(route.ClusterId, clusterIds));
    }
}
