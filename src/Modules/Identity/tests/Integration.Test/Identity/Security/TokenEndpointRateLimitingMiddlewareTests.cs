using System.Net;
using FluentAssertions;
using Identity.Configurations;
using Identity.Extensions.Infrastructure;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Integration.Test.Identity.Security;

public class TokenEndpointRateLimitingMiddlewareTests
{
    [Theory]
    [InlineData("/connect/token")]
    [InlineData("/CONNECT/TOKEN")]
    [InlineData("/connect/token/subpath")]
    public async Task token_paths_share_the_configured_limit(string path)
    {
        await using var app = await CreateApp();
        using var client = app.GetTestClient();

        for (var attempt = 0; attempt < 2; attempt++)
        {
            using var response = await client.PostAsync(path, null);
            response.StatusCode.Should().Be(HttpStatusCode.OK);
        }

        using var rejected = await client.PostAsync("/connect/token", null);
        rejected.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
    }

    [Theory]
    [InlineData("/api/users")]
    [InlineData("/connect/tokenize")]
    [InlineData("/")]
    public async Task other_paths_are_not_limited_even_after_token_partition_is_exhausted(string path)
    {
        await using var app = await CreateApp();
        using var client = app.GetTestClient();
        for (var attempt = 0; attempt < 3; attempt++)
        {
            using var tokenResponse = await client.PostAsync("/connect/token", null);
        }

        for (var attempt = 0; attempt < 5; attempt++)
        {
            using var response = await client.GetAsync(path);
            response.StatusCode.Should().Be(HttpStatusCode.OK);
        }
    }

    [Fact]
    public async Task separate_client_ips_have_independent_partitions()
    {
        await using var app = await CreateApp();
        using var client = app.GetTestClient();
        foreach (var ip in new[] { "192.0.2.1", "192.0.2.2" })
        {
            for (var attempt = 0; attempt < 3; attempt++)
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, "/connect/token");
                request.Headers.Add("X-Test-Client-IP", ip);
                using var response = await client.SendAsync(request);
                response.StatusCode.Should().Be(attempt < 2 ? HttpStatusCode.OK : HttpStatusCode.TooManyRequests);
            }
        }
    }

    [Theory]
    [InlineData(0, 3600, nameof(TokenEndpointRateLimitOptions.PermitLimit))]
    [InlineData(2, 0, nameof(TokenEndpointRateLimitOptions.WindowSeconds))]
    public void invalid_settings_fail_when_resolved(int permitLimit, int windowSeconds, string invalidProperty)
    {
        var builder = CreateBuilder(permitLimit, windowSeconds);
        using var provider = builder.Services.BuildServiceProvider();

        var resolve = () => provider.GetRequiredService<TokenEndpointRateLimitOptions>();

        resolve
            .Should()
            .Throw<OptionsValidationException>()
            .Which.Failures.Should()
            .Contain(failure => failure.Contains(invalidProperty, StringComparison.Ordinal));
    }

    private static WebApplicationBuilder CreateBuilder(int permitLimit = 2, int windowSeconds = 3600)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.WebHost.UseTestServer();
        builder.Configuration.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["TokenEndpointRateLimitOptions:PermitLimit"] = permitLimit.ToString(),
                ["TokenEndpointRateLimitOptions:WindowSeconds"] = windowSeconds.ToString(),
            }
        );
        builder.AddTokenEndpointRateLimiting();
        return builder;
    }

    private static async Task<WebApplication> CreateApp()
    {
        var app = CreateBuilder().Build();
        app.Use(
            async (context, next) =>
            {
                if (context.Request.Headers.TryGetValue("X-Test-Client-IP", out var ip))
                {
                    context.Connection.RemoteIpAddress = IPAddress.Parse(ip.ToString());
                }

                await next(context);
            }
        );
        app.UseRateLimiter();
        app.Run(context => context.Response.WriteAsync("OK"));
        await app.StartAsync();
        return app;
    }
}
