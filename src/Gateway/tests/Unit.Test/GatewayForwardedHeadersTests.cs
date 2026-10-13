using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Gateway.Unit.Test;

public class GatewayForwardedHeadersTests : IClassFixture<DownstreamStub>
{
    private const string AnonymousPath = "/.well-known/jwks";
    private const string ClientA = "203.0.113.10";
    private const string ClientB = "203.0.113.20";
    private readonly DownstreamStub _downstream;

    public GatewayForwardedHeadersTests(DownstreamStub downstream)
    {
        _downstream = downstream;
    }

    [Fact]
    public async Task forwarded_client_ip_is_ignored_when_the_peer_is_not_a_trusted_proxy()
    {
        using var factory = CreateFactory(IPAddress.Parse("10.0.0.2"));
        var client = factory.CreateClient();

        var statuses = new List<HttpStatusCode>
        {
            await SendAsAsync(client, ClientA),
            await SendAsAsync(client, ClientB),
            await SendAsAsync(client, ClientA),
        };

        Assert.Equal([HttpStatusCode.OK, HttpStatusCode.OK, HttpStatusCode.TooManyRequests], statuses);
    }

    [Theory]
    [InlineData("TrustedProxyOptions:KnownProxies:0", "10.0.0.2", "10.0.0.2")]
    [InlineData("TrustedProxyOptions:KnownNetworks:0", "10.0.0.0/8", "10.1.2.3")]
    public async Task clients_behind_a_trusted_proxy_are_rate_limited_separately(
        string settingKey,
        string settingValue,
        string peerAddress
    )
    {
        using var factory = CreateFactory(IPAddress.Parse(peerAddress), (settingKey, settingValue));
        var client = factory.CreateClient();

        var statuses = new List<HttpStatusCode>
        {
            await SendAsAsync(client, ClientA),
            await SendAsAsync(client, ClientA),
            await SendAsAsync(client, ClientB),
            await SendAsAsync(client, ClientA),
        };

        Assert.Equal(
            [HttpStatusCode.OK, HttpStatusCode.OK, HttpStatusCode.OK, HttpStatusCode.TooManyRequests],
            statuses
        );
    }

    [Fact]
    public async Task trusted_proxy_forwarded_ip_only_counts_the_first_hop()
    {
        using var factory = CreateFactory(
            IPAddress.Parse("10.0.0.2"),
            ("TrustedProxyOptions:KnownProxies:0", "10.0.0.2")
        );
        var client = factory.CreateClient();

        var statuses = new List<HttpStatusCode>
        {
            await SendAsAsync(client, $"{ClientA}, {ClientB}"),
            await SendAsAsync(client, $"{ClientA}, {ClientB}"),
            await SendAsAsync(client, ClientB),
        };

        Assert.Equal([HttpStatusCode.OK, HttpStatusCode.OK, HttpStatusCode.TooManyRequests], statuses);
    }

    private static async Task<HttpStatusCode> SendAsAsync(HttpClient client, string forwardedFor)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, AnonymousPath);
        request.Headers.Add("X-Forwarded-For", forwardedFor);

        var response = await client.SendAsync(request);

        return response.StatusCode;
    }

    private WebApplicationFactory<Program> CreateFactory(
        IPAddress peerAddress,
        params (string Key, string Value)[] settings
    )
    {
        return new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("test");
            builder.UseSetting("RateLimitOptions:PermitLimit", "2");
            builder.UseSetting("RateLimitOptions:WindowSeconds", "60");

            foreach (var (key, value) in settings)
                builder.UseSetting(key, value);

            foreach (var cluster in new[] { "flight", "passenger", "booking", "identity", "monolith" })
                builder.UseSetting(
                    $"ReverseProxy:Clusters:{cluster}:Destinations:monolith:Address",
                    _downstream.Address
                );

            builder.ConfigureTestServices(services =>
                services.AddSingleton<IStartupFilter>(new PeerAddressStartupFilter(peerAddress))
            );
        });
    }

    // TestServer never sets a remote address; this runs ahead of the gateway pipeline so
    // UseForwardedHeaders sees the configured peer, as it would behind a real load balancer.
    private sealed class PeerAddressStartupFilter(IPAddress peerAddress) : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) =>
            app =>
            {
                app.Use(
                    (context, nextMiddleware) =>
                    {
                        context.Connection.RemoteIpAddress = peerAddress;
                        return nextMiddleware(context);
                    }
                );
                next(app);
            };
    }
}
