using System.Net;
using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Gateway.Tests;

public class ProxyBehaviorTests
{
    [Theory]
    [InlineData("/connect/token", "identity")]
    [InlineData("/.well-known/openid-configuration", "identity")]
    [InlineData("/api/v1/flight/search", "flight")]
    [InlineData("/api/v1/identity/users", "identity")]
    [InlineData("/api/v1/passenger/1", "passenger")]
    [InlineData("/api/v1/booking/1", "booking")]
    public async Task RequestsReachTheirConfiguredCluster(string path, string cluster)
    {
        await using var fixture = await ProxyFixture.CreateAsync();
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        if (path.StartsWith("/api/", StringComparison.Ordinal))
            request.Headers.Add("Test-Subject", "user");
        using var response = await fixture.Client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal($"/clusters/{cluster}{path}", await response.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData("/some/unknown/path")]
    [InlineData("/")]
    public async Task UnknownPathsReturnNotFound(string path)
    {
        await using var fixture = await ProxyFixture.CreateAsync();
        using var response = await fixture.Client.GetAsync(path);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [InlineData(
        "4d4d8113-18f9-42c4-bcaf-b64308996e61",
        "b5838f28-8df8-4bba-ae2c-f7f7fcff8ded",
        "4d4d8113-18f9-42c4-bcaf-b64308996e61"
    )]
    [InlineData("invalid", "b5838f28-8df8-4bba-ae2c-f7f7fcff8ded", "b5838f28-8df8-4bba-ae2c-f7f7fcff8ded")]
    [InlineData("{4D4D8113-18F9-42C4-BCAF-B64308996E61}", "invalid", "4d4d8113-18f9-42c4-bcaf-b64308996e61")]
    [InlineData(" ", " ", null)]
    public async Task CorrelationHeadersUseBackendPrecedenceAndCanonicalGuid(
        string backendHeader,
        string publicHeader,
        string? expected
    )
    {
        await using var fixture = await ProxyFixture.CreateAsync();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/connect/token");
        request.Headers.TryAddWithoutValidation("correlationId", backendHeader);
        request.Headers.TryAddWithoutValidation("X-Correlation-ID", publicHeader);
        using var response = await fixture.Client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var returned = Assert.Single(response.Headers.GetValues("X-Correlation-ID"));
        Assert.True(Guid.TryParseExact(returned, "D", out _));
        Assert.Equal(returned, Assert.Single(response.Headers.GetValues("correlationId")));
        Assert.Equal(returned, Assert.Single(response.Headers.GetValues("Upstream-Correlation")));
        Assert.Equal(returned, Assert.Single(response.Headers.GetValues("Upstream-Backend-Correlation")));
        if (expected is not null)
            Assert.Equal(expected, returned);
    }

    [Theory]
    [InlineData("/connect/token")]
    [InlineData("/.well-known/openid-configuration")]
    [InlineData("/api/v1/identity/users")]
    public async Task IdentityRequestsForwardThePublicHost(string path)
    {
        await using var fixture = await ProxyFixture.CreateAsync();
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Host = "public.example:5000";
        if (path.StartsWith("/api/", StringComparison.Ordinal))
            request.Headers.Add("Test-Subject", "user");
        using var response = await fixture.Client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("public.example:5000", Assert.Single(response.Headers.GetValues("Upstream-Host")));
    }

    [Fact]
    public async Task SubjectsHaveSeparateLimitsAndKeepTheirLimitAcrossAddresses()
    {
        await using var fixture = await ProxyFixture.CreateAsync(1);
        Assert.Equal(HttpStatusCode.OK, await fixture.SendAsync("alice", "192.0.2.1"));
        Assert.Equal(HttpStatusCode.OK, await fixture.SendAsync("bob", "192.0.2.1"));
        Assert.Equal(HttpStatusCode.TooManyRequests, await fixture.SendAsync("alice", "192.0.2.2"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RequestsWithoutSubjectShareOnlyTheirAddressLimit(bool authenticated)
    {
        await using var fixture = await ProxyFixture.CreateAsync(1);
        var subject = authenticated ? "" : null;
        Assert.Equal(HttpStatusCode.OK, await fixture.SendAsync(subject, "192.0.2.1"));
        Assert.Equal(HttpStatusCode.TooManyRequests, await fixture.SendAsync(subject, "192.0.2.1"));
        Assert.Equal(HttpStatusCode.OK, await fixture.SendAsync(subject, "192.0.2.2"));
    }

    private sealed class ProxyFixture(WebApplication upstream, WebApplicationFactory<Program> gateway)
        : IAsyncDisposable
    {
        public HttpClient Client { get; } = gateway.CreateClient();

        public static async Task<ProxyFixture> CreateAsync(int permitLimit = 100)
        {
            var builder = WebApplication.CreateBuilder();
            builder.WebHost.UseKestrel().UseUrls("http://127.0.0.1:0");
            var upstream = builder.Build();
            upstream.Run(async context =>
            {
                context.Response.Headers["Upstream-Correlation"] = context.Request.Headers["X-Correlation-ID"];
                context.Response.Headers["Upstream-Backend-Correlation"] = context.Request.Headers["correlationId"];
                context.Response.Headers["Upstream-Host"] = context.Request.Host.ToString();
                await context.Response.WriteAsync(context.Request.Path);
            });
            await upstream.StartAsync();
            var settings = new Dictionary<string, string?> { ["RateLimiting:PermitLimit"] = permitLimit.ToString() };
            foreach (var cluster in new[] { "identity", "flight", "passenger", "booking" })
                settings[$"ReverseProxy:Clusters:{cluster}:Destinations:primary:Address"] =
                    $"{upstream.Urls.Single()}/clusters/{cluster}/";
            var gateway = new WebApplicationFactory<Program>().WithWebHostBuilder(host =>
            {
                host.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(settings));
                host.ConfigureServices(services => services.AddSingleton<IStartupFilter, RequestIdentityFilter>());
            });
            return new ProxyFixture(upstream, gateway);
        }

        public async Task<HttpStatusCode> SendAsync(string? subject, string address)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "/connect/token");
            if (subject is not null)
                request.Headers.Add("Test-Subject", subject.Length == 0 ? "no-sub" : subject);
            request.Headers.Add("Test-Address", address);
            using var response = await Client.SendAsync(request);
            return response.StatusCode;
        }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await gateway.DisposeAsync();
            await upstream.DisposeAsync();
        }
    }

    private sealed class RequestIdentityFilter : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) =>
            app =>
            {
                app.Use(
                    async (context, continuation) =>
                    {
                        var subject = context.Request.Headers["Test-Subject"].ToString();
                        if (subject.Length > 0)
                            context.User = new ClaimsPrincipal(
                                new ClaimsIdentity(
                                    subject == "no-sub" ? [] : new[] { new Claim("sub", subject) },
                                    "test"
                                )
                            );
                        if (IPAddress.TryParse(context.Request.Headers["Test-Address"].ToString(), out var address))
                            context.Connection.RemoteIpAddress = address;
                        await continuation(context);
                    }
                );
                next(app);
            };
    }
}
