using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
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
    public async Task anonymous_identity_routes_are_forwarded(string path)
    {
        await using var factory = GatewayFactory.AllClustersTo(_monolith);
        var client = factory.CreateClient();

        var response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(path, Assert.Single(_monolith.Requests).Path);
    }

    [Theory]
    [InlineData("/api/v1/unknown-module/anything")]
    [InlineData("/api")]
    [InlineData("/")]
    [InlineData("/scalar/v1")]
    public async Task unmatched_paths_return_404_without_calling_a_backend(string path)
    {
        await using var factory = GatewayFactory.AllClustersTo(_monolith);
        var response = await factory.CreateClient().GetAsync(path);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Empty(_monolith.Requests);
        Assert.Empty(_flightService.Requests);
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
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            GatewayFactory.CreateToken()
        );

        var flight = await client.GetAsync("/api/v1/flight/get-available-flights");
        var booking = await client.PostAsync("/api/v1/booking", content: null);

        Assert.Equal("flight-service", await flight.Content.ReadAsStringAsync());
        Assert.Equal("monolith", await booking.Content.ReadAsStringAsync());
        Assert.Equal("/api/v1/flight/get-available-flights", Assert.Single(_flightService.Requests).Path);
        Assert.Equal("/api/v1/booking", Assert.Single(_monolith.Requests).Path);
    }

    [Fact]
    public async Task cutover_overrides_repoint_and_freeze_clusters_without_restarting()
    {
        var configPath = Path.Combine(Path.GetTempPath(), $"gateway-cutover-{Guid.NewGuid():N}.json");
        await File.WriteAllTextAsync(configPath, CutoverConfiguration(_flightService.Address));
        var settings = new Dictionary<string, string?> { ["Cutover:ConfigFile"] = configPath };
        foreach (var cluster in new[] { "identity", "passenger", "booking" })
            settings[GatewayFactory.ClusterAddressKey(cluster)] = _monolith.Address;

        await using var factory = new GatewayFactory(settings);
        var configuration = factory.Services.GetRequiredService<IConfiguration>();
        Assert.Equal(configPath, configuration["Cutover:ConfigFile"]);
        Assert.Equal(
            _flightService.Address,
            configuration["ReverseProxy:Clusters:flight:Destinations:primary:Address"]
        );
        var initialFlightCluster = factory
            .Services.GetRequiredService<IProxyStateLookup>()
            .GetClusters()
            .Single(cluster => cluster.ClusterId == "flight");
        Assert.Equal(_flightService.Address, initialFlightCluster.Model.Config.Destinations!["primary"].Address);
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            GatewayFactory.CreateToken()
        );

        var repointed = await SendUntilAsync(
            client,
            "/api/v1/flight/get-available-flights",
            async response =>
                response.StatusCode == HttpStatusCode.OK
                && _flightService.Requests.Count > 0
                && await response.Content.ReadAsStringAsync() == "flight-service"
        );
        Assert.Equal("flight-service", await repointed.Content.ReadAsStringAsync());
        Assert.Empty(_monolith.Requests);

        await ReplaceConfigurationAsync(configPath, CutoverConfiguration(_flightService.Address, ["flight"], 17));
        var frozen = await SendUntilAsync(
            client,
            "/api/v1/flight/get-available-flights",
            response =>
                Task.FromResult(
                    response.StatusCode == HttpStatusCode.ServiceUnavailable
                        && response.Headers.Contains("X-Gateway-Cutover")
                )
        );
        Assert.Equal("application/problem+json", frozen.Content.Headers.ContentType?.MediaType);
        Assert.Equal("17", frozen.Headers.RetryAfter?.ToString());
        Assert.Equal("frozen", frozen.Headers.GetValues("X-Gateway-Cutover").Single());
        var flightRequestsWhileFrozen = _flightService.Requests.Count;
        var stillFrozen = await client.GetAsync("/api/v1/flight/get-available-flights");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, stillFrozen.StatusCode);
        Assert.Equal(flightRequestsWhileFrozen, _flightService.Requests.Count);

        var otherCluster = await client.PostAsync("/api/v1/booking", content: null);
        Assert.Equal(HttpStatusCode.OK, otherCluster.StatusCode);
        Assert.Equal("monolith", await otherCluster.Content.ReadAsStringAsync());

        await ReplaceConfigurationAsync(configPath, CutoverConfiguration(_flightService.Address));
        var unfrozen = await SendUntilAsync(
            client,
            "/api/v1/flight/get-available-flights",
            async response =>
                response.StatusCode == HttpStatusCode.OK
                && _flightService.Requests.Count > flightRequestsWhileFrozen
                && await response.Content.ReadAsStringAsync() == "flight-service"
        );
        Assert.Equal("flight-service", await unfrozen.Content.ReadAsStringAsync());

        File.Delete(configPath);
    }

    [Fact]
    public async Task shipped_config_points_clusters_at_service_hosts()
    {
        await using var factory = new GatewayFactory(new Dictionary<string, string?>());
        var proxyState = factory.Services.GetRequiredService<IProxyStateLookup>();

        var clusters = proxyState.GetClusters().ToDictionary(c => c.ClusterId);
        Assert.Equal(["booking", "flight", "identity", "passenger"], clusters.Keys.Order());
        Assert.Equal("http://localhost:5103/", clusters["identity"].Model.Config.Destinations!["primary"].Address);
        Assert.Equal("http://localhost:5107/", clusters["flight"].Model.Config.Destinations!["primary"].Address);
        Assert.Equal("http://localhost:5102/", clusters["passenger"].Model.Config.Destinations!["primary"].Address);
        Assert.Equal("http://localhost:5004/", clusters["booking"].Model.Config.Destinations!["primary"].Address);

        var routes = proxyState.GetRoutes().ToList();
        Assert.All(routes, r => Assert.Contains(r.Cluster!.ClusterId, clusters.Keys));
        Assert.All(routes, r => Assert.Equal("gateway", r.Config.RateLimiterPolicy));
        Assert.All(
            routes.Where(r => r.Config.Match.Path!.StartsWith("/api/", StringComparison.Ordinal)),
            r => Assert.Equal("ApiScope", r.Config.AuthorizationPolicy)
        );
    }

    private static string CutoverConfiguration(
        string flightAddress,
        string[]? frozenClusters = null,
        int retryAfter = 30
    ) =>
        JsonSerializer.Serialize(
            new
            {
                Cutover = new { FrozenClusters = frozenClusters ?? [], RetryAfterSeconds = retryAfter },
                ReverseProxy = new
                {
                    Clusters = new
                    {
                        flight = new { Destinations = new { primary = new { Address = flightAddress } } },
                    },
                },
            }
        );

    private static async Task ReplaceConfigurationAsync(string path, string contents)
    {
        var temporaryPath = $"{path}.{Guid.NewGuid():N}.tmp";
        await File.WriteAllTextAsync(temporaryPath, contents);
        File.Move(temporaryPath, path, overwrite: true);
    }

    private static async Task<HttpResponseMessage> SendUntilAsync(
        HttpClient client,
        string path,
        Func<HttpResponseMessage, Task<bool>> predicate
    )
    {
        var timeout = DateTime.UtcNow.AddSeconds(10);
        HttpResponseMessage? response = null;
        while (DateTime.UtcNow < timeout)
        {
            response?.Dispose();
            response = await client.GetAsync(path);
            if (await predicate(response))
                return response;
            await Task.Delay(100);
        }

        return response ?? throw new TimeoutException($"Gateway did not reach the expected state for {path}.");
    }
}
