using BuildingBlocks.Web;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Xunit;

namespace BuildingBlocks.Unit.Test.WebTests;

public class CorrelationExtensionsTests
{
    private static async Task<(WebApplication App, HttpClient Client)> StartApp()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Development" });
        builder.WebHost.UseTestServer();
        var app = builder.Build();
        app.UseCorrelationId();
        app.MapGet("/bb-correlation", (HttpContext ctx) => ctx.GetCorrelationId().ToString("N"));
        await app.StartAsync();
        return (app, app.GetTestClient());
    }

    [Fact]
    public async Task incoming_correlation_id_header_is_echoed_back()
    {
        var (app, client) = await StartApp();
        await using (app)
        {
            var id = Guid.NewGuid();
            var request = new HttpRequestMessage(HttpMethod.Get, "/bb-correlation");
            request.Headers.TryAddWithoutValidation("correlationId", id.ToString("N"));

            var response = await client.SendAsync(request);

            (await response.Content.ReadAsStringAsync()).Should().Be(id.ToString("N"));
        }
    }

    [Fact]
    public async Task missing_header_generates_a_non_empty_correlation_id()
    {
        var (app, client) = await StartApp();
        await using (app)
        {
            var response = await client.GetAsync("/bb-correlation");

            var body = await response.Content.ReadAsStringAsync();
            Guid.Parse(body).Should().NotBeEmpty();
        }
    }

    [Fact]
    public async Task two_requests_without_a_header_get_different_ids()
    {
        var (app, client) = await StartApp();
        await using (app)
        {
            var first = await (await client.GetAsync("/bb-correlation")).Content.ReadAsStringAsync();
            var second = await (await client.GetAsync("/bb-correlation")).Content.ReadAsStringAsync();

            first.Should().NotBe(second);
        }
    }

    [Fact]
    public void get_correlation_id_on_a_bare_context_returns_a_non_empty_guid()
    {
        var context = new DefaultHttpContext();

        context.GetCorrelationId().Should().NotBeEmpty();
    }
}
