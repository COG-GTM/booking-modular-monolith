using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace GatewayService.Unit.Test;

public class GatewayProxyForwardingTests : IAsyncLifetime
{
    private WebApplication _backend = null!;
    private WebApplicationFactory<Program> _factory = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        var backendBuilder = WebApplication.CreateBuilder();
        backendBuilder.WebHost.UseUrls("http://127.0.0.1:0");
        _backend = backendBuilder.Build();
        _backend.MapGet(
            "/api/{version}/flight/{**catchAll}",
            (string version, string catchAll) => Results.Ok($"flight:{version}:{catchAll}"));
        await _backend.StartAsync();

        var backendAddress = _backend.Urls.Single();

        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.UseSetting(
                "ReverseProxy:Clusters:flight-cluster:Destinations:flight-service:Address",
                backendAddress));
        _client = _factory.CreateClient();
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
        await _backend.DisposeAsync();
    }

    [Fact]
    public async Task gateway_should_forward_matching_request_to_cluster_destination()
    {
        var response = await _client.GetAsync(new Uri("/api/v1/flight/get-available-flights", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("flight:v1:get-available-flights");
    }

    [Fact]
    public async Task gateway_should_return_not_found_for_unmatched_path()
    {
        var response = await _client.GetAsync(new Uri("/api/v1/unknown/route", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
