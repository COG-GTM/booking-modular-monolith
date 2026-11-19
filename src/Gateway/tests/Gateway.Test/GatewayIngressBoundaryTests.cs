using System.Net;
using Xunit;

namespace Gateway.Test;

public class GatewayIngressBoundaryTests
{
    [Theory]
    [InlineData("/api/v1/flight", 401)]
    [InlineData("/", 200)]
    [InlineData("/health", 200)]
    public async Task actual_cors_responses_expose_correlation_even_when_authentication_rejects(string path, int status)
    {
        await using var backend = await EchoBackend.StartAsync("monolith");
        await using var factory = GatewayFactory.AllClustersTo(
            backend,
            new Dictionary<string, string?> { ["Cors:AllowedOrigins:0"] = "http://app.test" }
        );
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Add("Origin", "http://app.test");

        using var response = await client.SendAsync(request);

        Assert.Equal((HttpStatusCode)status, response.StatusCode);
        Assert.Equal("http://app.test", Assert.Single(response.Headers.GetValues("Access-Control-Allow-Origin")));
        Assert.Contains("correlationId", response.Headers.GetValues("Access-Control-Expose-Headers"));
        var correlationId = Assert.Single(response.Headers.GetValues("correlationId"));
        Assert.True(Guid.TryParseExact(correlationId, "N", out _));
        if (path == "/")
            Assert.Equal(correlationId, Assert.Single(backend.Requests).Headers["correlationId"]);
        else
            Assert.Empty(backend.Requests);
    }

    [Fact]
    public async Task disabling_rate_limiting_bypasses_the_configured_permit_limit()
    {
        await using var backend = await EchoBackend.StartAsync("monolith");
        await using var factory = GatewayFactory.AllClustersTo(
            backend,
            new Dictionary<string, string?>
            {
                ["RateLimiting:Enabled"] = "false",
                ["RateLimiting:PermitLimit"] = "1",
                ["RateLimiting:WindowSeconds"] = "600",
            }
        );
        using var client = factory.CreateClient();

        for (var i = 0; i < 3; i++)
        {
            using var response = await client.GetAsync("/");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.False(response.Headers.Contains("Retry-After"));
        }

        Assert.Equal(3, backend.Requests.Count);
    }
}
