using System.Net;
using System.Net.Http.Headers;
using Xunit;

namespace Gateway.Test;

public class GatewayPartitioningTests
{
    [Theory]
    [InlineData(null, HttpStatusCode.Unauthorized)]
    [InlineData("other", HttpStatusCode.Forbidden)]
    public async Task rejected_api_requests_are_rate_limited(string? scope, HttpStatusCode initialStatus)
    {
        await using var backend = await EchoBackend.StartAsync("monolith");
        await using var factory = GatewayFactory.AllClustersTo(
            backend,
            new Dictionary<string, string?>
            {
                ["RateLimiting:PermitLimit"] = "2",
                ["RateLimiting:WindowSeconds"] = "600",
            }
        );
        using var client = factory.CreateClient();
        if (scope is not null)
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
                "Bearer",
                GatewayFactory.CreateToken(scope: scope)
            );

        for (var i = 0; i < 2; i++)
        {
            using var response = await client.GetAsync("/api/v1/flight/get-available-flights");
            Assert.Equal(initialStatus, response.StatusCode);
        }

        using var limited = await client.GetAsync("/api/v1/flight/get-available-flights");
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.Empty(backend.Requests);
    }

    [Fact]
    public async Task authenticated_subjects_have_separate_windows_even_on_the_same_connection()
    {
        await using var backend = await EchoBackend.StartAsync("monolith");
        await using var factory = GatewayFactory.AllClustersTo(
            backend,
            new Dictionary<string, string?>
            {
                ["RateLimiting:PermitLimit"] = "1",
                ["RateLimiting:WindowSeconds"] = "600",
            }
        );
        using var client = factory.CreateClient();

        Assert.Equal(HttpStatusCode.OK, await Send("alice"));
        Assert.Equal(HttpStatusCode.TooManyRequests, await Send("alice"));
        Assert.Equal(HttpStatusCode.OK, await Send("bob"));
        Assert.Equal(HttpStatusCode.TooManyRequests, await Send("bob"));
        Assert.Equal(2, backend.Requests.Count);

        async Task<HttpStatusCode> Send(string subject)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/flight/get-available-flights");
            request.Headers.Authorization = new AuthenticationHeaderValue(
                "Bearer",
                GatewayFactory.CreateToken(subject: subject)
            );
            using var response = await client.SendAsync(request);
            return response.StatusCode;
        }
    }
}
