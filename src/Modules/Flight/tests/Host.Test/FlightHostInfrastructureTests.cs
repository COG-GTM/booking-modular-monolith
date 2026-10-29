using System.Net;
using BuildingBlocks.Mongo;
using Flight;
using Flight.Host.Extensions;
using FluentAssertions;
using Grpc.AspNetCore.HealthChecks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Xunit;

namespace Host.Test;

public class FlightHostInfrastructureTests
{
    [Fact]
    public async Task grpc_health_mappings_use_readiness_tags_for_both_services()
    {
        var builder = CreateBuilder();
        await using var provider = builder.Services.BuildServiceProvider();
        var mappings = provider.GetRequiredService<IOptions<GrpcHealthChecksOptions>>().Value.Services.ToArray();

        mappings.Select(mapping => mapping.Name).Should().BeEquivalentTo(["", FlightGrpcService.Descriptor.FullName]);
        foreach (var mapping in mappings)
        {
            mapping.HealthCheckPredicate.Should().NotBeNull();
            var predicate = mapping.HealthCheckPredicate!;

            predicate(new HealthCheckMapContext("check", ["live"])).Should().BeFalse();
            predicate(new HealthCheckMapContext("check", ["ready"])).Should().BeTrue();
            predicate(new HealthCheckMapContext("check", [])).Should().BeTrue();
        }
    }

    [Fact]
    public async Task readiness_checks_are_registered_by_flight_host_infrastructure()
    {
        var builder = CreateBuilder();
        await using var provider = builder.Services.BuildServiceProvider();
        var names = provider
            .GetRequiredService<IOptions<HealthCheckServiceOptions>>()
            .Value.Registrations.Select(registration => registration.Name);

        names.Should().Contain(["flight-postgres", "flight-outbox-postgres", "flight-mongo", "rabbitmq"]);
    }

    [Theory]
    [InlineData("custom-flight", "custom-flight")]
    [InlineData(null, "Flight-Service")]
    public async Task root_endpoint_returns_configured_or_default_service_name(string? name, string expected)
    {
        var builder = CreateBuilder("Production", name is null ? [] : [new("AppOptions:Name", name)]);
        await using var app = builder.Build();
        app.UseFlightHostInfrastructure();
        await app.StartAsync();
        using var client = app.GetTestClient();

        using var response = await client.GetAsync("/");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().Be(expected);
    }

    [Theory]
    [InlineData("Development", HttpStatusCode.OK)]
    [InlineData("Production", HttpStatusCode.NotFound)]
    public async Task openapi_document_is_mapped_only_in_development(string environment, HttpStatusCode expectedStatus)
    {
        var builder = CreateBuilder(environment);
        await using var app = builder.Build();
        app.UseFlightHostInfrastructure();
        await app.StartAsync();
        using var client = app.GetTestClient();

        using var response = await client.GetAsync("/openapi/v1.json");

        response.StatusCode.Should().Be(expectedStatus);
    }

    private static WebApplicationBuilder CreateBuilder(
        string environment = "Production",
        IEnumerable<KeyValuePair<string, string?>>? additionalConfiguration = null
    )
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = environment });
        builder.WebHost.UseTestServer();
        builder.Configuration.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["MessagingOptions:Transport"] = "InMemory",
                ["Jwt:Authority"] = "http://localhost:5000",
                ["Jwt:Audience"] = "booking-modular-monolith",
                ["MongoOptions:ConnectionString"] = "mongodb://localhost:27017",
                ["MongoOptions:Flight:DatabaseName"] = "flight_service_read",
                ["ObservabilityOptions:InstrumentationName"] = "flight-host-test",
                ["ObservabilityOptions:MetricsEnabled"] = "false",
                ["ObservabilityOptions:TracingEnabled"] = "false",
                ["ObservabilityOptions:LoggingEnabled"] = "false",
                ["ObservabilityOptions:UsePrometheusExporter"] = "false",
                ["ObservabilityOptions:UseOTLPExporter"] = "false",
                ["ObservabilityOptions:UseAspireOTLPExporter"] = "false",
                ["ObservabilityOptions:UseGrafanaExporter"] = "false",
                ["ObservabilityOptions:UseJaegerExporter"] = "false",
                ["ObservabilityOptions:UseZipkinExporter"] = "false",
                ["ObservabilityOptions:UseConsoleExporter"] = "false",
                ["HealthOptions:Enabled"] = "false",
            }
        );
        if (additionalConfiguration is not null)
            builder.Configuration.AddInMemoryCollection(additionalConfiguration);
        builder
            .Services.AddOptions<MongoOptions>("Flight")
            .Configure<IConfiguration>(
                (options, configuration) =>
                {
                    options.DatabaseName = configuration["MongoOptions:Flight:DatabaseName"]!;
                    options.ConnectionString = configuration["MongoOptions:ConnectionString"]!;
                }
            );
        builder.AddFlightHostInfrastructure();
        return builder;
    }
}
