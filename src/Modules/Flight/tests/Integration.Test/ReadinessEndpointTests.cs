using System.Net;
using Api.Extensions;
using Booking.Extensions.Infrastructure;
using BuildingBlocks.OpenTelemetryCollector;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Xunit;

namespace Integration.Test;

public class ReadinessEndpointTests
{
    [Theory]
    [InlineData("Production", "none", HttpStatusCode.OK)]
    [InlineData("Production", "database", HttpStatusCode.ServiceUnavailable)]
    [InlineData("Production", "grpc", HttpStatusCode.ServiceUnavailable)]
    [InlineData("docker", "none", HttpStatusCode.OK)]
    [InlineData("docker", "database", HttpStatusCode.ServiceUnavailable)]
    [InlineData("docker", "grpc", HttpStatusCode.ServiceUnavailable)]
    [InlineData("Staging", "none", HttpStatusCode.OK)]
    [InlineData("Staging", "database", HttpStatusCode.ServiceUnavailable)]
    [InlineData("Staging", "grpc", HttpStatusCode.ServiceUnavailable)]
    public async Task readiness_is_mapped_outside_development_and_checks_grpc_and_untagged_dependencies(
        string environment,
        string failingDependency,
        HttpStatusCode expected
    )
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = environment });
        builder.WebHost.UseTestServer();
        builder.Configuration.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["AppOptions:Name"] = "readiness-test",
                ["HealthOptions:Enabled"] = "false",
            }
        );
        builder.Services.AddProblemDetails();
        builder.Services.Configure<ObservabilityOptions>(options => options.UsePrometheusExporter = false);
        builder.Services.AddGrpc();
        builder.Services.AddGrpcHealthChecks();
        var grpcCalls = 0;
        var databaseCalls = 0;
        builder
            .Services.AddHealthChecks()
            .AddCheck(
                "flight-grpc",
                () =>
                {
                    Interlocked.Increment(ref grpcCalls);
                    return failingDependency == "grpc"
                        ? HealthCheckResult.Unhealthy("gRPC unavailable")
                        : HealthCheckResult.Healthy();
                },
                [GrpcClientExtensions.ReadyHealthTag, GrpcClientExtensions.GrpcDependencyHealthTag]
            )
            .AddCheck(
                "database",
                () =>
                {
                    Interlocked.Increment(ref databaseCalls);
                    return failingDependency == "database"
                        ? HealthCheckResult.Unhealthy("database unavailable")
                        : HealthCheckResult.Healthy();
                }
            )
            .AddCheck("live-only", () => HealthCheckResult.Unhealthy(), ["live"]);
        await using var app = builder.Build();
        app.UserSharedInfrastructure();
        await app.StartAsync();
        using var client = app.GetTestClient();

        using var response = await client.GetAsync("/health/ready");

        response.StatusCode.Should().Be(expected);
        grpcCalls.Should().BeGreaterThan(0);
        databaseCalls.Should().BeGreaterThan(0);
        using var legacy = await client.GetAsync("/health");
        legacy.StatusCode.Should().Be(HttpStatusCode.NotFound);
        using var alive = await client.GetAsync("/alive");
        alive.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
