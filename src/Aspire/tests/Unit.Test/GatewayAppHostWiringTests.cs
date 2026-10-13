using Aspire.Hosting.ApplicationModel;
using Xunit;
using static AppHost.Unit.Test.AppHostModel;

namespace AppHost.Unit.Test;

// Monolith topology (AppHost:Topology=Monolith): every gateway cluster fronts src/Api.
public class GatewayAppHostWiringTests
{
    private const string ApiHttpUrl = "{api.bindings.api-http.url}";
    private const string ApiHttpsUrl = "{api.bindings.api-https.url}";

    [Fact]
    public async Task every_gateway_cluster_defaults_to_the_monolith_http_endpoint()
    {
        await using var appHost = await BuildMonolithAsync();
        var env = await GetEnvironmentAsync(appHost.App, "gateway");

        foreach (var cluster in Clusters)
        {
            Assert.Equal(ApiHttpUrl, env[ClusterAddressKey(cluster)]);
        }
    }

    [Theory]
    [InlineData("flight")]
    [InlineData("passenger")]
    [InlineData("booking")]
    [InlineData("identity")]
    [InlineData("monolith")]
    public async Task a_cluster_is_repointed_only_through_its_gateway_clusters_override(string cluster)
    {
        var extractedService = $"http://{cluster}-service:80";
        await using var appHost = await BuildMonolithAsync(
            new() { [$"Gateway:Clusters:{cluster}"] = extractedService }
        );
        var env = await GetEnvironmentAsync(appHost.App, "gateway");

        Assert.Equal(extractedService, env[ClusterAddressKey(cluster)]);

        foreach (var other in Clusters.Where(name => name != cluster))
        {
            Assert.Equal(ApiHttpUrl, env[ClusterAddressKey(other)]);
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task blank_cluster_override_keeps_the_monolith_destination(string blank)
    {
        await using var appHost = await BuildMonolithAsync(new() { ["Gateway:Clusters:flight"] = blank });
        var env = await GetEnvironmentAsync(appHost.App, "gateway");

        Assert.Equal(ApiHttpUrl, env[ClusterAddressKey("flight")]);
    }

    [Fact]
    public async Task gateway_and_api_validate_tokens_against_the_same_issuer_the_api_issues()
    {
        await using var appHost = await BuildMonolithAsync();
        var gatewayEnv = await GetEnvironmentAsync(appHost.App, "gateway");
        var apiEnv = await GetEnvironmentAsync(appHost.App, "api");

        Assert.Equal(ApiHttpsUrl, apiEnv["AuthOptions__IssuerUri"]);
        Assert.Equal(ApiHttpsUrl, apiEnv["Jwt__Authority"]);
        Assert.Equal(ApiHttpsUrl, gatewayEnv["Jwt__Authority"]);
    }

    [Fact]
    public async Task gateway_is_exposed_on_its_own_ports_and_waits_for_the_api()
    {
        await using var appHost = await BuildMonolithAsync();
        var gateway = GetProject(appHost.App, "gateway");

        var endpoints = gateway.Annotations.OfType<EndpointAnnotation>().ToDictionary(endpoint => endpoint.Name);
        Assert.Equal(5000, endpoints["gateway-http"].Port);
        Assert.Equal("http", endpoints["gateway-http"].UriScheme);
        Assert.Equal(5001, endpoints["gateway-https"].Port);
        Assert.Equal("https", endpoints["gateway-https"].UriScheme);

        Assert.Contains(gateway.Annotations.OfType<WaitAnnotation>(), wait => wait.Resource.Name == "api");
    }
}
