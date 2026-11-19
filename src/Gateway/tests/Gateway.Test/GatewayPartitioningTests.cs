using System.Net;
using System.Net.Http.Headers;
using Xunit;

namespace Gateway.Test;

public class GatewayPartitioningTests
{
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
