using System.Net;
using System.Security.Claims;
using Gateway.Configurations;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using WebMotions.Fake.Authentication.JwtBearer;
using Xunit;

namespace Gateway.Unit.Test;

// Cut-over scenario: clusters point at different hosts (extracted service vs monolith), and the
// header must name the destination that actually served each request so a flip can be verified.
public class GatewayUpstreamHeaderPerClusterTests : IAsyncLifetime
{
    private readonly DownstreamStub _service = new();
    private readonly DownstreamStub _monolith = new();
    private WebApplicationFactory<Program> _factory = null!;

    public async Task InitializeAsync()
    {
        await _service.InitializeAsync();
        await _monolith.InitializeAsync();

        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("test");
            builder.UseSetting("UpstreamHeaderOptions:Enabled", "true");

            foreach (var cluster in new[] { "flight", "passenger", "booking", "identity" })
                builder.UseSetting($"ReverseProxy:Clusters:{cluster}:Destinations:monolith:Address", _service.Address);

            builder.UseSetting("ReverseProxy:Clusters:monolith:Destinations:monolith:Address", _monolith.Address);

            builder.ConfigureTestServices(services =>
                services
                    .AddAuthentication(options =>
                    {
                        options.DefaultAuthenticateScheme = FakeJwtBearerDefaults.AuthenticationScheme;
                        options.DefaultChallengeScheme = FakeJwtBearerDefaults.AuthenticationScheme;
                    })
                    .AddFakeJwtBearer()
            );
        });
    }

    public async Task DisposeAsync()
    {
        _factory.Dispose();
        await _service.DisposeAsync();
        await _monolith.DisposeAsync();
    }

    [Fact]
    public async Task upstream_header_names_the_cluster_destination_that_served_each_request()
    {
        var client = SignedIn(_factory);

        var identity = await client.GetAsync("/.well-known/openid-configuration");
        var monolith = await client.GetAsync("/some-legacy-page");

        Assert.Equal(HttpStatusCode.OK, identity.StatusCode);
        Assert.Equal(HttpStatusCode.OK, monolith.StatusCode);
        Assert.Equal(
            HostPort(_service.Address),
            Assert.Single(identity.Headers.GetValues(UpstreamHeaderOptions.HeaderName))
        );
        Assert.Equal(
            HostPort(_monolith.Address),
            Assert.Single(monolith.Headers.GetValues(UpstreamHeaderOptions.HeaderName))
        );
        Assert.NotEqual(HostPort(_service.Address), HostPort(_monolith.Address));
    }

    [Fact]
    public async Task upstream_header_reflects_a_flipped_cluster_address()
    {
        using var flipped = _factory.WithWebHostBuilder(builder =>
            builder.UseSetting("ReverseProxy:Clusters:identity:Destinations:monolith:Address", _monolith.Address)
        );

        var response = await SignedIn(flipped).GetAsync("/.well-known/openid-configuration");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(
            HostPort(_monolith.Address),
            Assert.Single(response.Headers.GetValues(UpstreamHeaderOptions.HeaderName))
        );
    }

    private static HttpClient SignedIn(WebApplicationFactory<Program> factory)
    {
        var client = factory.CreateClient();
        client.SetFakeBearerToken(new Dictionary<string, object> { { ClaimTypes.Name, "operator" } });
        return client;
    }

    private static string HostPort(string address)
    {
        var uri = new Uri(address);
        return $"{uri.Host}:{uri.Port}";
    }
}
