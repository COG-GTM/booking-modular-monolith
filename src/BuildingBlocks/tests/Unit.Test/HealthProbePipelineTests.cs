using System.Net;
using System.Net.Http.Json;
using System.Text.Encodings.Web;
using System.Text.Json;
using BuildingBlocks.HealthCheck;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace BuildingBlocks.Unit.Test;

public class HealthProbePipelineTests
{
    [Theory]
    [InlineData("/health", true)]
    [InlineData("/health/live", true)]
    [InlineData("/health/ready", true)]
    [InlineData("/HEALTH/READY", true)]
    [InlineData("/alive", true)]
    [InlineData("/healthy", false)]
    [InlineData("/api/health", false)]
    [InlineData("/metrics", false)]
    [InlineData("/", false)]
    public void is_health_endpoint_matches_only_the_probe_paths(string path, bool expected) =>
        Extensions.IsHealthEndpoint(new PathString(path)).Should().Be(expected);

    [Fact]
    public async Task legacy_health_path_reports_readiness_while_alive_reports_liveness()
    {
        await using var app = await StartAppAsync(failingDependency: true);
        var client = app.GetTestClient();

        (await client.GetAsync(Extensions.AlivenessEndpointPath)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.GetAsync(Extensions.HealthEndpointPath))
            .StatusCode.Should()
            .Be(HttpStatusCode.ServiceUnavailable);
    }

    [Fact]
    public async Task readiness_payload_lists_every_check_with_its_status()
    {
        await using var app = await StartAppAsync(failingDependency: true);
        var client = app.GetTestClient();

        var response = await client.GetAsync(Extensions.ReadinessEndpointPath);

        response.Content.Headers.ContentType!.MediaType.Should().Be("application/json");
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        payload.GetProperty("status").GetString().Should().Be("Unhealthy");
        var entries = payload.GetProperty("entries");
        entries.GetProperty(Extensions.SelfCheckName).GetProperty("status").GetString().Should().Be("Healthy");
        entries.GetProperty("grpc-flight").GetProperty("status").GetString().Should().Be("Unhealthy");
        entries.GetProperty("grpc-flight").GetProperty("description").GetString().Should().Be("down");
    }

    [Fact]
    public async Task liveness_payload_is_the_plain_status_text()
    {
        await using var app = await StartAppAsync(failingDependency: true);
        var client = app.GetTestClient();

        var response = await client.GetAsync(Extensions.LivenessEndpointPath);

        (await response.Content.ReadAsStringAsync()).Should().Be("Healthy");
    }

    [Fact]
    public async Task probes_bypass_a_fallback_authorization_policy()
    {
        await using var app = await StartAppAsync(failingDependency: false, requireAuthentication: true);
        var client = app.GetTestClient();

        (await client.GetAsync("/api/secure")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        foreach (var path in new[] { "/health/live", "/health/ready", "/health", "/alive" })
            (await client.GetAsync(path)).StatusCode.Should().Be(HttpStatusCode.OK, path);
    }

    private static async Task<WebApplication> StartAppAsync(bool failingDependency, bool requireAuthentication = false)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Production" });
        builder.WebHost.UseTestServer();
        builder.Services.AddRouting();
        builder.Services.AddCustomHealthCheck();

        if (failingDependency)
        {
            builder
                .Services.AddHealthChecks()
                .AddCheck("grpc-flight", () => HealthCheckResult.Unhealthy("down"), [Extensions.ReadyTag]);
        }

        if (requireAuthentication)
        {
            builder
                .Services.AddAuthentication(DenyAllHandler.Scheme)
                .AddScheme<AuthenticationSchemeOptions, DenyAllHandler>(DenyAllHandler.Scheme, _ => { });
            builder.Services.AddAuthorization(options =>
                options.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build()
            );
        }

        var app = builder.Build();

        if (requireAuthentication)
        {
            app.UseAuthentication();
            app.UseAuthorization();
            app.MapGet("/api/secure", () => "secret");
        }

        app.UseCustomHealthCheck();
        await app.StartAsync();

        return app;
    }

    private sealed class DenyAllHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder
    ) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        public const string Scheme = "Deny";

        protected override Task<AuthenticateResult> HandleAuthenticateAsync() =>
            Task.FromResult(AuthenticateResult.NoResult());
    }
}
