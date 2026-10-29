using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Net;
using System.Text.Json;
using BuildingBlocks.Core.Event;
using BuildingBlocks.HealthCheck;
using BuildingBlocks.OpenTelemetryCollector;
using BuildingBlocks.PersistMessageProcessor;
using FluentAssertions;
using MassTransit;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.Extensions;
using OpenTelemetry;
using OpenTelemetry.Trace;
using Xunit;
using HealthExtensions = BuildingBlocks.HealthCheck.Extensions;
using ObservabilityExtensions = BuildingBlocks.OpenTelemetryCollector.Extensions;

namespace BuildingBlocks.Tests;

public class ObservabilityWiringTests
{
    [Theory]
    [InlineData("from-env", "from-options", "from-env")]
    [InlineData(null, "from-options", "from-options")]
    [InlineData(null, null, "App.Name")]
    [InlineData("", " ", "App.Name")]
    public void service_name_prefers_otel_env_then_options_then_application_name(
        string? otelServiceName,
        string? configured,
        string expected
    )
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?> { [ObservabilityExtensions.OtelServiceNameKey] = otelServiceName }
            )
            .Build();
        var environment = Substitute.For<IHostEnvironment>();
        environment.ApplicationName.Returns("App.Name");

        ObservabilityExtensions
            .ResolveServiceName(configuration, new ObservabilityOptions { ServiceName = configured }, environment)
            .Should()
            .Be(expected);
    }

    [Fact]
    public void legacy_misspelt_otlp_key_still_binds_the_collector_endpoint()
    {
        var options = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["ObservabilityOptions:OTLPOptions:OTLPGrpExporterEndpoint"] = "http://otel-collector:4317",
                }
            )
            .Build()
            .GetSection(nameof(ObservabilityOptions))
            .Get<ObservabilityOptions>()!;

        options.OTLPOptions.OTLPGrpcExporterEndpoint.Should().Be("http://otel-collector:4317");
    }

    [Fact]
    public void otel_exporter_endpoint_env_overrides_the_configured_collector()
    {
        var options = new ObservabilityOptions { OTLPOptions = new OTLPOptions() };
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    [ObservabilityExtensions.OtelExporterEndpointKey] = "http://collector:4317",
                }
            )
            .Build();

        ObservabilityExtensions.ApplyOtlpEndpointOverride(configuration, options);

        options.OTLPOptions.OTLPGrpcExporterEndpoint.Should().Be("http://collector:4317");
    }

    [Theory]
    [InlineData("/health", true)]
    [InlineData("/health/ready", true)]
    [InlineData("/health/live", true)]
    [InlineData("/alive", true)]
    [InlineData("/metrics", true)]
    [InlineData("/api/v1/booking", false)]
    [InlineData("/healthy-flights", false)]
    public void probe_and_scrape_paths_are_excluded_from_request_telemetry(string path, bool excluded) =>
        ObservabilityExtensions.IsProbeOrScrapePath(new PathString(path)).Should().Be(excluded);

    [Fact]
    public async Task service_resource_carries_the_configured_service_name_and_namespace()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "test" });
        builder.WebHost.UseTestServer();
        builder.Configuration.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["ObservabilityOptions:InstrumentationName"] = "flight_service",
                ["ObservabilityOptions:ServiceName"] = "flight-service",
                ["ObservabilityOptions:UsePrometheusExporter"] = "false",
                ["ObservabilityOptions:UseOTLPExporter"] = "false",
                ["ObservabilityOptions:UseAspireOTLPExporter"] = "false",
            }
        );
        builder.AddCustomObservability();
        await using var app = builder.Build();

        var attributes = app.Services.GetRequiredService<TracerProvider>().GetResource().Attributes.ToDictionary();

        attributes["service.name"].Should().Be("flight-service");
        attributes["service.namespace"].Should().Be("booking");
        attributes["deployment.environment.name"].Should().Be("test");
    }

    [Fact]
    public async Task probe_endpoints_are_mapped_outside_development_with_live_and_ready_split()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "docker" });
        builder.WebHost.UseTestServer();
        builder.Configuration.AddInMemoryCollection(
            new Dictionary<string, string?> { ["HealthOptions:Enabled"] = "false" }
        );
        builder.Services.AddCustomHealthCheck();
        builder.Services.AddHealthChecks().AddCheck("dependency", () => HealthCheckResult.Unhealthy("down"));
        await using var app = builder.Build();
        app.UseCustomHealthCheck();
        await app.StartAsync();
        using var client = app.GetTestClient();

        (await client.GetAsync(HealthExtensions.LivenessEndpointPath)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.GetAsync(HealthExtensions.AlivenessEndpointPath)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.GetAsync(HealthExtensions.HealthEndpointPath))
            .StatusCode.Should()
            .Be(HttpStatusCode.ServiceUnavailable);

        using var ready = await client.GetAsync(HealthExtensions.ReadinessEndpointPath);
        ready.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        using var body = JsonDocument.Parse(await ready.Content.ReadAsStringAsync());
        body.RootElement.GetProperty("status").GetString().Should().Be("Unhealthy");
        var checks = body.RootElement.GetProperty("checks");
        checks.TryGetProperty("dependency", out _).Should().BeTrue();
        checks.TryGetProperty("self", out _).Should().BeFalse();
        checks.GetProperty("dependency").GetProperty("description").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task health_results_are_exported_as_a_gauge_per_check()
    {
        using var meterFactory = new ServiceCollection().AddMetrics().BuildServiceProvider();
        using var publisher = new HealthCheckMetricsPublisher(meterFactory.GetRequiredService<IMeterFactory>());
        var measurements = new List<(double Value, string? Check)>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Meter.Name == HealthCheckMetricsPublisher.MeterName)
                l.EnableMeasurementEvents(instrument);
        };
        listener.SetMeasurementEventCallback<double>(
            (_, value, tags, _) =>
                measurements.Add((value, tags.ToArray().Single(t => t.Key == "check").Value?.ToString()))
        );
        listener.Start();

        await publisher.PublishAsync(
            new HealthReport(
                new Dictionary<string, HealthReportEntry>
                {
                    ["self"] = new(HealthStatus.Healthy, null, TimeSpan.Zero, null, null),
                    ["rabbitmq"] = new(HealthStatus.Unhealthy, "down", TimeSpan.Zero, null, null),
                },
                TimeSpan.Zero
            ),
            CancellationToken.None
        );
        listener.RecordObservableInstruments();

        measurements.Should().BeEquivalentTo([(1d, "self"), (0d, "rabbitmq")]);
    }

    [Fact]
    public async Task outbox_row_carries_the_request_trace_and_publish_rejoins_it()
    {
        Activity? publishActivity = null;
        using var listener = new ActivityListener
        {
            ShouldListenTo = source =>
                source.Name is PersistMessageTracing.ActivitySourceName or nameof(ObservabilityWiringTests),
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
        };
        ActivitySource.AddActivityListener(listener);
        using var requestSource = new ActivitySource(nameof(ObservabilityWiringTests));

        await using var store = new PersistMessageDbContext<ObservabilityWiringTests>(
            new DbContextOptionsBuilder<PersistMessageDbContext<ObservabilityWiringTests>>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options
        );
        var endpoint = Substitute.For<IPublishEndpoint>();
        endpoint.ReturnsForAll<Task>(_ =>
        {
            publishActivity = Activity.Current;
            return Task.CompletedTask;
        });
        var processor = new PersistMessageProcessor<ObservabilityWiringTests>(
            NullLogger<PersistMessageProcessor<ObservabilityWiringTests>>.Instance,
            Substitute.For<IMediator>(),
            store,
            endpoint
        );
        var message = new TracedEvent(Guid.NewGuid());

        ActivityTraceId requestTraceId;
        ActivitySpanId requestSpanId;
        using (var request = requestSource.StartActivity("POST /api/v1/booking"))
        {
            requestTraceId = request!.TraceId;
            requestSpanId = request.SpanId;
            await processor.PublishMessageAsync(new MessageEnvelope(message, new Dictionary<string, object?>()));
        }

        var row = await store.PersistMessage.SingleAsync();
        var envelope = JsonSerializer.Deserialize<MessageEnvelope>(row.Data)!;
        envelope.Headers[PersistMessageTracing.TraceParentHeader]!
            .ToString()
            .Should()
            .Contain(requestTraceId.ToString());

        Activity.Current.Should().BeNull();
        await processor.ProcessAsync(row.Id, MessageDeliveryType.Outbox);

        publishActivity.Should().NotBeNull();
        publishActivity!.Source.Name.Should().Be(PersistMessageTracing.ActivitySourceName);
        publishActivity.DisplayName.Should().Be($"outbox publish {nameof(TracedEvent)}");
        publishActivity.TraceId.Should().Be(requestTraceId);
        publishActivity.ParentSpanId.Should().Be(requestSpanId);
    }

    [Fact]
    public void stored_trace_context_is_left_untouched_when_already_present()
    {
        using var activity = new Activity("current").SetIdFormat(ActivityIdFormat.W3C).Start();
        var original = new MessageEnvelope(
            new TracedEvent(Guid.NewGuid()),
            new Dictionary<string, object?>
            {
                [PersistMessageTracing.TraceParentHeader] = "00-0af7651916cd43dd8448eb211c80319c-b7ad6b7169203331-01",
            }
        );

        PersistMessageTracing.WithCurrentTraceContext(original).Should().BeSameAs(original);
        PersistMessageTracing
            .GetParentContext(original.Headers)
            .TraceId.ToString()
            .Should()
            .Be("0af7651916cd43dd8448eb211c80319c");
    }

    [Theory]
    [InlineData("src/Gateway/src/appsettings.json", "booking-gateway")]
    [InlineData("src/Services/Identity.Host/appsettings.json", "identity-service")]
    [InlineData("src/Services/Flight.Host/appsettings.json", "flight-service")]
    [InlineData("src/Services/Passenger.Host/appsettings.json", "passenger-service")]
    [InlineData("src/Services/Booking.Host/appsettings.json", "booking-service")]
    public void every_deployable_declares_its_own_service_name(string relativePath, string expected)
    {
        var options = new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(FindRepositoryRoot(), relativePath))
            .Build()
            .GetSection(nameof(ObservabilityOptions))
            .Get<ObservabilityOptions>()!;

        options.ServiceName.Should().Be(expected);
        options.OTLPOptions.OTLPGrpcExporterEndpoint.Should().NotBeNullOrWhiteSpace();
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "booking-modular-monolith.sln")))
            directory = directory.Parent;

        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }

    private sealed record TracedEvent(Guid EventId) : IIntegrationEvent;
}
