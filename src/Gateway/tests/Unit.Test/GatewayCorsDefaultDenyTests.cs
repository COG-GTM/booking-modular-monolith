using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Gateway.Unit.Test;

public class GatewayCorsDefaultDenyTests : IAsyncLifetime
{
    private WebApplicationFactory<Program> _factory = null!;

    public Task InitializeAsync()
    {
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder => builder.UseEnvironment("test"));

        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        _factory.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task preflight_is_not_allowed_when_no_origins_are_configured()
    {
        var request = new HttpRequestMessage(HttpMethod.Options, "/api/v1/flight");
        request.Headers.Add("Origin", "https://app.example.com");
        request.Headers.Add("Access-Control-Request-Method", "GET");

        var response = await _factory.CreateClient().SendAsync(request);

        Assert.False(response.Headers.Contains("Access-Control-Allow-Origin"));
        Assert.False(response.Headers.Contains("Access-Control-Allow-Credentials"));
    }
}
