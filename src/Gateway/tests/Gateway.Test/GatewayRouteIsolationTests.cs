using System.Net;
using System.Net.Http.Headers;
using Xunit;

namespace Gateway.Test;

public class GatewayRouteIsolationTests
{
    [Theory]
    [InlineData("/api/v1/identity/register-user", "identity")]
    [InlineData("/connect/token", "identity")]
    [InlineData("/.well-known/openid-configuration", "identity")]
    [InlineData("/api/v1/flight", "flight")]
    [InlineData("/api/v1/flight/seat", "flight")]
    [InlineData("/api/v1/passenger/123", "passenger")]
    [InlineData("/api/v1/booking", "booking")]
    [InlineData("/api/v1/booking/123", "booking")]
    [InlineData("/api/v1/unknown-module/anything", "monolith")]
    [InlineData("/api", "monolith")]
    [InlineData("/scalar/v1", "monolith")]
    public async Task routes_select_their_cluster_instead_of_a_fallback(string path, string cluster)
    {
        await using var fallback = await EchoBackend.StartAsync("fallback");
        await using var selected = await EchoBackend.StartAsync(cluster);
        await using var factory = GatewayFactory.AllClustersTo(
            fallback,
            new Dictionary<string, string?> { [GatewayFactory.ClusterAddressKey(cluster)] = selected.Address }
        );
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            GatewayFactory.CreateToken()
        );

        using var response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(cluster, await response.Content.ReadAsStringAsync());
        Assert.Equal(path, Assert.Single(selected.Requests).Path);
        Assert.Empty(fallback.Requests);
    }

    [Theory]
    [InlineData("/connect/token")]
    [InlineData("/.well-known/openid-configuration")]
    [InlineData("/api/v1/identity/register-user")]
    [InlineData("/api/v1/flight/seat")]
    [InlineData("/api/v1/passenger/123")]
    [InlineData("/api/v1/booking/123")]
    [InlineData("/api/v1/flight")]
    [InlineData("/api/v1/booking")]
    [InlineData("/api/v1/unknown-module/anything")]
    [InlineData("/scalar/v1")]
    public async Task all_routes_use_destination_host_and_remove_client_supplied_forwarding_headers(string path)
    {
        await using var backend = await EchoBackend.StartAsync("identity");
        await using var factory = GatewayFactory.AllClustersTo(backend);
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", GatewayFactory.CreateToken());
        request.Headers.Host = "public.example.test";
        foreach (
            var header in new[] { "X-Forwarded-For", "X-Forwarded-Host", "X-Forwarded-Proto", "X-Forwarded-Prefix" }
        )
            request.Headers.Add(header, "untrusted");

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var headers = Assert.Single(backend.Requests).Headers;
        Assert.Equal(new Uri(backend.Address).Authority, headers["Host"]);
        foreach (
            var header in new[] { "X-Forwarded-For", "X-Forwarded-Host", "X-Forwarded-Proto", "X-Forwarded-Prefix" }
        )
            Assert.False(headers.ContainsKey(header), header);
    }
}
