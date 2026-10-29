using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Gateway.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Gateway.Test;

public class CutoverFreezeTests : IAsyncLifetime
{
    private EchoBackend _backend = null!;

    public async Task InitializeAsync()
    {
        _backend = await EchoBackend.StartAsync("echo");
    }

    public async Task DisposeAsync()
    {
        await _backend.DisposeAsync();
    }

    [Fact]
    public async Task frozen_cluster_match_is_case_insensitive_and_other_clusters_are_forwarded()
    {
        await using var factory = GatewayFactory.AllClustersTo(
            _backend,
            new Dictionary<string, string?> { ["Cutover:FrozenClusters:0"] = "FLIGHT" }
        );
        var client = CreateAuthenticatedClient(factory);

        var frozen = await client.GetAsync("/api/v1/flight/get-available-flights");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, frozen.StatusCode);
        Assert.Equal("frozen", frozen.Headers.GetValues("X-Gateway-Cutover").Single());
        Assert.Empty(_backend.Requests);

        var booking = await client.PostAsync("/api/v1/booking", content: null);

        Assert.Equal(HttpStatusCode.OK, booking.StatusCode);
        Assert.Equal("/api/v1/booking", Assert.Single(_backend.Requests).Path);
    }

    [Fact]
    public async Task frozen_response_contains_problem_details_and_clamps_negative_retry_after()
    {
        await using var factory = GatewayFactory.AllClustersTo(
            _backend,
            new Dictionary<string, string?>
            {
                ["Cutover:FrozenClusters:0"] = "flight",
                ["Cutover:RetryAfterSeconds"] = "-5",
            }
        );
        var client = CreateAuthenticatedClient(factory);

        var response = await client.GetAsync("/api/v1/flight/get-available-flights");

        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("0", response.Headers.RetryAfter?.ToString());
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var problem = document.RootElement;
        Assert.Equal(503, problem.GetProperty("status").GetInt32());
        Assert.Equal("about:blank", problem.GetProperty("type").GetString());
        Assert.Equal("Service temporarily unavailable during cutover", problem.GetProperty("title").GetString());
        Assert.Equal("The flight route is temporarily frozen.", problem.GetProperty("detail").GetString());
        Assert.Empty(_backend.Requests);
    }

    [Fact]
    public async Task frozen_response_defaults_retry_after_to_thirty_seconds()
    {
        await using var factory = GatewayFactory.AllClustersTo(
            _backend,
            new Dictionary<string, string?> { ["Cutover:FrozenClusters:0"] = "flight" }
        );
        var client = CreateAuthenticatedClient(factory);

        var response = await client.GetAsync("/api/v1/flight/get-available-flights");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("30", response.Headers.RetryAfter?.ToString());
        Assert.Empty(_backend.Requests);
    }

    [Fact]
    public async Task authentication_runs_before_freeze_middleware()
    {
        await using var factory = GatewayFactory.AllClustersTo(
            _backend,
            new Dictionary<string, string?> { ["Cutover:FrozenClusters:0"] = "flight" }
        );
        var response = await factory.CreateClient().GetAsync("/api/v1/flight/get-available-flights");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.False(response.Headers.Contains("X-Gateway-Cutover"));
        Assert.Empty(_backend.Requests);
    }

    [Fact]
    public async Task freezing_identity_blocks_anonymous_token_requests()
    {
        await using var factory = GatewayFactory.AllClustersTo(
            _backend,
            new Dictionary<string, string?> { ["Cutover:FrozenClusters:0"] = "identity" }
        );
        var response = await factory.CreateClient().GetAsync("/connect/token");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("frozen", response.Headers.GetValues("X-Gateway-Cutover").Single());
        Assert.Empty(_backend.Requests);
    }

    [Fact]
    public async Task missing_cutover_config_file_does_not_prevent_gateway_startup()
    {
        var configPath = Path.Combine(Path.GetTempPath(), $"missing-cutover-{Guid.NewGuid():N}.json");
        Assert.False(File.Exists(configPath));
        await using var factory = GatewayFactory.AllClustersTo(
            _backend,
            new Dictionary<string, string?> { [GatewayFactory.CutoverConfigFileKey] = configPath }
        );
        var client = CreateAuthenticatedClient(factory);

        var response = await client.GetAsync("/api/v1/flight/get-available-flights");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("/api/v1/flight/get-available-flights", Assert.Single(_backend.Requests).Path);
    }

    [Fact]
    public async Task shipped_cutover_options_default_to_unfrozen_and_requests_are_not_frozen()
    {
        await using var factory = new GatewayFactory(new Dictionary<string, string?>());
        var options = factory.Services.GetRequiredService<IOptions<CutoverOptions>>().Value;

        Assert.Empty(options.FrozenClusters);
        Assert.Equal(30, options.RetryAfterSeconds);

        var client = CreateAuthenticatedClient(factory);
        var response = await client.GetAsync("/api/v1/flight/get-available-flights");

        Assert.NotEqual(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.False(response.Headers.Contains("X-Gateway-Cutover"));
    }

    private static HttpClient CreateAuthenticatedClient(GatewayFactory factory)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            GatewayFactory.CreateToken()
        );
        return client;
    }
}
