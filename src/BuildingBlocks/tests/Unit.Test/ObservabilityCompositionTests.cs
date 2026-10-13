using System.Diagnostics;
using BuildingBlocks.OpenTelemetryCollector;
using FluentAssertions;
using MassTransit.Logging;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry;
using OpenTelemetry.Trace;
using Xunit;

namespace BuildingBlocks.Unit.Test;

public class ObservabilityCompositionTests
{
    private static async Task<WebApplication> StartAppAsync(
        string environment,
        Dictionary<string, string?>? overrides = null,
        Action<WebApplication>? configure = null
    )
    {
        var settings = new Dictionary<string, string?>
        {
            ["ObservabilityOptions:InstrumentationName"] = "booking-tests",
            ["ObservabilityOptions:ServiceName"] = "flight-service",
            ["ObservabilityOptions:LoggingEnabled"] = "false",
            ["ObservabilityOptions:MetricsEnabled"] = "false",
            ["ObservabilityOptions:UsePrometheusExporter"] = "false",
            ["ObservabilityOptions:UseOTLPExporter"] = "false",
            ["ObservabilityOptions:UseAspireOTLPExporter"] = "false",
        };

        foreach (var (key, value) in overrides ?? [])
            settings[key] = value;

        var builder = WebApplication.CreateBuilder(
            new WebApplicationOptions
            {
                EnvironmentName = environment,
                ApplicationName = typeof(ObservabilityCompositionTests).Assembly.GetName().Name,
            }
        );
        builder.Configuration.AddInMemoryCollection(settings);
        builder.WebHost.UseTestServer();
        builder.AddCustomObservability();

        var app = builder.Build();
        app.UseCustomObservability();
        configure?.Invoke(app);
        app.MapGet("/api/ping", () => "pong");
        app.MapGet("/health/live", () => "Healthy");
        app.MapGet("/alive", () => "Healthy");
        app.MapGet("/metrics", () => "# metrics");
        await app.StartAsync();

        return app;
    }

    private static IReadOnlyDictionary<string, object> ResourceAttributes(WebApplication app) =>
        app
            .Services.GetRequiredService<TracerProvider>()
            .GetResource()
            .Attributes.ToDictionary(a => a.Key, a => a.Value);

    [Theory]
    [InlineData("Development")]
    [InlineData("Production")]
    public async Task resource_carries_the_configured_service_name_and_deployment_environment(string environment)
    {
        await using var app = await StartAppAsync(environment);

        var attributes = ResourceAttributes(app);

        attributes["service.name"].Should().Be("flight-service");
        attributes["service.environment"].Should().Be(environment);
        attributes["deployment.environment.name"].Should().Be(environment);
        attributes.Should().ContainKey("service.instance.id");
    }

    [Fact]
    public async Task otel_service_name_environment_variable_overrides_the_configured_service_name()
    {
        await using var app = await StartAppAsync(
            "Production",
            new() { [Extensions.ServiceNameEnvironmentVariable] = "booking-service" }
        );

        ResourceAttributes(app)["service.name"].Should().Be("booking-service");
    }

    [Theory]
    [InlineData(Extensions.YarpActivitySourceName)]
    [InlineData(Extensions.PersistMessageActivitySourceName)]
    [InlineData(DiagnosticHeaders.DefaultListenerName)]
    [InlineData("booking-tests")]
    public async Task tracing_listens_to_the_gateway_outbox_and_bus_activity_sources(string sourceName)
    {
        await using var app = await StartAppAsync("Production");
        using var source = new ActivitySource(sourceName);

        using var activity = source.StartActivity("probe");

        activity.Should().NotBeNull(sourceName);
        activity!.Recorded.Should().BeTrue();
    }

    [Fact]
    public async Task outbox_publish_activity_source_name_matches_the_processor_constant()
    {
        Extensions
            .PersistMessageActivitySourceName.Should()
            .Be(PersistMessageProcessor.PersistMessageProcessor.ActivitySourceName);
    }

    [Theory]
    [InlineData("/health/live", false)]
    [InlineData("/alive", false)]
    [InlineData("/metrics", false)]
    [InlineData("/api/ping", true)]
    public async Task server_spans_are_recorded_for_business_requests_but_not_for_infrastructure_endpoints(
        string path,
        bool expectRecorded
    )
    {
        Activity? server = null;
        await using var app = await StartAppAsync(
            "Production",
            configure: app =>
                app.Use(
                    (context, next) =>
                    {
                        server = context.Features.Get<IHttpActivityFeature>()?.Activity;
                        return next(context);
                    }
                )
        );

        var response = await app.GetTestClient().GetAsync(path);

        response.EnsureSuccessStatusCode();
        server.Should().NotBeNull(path);
        server!.Recorded.Should().Be(expectRecorded, path);
        server.IsAllDataRequested.Should().Be(expectRecorded, path);
    }
}
