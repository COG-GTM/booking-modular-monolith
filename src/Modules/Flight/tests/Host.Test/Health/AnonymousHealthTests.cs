using System.Net;
using BuildingBlocks.OpenTelemetryCollector;
using Flight.Host.Extensions;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Xunit;

namespace Flight.Host.Test.Health;

public class AnonymousHealthTests
{
    [Theory]
    [InlineData("/health/live", false, HttpStatusCode.OK)]
    [InlineData("/health/ready", false, HttpStatusCode.OK)]
    [InlineData("/health/live", true, HttpStatusCode.OK)]
    [InlineData("/health/ready", true, HttpStatusCode.ServiceUnavailable)]
    public async Task Health_remains_anonymous_under_fallback_authorization(
        string path,
        bool failingReady,
        HttpStatusCode expected
    )
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.WebHost.UseTestServer();
        builder.Services.Configure<ObservabilityOptions>(options => options.UsePrometheusExporter = false);
        builder.Services.AddProblemDetails();
        builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
        builder.Services.AddAuthorization(options =>
            options.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build()
        );
        builder.Services.AddGrpc();
        builder.Services.AddGrpcHealthChecks();
        builder
            .Services.AddHealthChecks()
            .AddCheck("self", () => HealthCheckResult.Healthy(), tags: ["live"])
            .AddCheck(
                "dependency",
                () => failingReady ? HealthCheckResult.Unhealthy() : HealthCheckResult.Healthy(),
                tags: ["ready"]
            );
        await using var app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();
        app.UseFlightHostInfrastructure();
        app.MapGet("/protected", () => "protected");
        await app.StartAsync();
        using var client = app.GetTestClient();
        using var protectedResponse = await client.GetAsync("/protected");
        Assert.Equal(HttpStatusCode.Unauthorized, protectedResponse.StatusCode);
        using var response = await client.GetAsync(path);
        Assert.Equal(expected, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
    }
}
