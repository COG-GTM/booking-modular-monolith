using System.Net;
using BuildingBlocks.HealthCheck;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Xunit;

namespace Unit.Test.HealthCheck;

// UseCustomHealthCheck maps three probes over the same registrations: /health runs everything,
// /alive only the "live"-tagged checks and /ready only the "ready"-tagged checks.
public class ReadinessEndpointTests
{
    [Fact]
    public async Task ready_probe_only_evaluates_ready_tagged_checks()
    {
        await using var app = await StartAppAsync(readyStoreStatus: HealthStatus.Healthy);
        using var client = app.GetTestClient();

        using var ready = await client.GetAsync("/ready");
        using var alive = await client.GetAsync("/alive");
        using var health = await client.GetAsync("/health");

        ready.StatusCode.Should().Be(HttpStatusCode.OK);
        (await ready.Content.ReadAsStringAsync()).Should().Be("Healthy");
        alive.StatusCode.Should().Be(HttpStatusCode.OK);
        (await alive.Content.ReadAsStringAsync()).Should().Be("Healthy");
        health.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        (await health.Content.ReadAsStringAsync()).Should().Be("Unhealthy");
    }

    [Fact]
    public async Task unhealthy_ready_check_fails_readiness_but_not_liveness()
    {
        await using var app = await StartAppAsync(readyStoreStatus: HealthStatus.Unhealthy);
        using var client = app.GetTestClient();

        using var ready = await client.GetAsync("/ready");
        using var alive = await client.GetAsync("/alive");

        ready.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        (await ready.Content.ReadAsStringAsync()).Should().Be("Unhealthy");
        alive.StatusCode.Should().Be(HttpStatusCode.OK);
        (await alive.Content.ReadAsStringAsync()).Should().Be("Healthy");
    }

    [Fact]
    public async Task ready_probe_reports_healthy_when_no_check_is_tagged_ready()
    {
        var builder = CreateBuilder();
        builder.Services.AddHealthChecks().AddCheck("self", () => HealthCheckResult.Healthy(), ["live"]);
        await using var app = builder.Build();
        app.UseCustomHealthCheck();
        await app.StartAsync();
        using var client = app.GetTestClient();

        using var ready = await client.GetAsync("/ready");

        ready.StatusCode.Should().Be(HttpStatusCode.OK);
        (await ready.Content.ReadAsStringAsync()).Should().Be("Healthy");
    }

    private static async Task<WebApplication> StartAppAsync(HealthStatus readyStoreStatus)
    {
        var builder = CreateBuilder();
        builder
            .Services.AddHealthChecks()
            .AddCheck("self", () => HealthCheckResult.Healthy(), ["live"])
            .AddCheck("store", () => new HealthCheckResult(readyStoreStatus), ["ready"])
            .AddCheck("downstream", () => HealthCheckResult.Unhealthy(), tags: []);

        var app = builder.Build();
        app.UseCustomHealthCheck();
        await app.StartAsync();
        return app;
    }

    private static WebApplicationBuilder CreateBuilder()
    {
        var builder = WebApplication.CreateBuilder(
            new WebApplicationOptions
            {
                EnvironmentName = "test",
                ApplicationName = typeof(ReadinessEndpointTests).Assembly.GetName().Name,
            }
        );
        builder.WebHost.UseTestServer();
        builder.Configuration.AddInMemoryCollection(
            new Dictionary<string, string?> { ["HealthOptions:Enabled"] = "false" }
        );
        return builder;
    }
}
