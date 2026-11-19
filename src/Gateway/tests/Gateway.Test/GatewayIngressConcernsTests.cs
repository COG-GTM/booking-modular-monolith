using System.Net;
using Xunit;

namespace Gateway.Test;

public class GatewayIngressConcernsTests : IAsyncLifetime
{
    private EchoBackend _monolith = null!;

    public async Task InitializeAsync() => _monolith = await EchoBackend.StartAsync("monolith");

    public async Task DisposeAsync() => await _monolith.DisposeAsync();

    [Fact]
    public async Task generates_a_guid_correlation_id_when_missing_and_echoes_it()
    {
        await using var factory = GatewayFactory.AllClustersTo(_monolith);
        var client = factory.CreateClient();

        var response = await client.GetAsync("/");

        var forwarded = Assert.Single(_monolith.Requests).Headers["correlationId"];
        Assert.True(Guid.TryParse(forwarded, out _));
        Assert.Equal(forwarded, response.Headers.GetValues("correlationId").Single());
    }

    [Fact]
    public async Task keeps_a_valid_incoming_correlation_id()
    {
        await using var factory = GatewayFactory.AllClustersTo(_monolith);
        var client = factory.CreateClient();
        var correlationId = Guid.NewGuid().ToString("N");

        var request = new HttpRequestMessage(HttpMethod.Get, "/");
        request.Headers.Add("correlationId", correlationId);
        await client.SendAsync(request);

        Assert.Equal(correlationId, Assert.Single(_monolith.Requests).Headers["correlationId"]);
    }

    [Fact]
    public async Task replaces_a_non_guid_correlation_id_the_backends_cannot_parse()
    {
        await using var factory = GatewayFactory.AllClustersTo(_monolith);
        var client = factory.CreateClient();

        var request = new HttpRequestMessage(HttpMethod.Get, "/");
        request.Headers.Add("correlationId", "not-a-guid");
        await client.SendAsync(request);

        var forwarded = Assert.Single(_monolith.Requests).Headers["correlationId"];
        Assert.NotEqual("not-a-guid", forwarded);
        Assert.True(Guid.TryParse(forwarded, out _));
    }

    [Fact]
    public async Task propagates_w3c_trace_context_to_the_destination()
    {
        await using var factory = GatewayFactory.AllClustersTo(_monolith);
        var client = factory.CreateClient();
        const string traceId = "0af7651916cd43dd8448eb211c80319c";

        var request = new HttpRequestMessage(HttpMethod.Get, "/");
        request.Headers.Add("traceparent", $"00-{traceId}-b7ad6b7169203331-01");
        await client.SendAsync(request);

        var traceparent = Assert.Single(_monolith.Requests).Headers["traceparent"];
        Assert.StartsWith($"00-{traceId}-", traceparent, StringComparison.Ordinal);
        Assert.DoesNotContain("b7ad6b7169203331", traceparent, StringComparison.Ordinal);
    }

    [Fact]
    public async Task rate_limits_per_client_with_retry_after()
    {
        await using var factory = GatewayFactory.AllClustersTo(
            _monolith,
            new Dictionary<string, string?>
            {
                ["RateLimiting:PermitLimit"] = "2",
                ["RateLimiting:WindowSeconds"] = "60",
            }
        );
        var client = factory.CreateClient();

        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < 3; i++)
            statuses.Add((await client.GetAsync("/")).StatusCode);

        Assert.Equal([HttpStatusCode.OK, HttpStatusCode.OK, HttpStatusCode.TooManyRequests], statuses);
        Assert.Equal(2, _monolith.Requests.Count);
        var limited = await client.GetAsync("/");
        Assert.True(limited.Headers.Contains("Retry-After"));
    }

    [Fact]
    public async Task cors_preflight_is_answered_by_the_gateway_for_allowed_origins()
    {
        await using var factory = GatewayFactory.AllClustersTo(
            _monolith,
            new Dictionary<string, string?> { ["Cors:AllowedOrigins:0"] = "http://app.test" }
        );
        var client = factory.CreateClient();

        var allowed = await client.SendAsync(Preflight("http://app.test"));
        var denied = await client.SendAsync(Preflight("http://evil.test"));

        Assert.Equal(HttpStatusCode.NoContent, allowed.StatusCode);
        Assert.Equal("http://app.test", allowed.Headers.GetValues("Access-Control-Allow-Origin").Single());
        Assert.False(denied.Headers.Contains("Access-Control-Allow-Origin"));
        Assert.Empty(_monolith.Requests);

        static HttpRequestMessage Preflight(string origin)
        {
            var request = new HttpRequestMessage(HttpMethod.Options, "/api/v1/flight/get-available-flights");
            request.Headers.Add("Origin", origin);
            request.Headers.Add("Access-Control-Request-Method", "GET");
            request.Headers.Add("Access-Control-Request-Headers", "authorization");
            return request;
        }
    }

    [Theory]
    [InlineData("/health")]
    [InlineData("/alive")]
    public async Task health_endpoints_are_served_by_the_gateway_itself(string path)
    {
        await using var factory = GatewayFactory.AllClustersTo(_monolith);
        var client = factory.CreateClient();

        var response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty(_monolith.Requests);
    }
}
