using System.Net;
using System.Net.Http.Headers;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Yarp.ReverseProxy;

namespace Gateway.Test;

public class GatewayRoutingTests : IAsyncLifetime
{
    private EchoBackend _monolith = null!;
    private EchoBackend _flightService = null!;

    public async Task InitializeAsync()
    {
        _monolith = await EchoBackend.StartAsync("monolith");
        _flightService = await EchoBackend.StartAsync("flight-service");
    }

    public async Task DisposeAsync()
    {
        await _monolith.DisposeAsync();
        await _flightService.DisposeAsync();
    }

    public static TheoryData<string, string> ProtectedEndpoints =>
        new()
        {
            { "POST", "/api/v1/identity/register-user" },
            { "POST", "/api/v1/flight" },
            { "POST", "/api/v1/flight/seat" },
            { "GET", "/api/v1/flight/get-available-flights" },
            { "GET", "/api/v1/flight/get-available-seats/3c5c0000-97c6-fc34-a0cb-08db322230c8" },
            { "POST", "/api/v1/flight/reserve-seat" },
            { "POST", "/api/v1/passenger/complete-registration" },
            { "GET", "/api/v1/passenger/4c5c0000-97c6-fc34-2eb9-08db322230c9" },
            { "POST", "/api/v1/booking" },
            { "GET", "/api/v1/unknown-module/anything" },
            { "GET", "/api" },
            { "GET", "/api/" },
            { "GET", "/API/V1/FLIGHT/get-available-flights" },
        };

    [Theory]
    [MemberData(nameof(ProtectedEndpoints))]
    public async Task protected_endpoint_without_token_is_rejected_at_the_gateway(string method, string path)
    {
        await using var factory = GatewayFactory.AllClustersTo(_monolith);
        var client = factory.CreateClient();

        var response = await client.SendAsync(new HttpRequestMessage(new HttpMethod(method), path));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Empty(_monolith.Requests);
    }

    [Fact]
    public async Task token_without_api_scope_is_forbidden()
    {
        await using var factory = GatewayFactory.AllClustersTo(_monolith);
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            GatewayFactory.CreateToken(scope: "other")
        );

        var response = await client.GetAsync("/api/v1/flight/get-available-flights");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Empty(_monolith.Requests);
    }

    [Theory]
    [MemberData(nameof(ProtectedEndpoints))]
    public async Task protected_endpoint_with_token_is_forwarded_with_path_and_token_intact(string method, string path)
    {
        await using var factory = GatewayFactory.AllClustersTo(_monolith);
        var client = factory.CreateClient();
        var token = GatewayFactory.CreateToken();

        var request = new HttpRequestMessage(new HttpMethod(method), path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("monolith", await response.Content.ReadAsStringAsync());
        var forwarded = Assert.Single(_monolith.Requests);
        Assert.Equal(method, forwarded.Method);
        Assert.Equal(path, forwarded.Path);
        Assert.Equal($"Bearer {token}", forwarded.Headers["Authorization"]);
    }

    [Theory]
    [InlineData("/connect/token")]
    [InlineData("/.well-known/openid-configuration")]
    [InlineData("/")]
    [InlineData("/scalar/v1")]
    public async Task anonymous_identity_and_monolith_routes_are_forwarded(string path)
    {
        await using var factory = GatewayFactory.AllClustersTo(_monolith);
        var client = factory.CreateClient();

        var response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(path, Assert.Single(_monolith.Requests).Path);
    }

    [Fact]
    public async Task identity_routes_preserve_the_public_host_for_issuer_and_discovery_urls()
    {
        await using var factory = GatewayFactory.AllClustersTo(_monolith);
        var client = factory.CreateClient();

        await client.GetAsync("/.well-known/openid-configuration");

        Assert.Equal("localhost", Assert.Single(_monolith.Requests).Headers["Host"]);
    }

    [Fact]
    public async Task repointing_a_cluster_is_a_config_only_change()
    {
        await using var factory = GatewayFactory.AllClustersTo(
            _monolith,
            new Dictionary<string, string?> { [GatewayFactory.ClusterAddressKey("flight")] = _flightService.Address }
        );
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", GatewayFactory.CreateToken());

        var flight = await client.GetAsync("/api/v1/flight/get-available-flights");
        var booking = await client.PostAsync("/api/v1/booking", content: null);

        Assert.Equal("flight-service", await flight.Content.ReadAsStringAsync());
        Assert.Equal("monolith", await booking.Content.ReadAsStringAsync());
        Assert.Equal("/api/v1/flight/get-available-flights", Assert.Single(_flightService.Requests).Path);
        Assert.Equal("/api/v1/booking", Assert.Single(_monolith.Requests).Path);
    }

    [Fact]
    public async Task shipped_config_points_every_cluster_at_the_monolith()
    {
        await using var factory = new GatewayFactory(new Dictionary<string, string?>());
        var proxyState = factory.Services.GetRequiredService<IProxyStateLookup>();

        var clusters = proxyState.GetClusters().ToDictionary(c => c.ClusterId);
        Assert.Equal(["booking", "flight", "identity", "monolith", "passenger"], clusters.Keys.Order());
        Assert.All(
            clusters.Values,
            c => Assert.Equal("http://localhost:3001/", c.Model.Config.Destinations!["primary"].Address)
        );

        var routes = proxyState.GetRoutes().ToList();
        Assert.All(routes, r => Assert.Contains(r.Cluster!.ClusterId, clusters.Keys));
        Assert.All(routes, r => Assert.Equal("gateway", r.Config.RateLimiterPolicy));
        Assert.All(
            routes.Where(r => r.Config.Match.Path!.StartsWith("/api/", StringComparison.Ordinal)),
            r => Assert.Equal("ApiScope", r.Config.AuthorizationPolicy)
        );
    }
}
