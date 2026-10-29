using System.Net;
using BuildingBlocks.HealthCheck;
using BuildingBlocks.OpenApi;
using BuildingBlocks.OpenTelemetryCollector;
using BuildingBlocks.Web;
using FluentAssertions;
using Identity.Host.Extensions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Xunit;

namespace Host.Test;

public class IdentityHostInfrastructureRoutingTests
{
    [Theory]
    [InlineData("Development")]
    [InlineData("Production")]
    [InlineData("docker")]
    public async Task health_routes_are_mapped_once_and_liveness_ignores_failed_dependencies(string environment)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = environment });
        builder.Configuration.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["AppOptions:Name"] = "standalone-identity-test",
                ["HealthOptions:Enabled"] = "false",
                ["HealthOptions:UIEnabled"] = "false",
            }
        );
        builder.WebHost.UseTestServer();
        builder.Services.AddCustomHealthCheck();
        builder.Services.AddHealthChecks().AddCheck("dependency", () => HealthCheckResult.Unhealthy());
        builder.Services.Configure<ObservabilityOptions>(options => options.UsePrometheusExporter = false);
        builder.Services.AddProblemDetails();
        builder.Services.AddEndpointsApiExplorer();
        builder.Services.AddCustomVersioning();
        builder.Services.AddAspnetOpenApi();

        await using var app = builder.Build();
        app.UseIdentityHostInfrastructure();
        await app.StartAsync();
        using var client = app.GetTestClient();

        using var alive = await client.GetAsync("/alive");
        alive.StatusCode.Should().Be(HttpStatusCode.OK);
        (await alive.Content.ReadAsStringAsync()).Should().Be("Healthy");

        using var health = await client.GetAsync("/health");
        health.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        (await health.Content.ReadAsStringAsync()).Should().Be("Unhealthy");

        (await client.GetStringAsync("/")).Should().Be("standalone-identity-test");
        using var ui = await client.GetAsync("/health-ui");
        ui.StatusCode.Should().Be(HttpStatusCode.NotFound);

        var routes = ((IEndpointRouteBuilder)app)
            .DataSources.SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .Select(endpoint => endpoint.RoutePattern.RawText)
            .ToArray();
        routes.Should().ContainSingle(route => route == "/health");
        routes.Should().ContainSingle(route => route == "/alive");
    }
}
