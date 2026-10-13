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

public class GatewayUpstreamHeaderTests : IClassFixture<DownstreamStub>
{
    private const string Discovery = "/.well-known/openid-configuration";
    private readonly DownstreamStub _downstream;

    public GatewayUpstreamHeaderTests(DownstreamStub downstream)
    {
        _downstream = downstream;
    }

    private WebApplicationFactory<Program> CreateFactory(bool? enabled, bool? authenticatedOnly = null) =>
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
            if (authenticatedOnly is { } auth)
                builder.UseSetting("UpstreamHeaderOptions:AuthenticatedOnly", auth.ToString());

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

    private static HttpClient SignedIn(WebApplicationFactory<Program> factory)
    {
        var client = factory.CreateClient();
        client.SetFakeBearerToken(new Dictionary<string, object> { { ClaimTypes.Name, "operator" } });
        return client;
    }

    [Fact]
    public async Task proxied_response_names_the_destination_that_served_it_for_an_authenticated_caller()
    {
        using var factory = CreateFactory(enabled: true);

        var response = await SignedIn(factory).GetAsync(Discovery);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(
            $"{new Uri(_downstream.Address).Host}:{new Uri(_downstream.Address).Port}",
            Assert.Single(response.Headers.GetValues(UpstreamHeaderOptions.HeaderName))
        );
    }

    [Fact]
    public async Task anonymous_callers_never_see_the_upstream_header_even_when_enabled()
    {
        using var factory = CreateFactory(enabled: true);

        var response = await factory.CreateClient().GetAsync(Discovery);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(response.Headers.Contains(UpstreamHeaderOptions.HeaderName));
    }

    [Fact]
    public async Task anonymous_callers_see_the_upstream_header_only_when_authenticated_only_is_switched_off()
    {
        using var factory = CreateFactory(enabled: true, authenticatedOnly: false);

        var response = await factory.CreateClient().GetAsync(Discovery);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.Contains(UpstreamHeaderOptions.HeaderName));
    }

    [Theory]
    [InlineData(null)]
    [InlineData(false)]
    public async Task upstream_header_is_not_emitted_by_default(bool? enabled)
    {
        using var factory = CreateFactory(enabled);

        var response = await SignedIn(factory).GetAsync(Discovery);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(response.Headers.Contains(UpstreamHeaderOptions.HeaderName));
    }

    [Fact]
    public async Task gateway_own_endpoints_do_not_get_an_upstream_header()
    {
        using var factory = CreateFactory(enabled: true);

        var response = await SignedIn(factory).GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(response.Headers.Contains(UpstreamHeaderOptions.HeaderName));
    }
}
