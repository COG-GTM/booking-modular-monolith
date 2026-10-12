using System.Net;
using Gateway.Configurations;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Gateway.Unit.Test;

public class GatewayUpstreamHeaderTests : IClassFixture<DownstreamStub>
{
    private readonly DownstreamStub _downstream;

    public GatewayUpstreamHeaderTests(DownstreamStub downstream)
    {
        _downstream = downstream;
    }

    private WebApplicationFactory<Program> CreateFactory(bool? enabled) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("test");

            foreach (var cluster in new[] { "flight", "passenger", "booking", "identity", "monolith" })
                builder.UseSetting(
                    $"ReverseProxy:Clusters:{cluster}:Destinations:monolith:Address",
                    _downstream.Address
                );

            if (enabled is { } value)
                builder.UseSetting("UpstreamHeaderOptions:Enabled", value.ToString());
        });

    [Fact]
    public async Task proxied_response_names_the_destination_that_served_it_when_enabled()
    {
        using var factory = CreateFactory(enabled: true);

        var response = await factory.CreateClient().GetAsync("/.well-known/openid-configuration");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(
            $"{new Uri(_downstream.Address).Host}:{new Uri(_downstream.Address).Port}",
            Assert.Single(response.Headers.GetValues(UpstreamHeaderOptions.HeaderName))
        );
    }

    [Theory]
    [InlineData(null)]
    [InlineData(false)]
    public async Task upstream_header_is_not_emitted_by_default(bool? enabled)
    {
        using var factory = CreateFactory(enabled);

        var response = await factory.CreateClient().GetAsync("/.well-known/openid-configuration");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(response.Headers.Contains(UpstreamHeaderOptions.HeaderName));
    }

    [Fact]
    public async Task gateway_own_endpoints_do_not_get_an_upstream_header()
    {
        using var factory = CreateFactory(enabled: true);

        var response = await factory.CreateClient().GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(response.Headers.Contains(UpstreamHeaderOptions.HeaderName));
    }
}
