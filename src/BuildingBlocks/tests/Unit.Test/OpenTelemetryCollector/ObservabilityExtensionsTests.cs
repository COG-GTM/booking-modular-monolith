using System.Collections.Concurrent;
using System.Diagnostics;
using BuildingBlocks.OpenTelemetryCollector;
using BuildingBlocks.PersistMessageProcessor;
using FluentAssertions;
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

namespace Unit.Test.OpenTelemetryCollector;

// Exercises AddCustomObservability/UseCustomObservability against a TestServer host with every
// exporter switched off: resource identity, which activity sources are subscribed, and which request
// paths are kept out of traces and HTTP metrics.
public class ObservabilityExtensionsTests
{
    private const string ServiceName = "unit-test-service";
    private const string InstrumentationName = "unit_test_instrumentation";
    private static readonly string ApplicationName = typeof(ObservabilityExtensionsTests).Assembly.GetName().Name!;

    [Fact]
    public void tracer_resource_uses_the_configured_service_name_environment_and_instance()
    {
        using var app = CreateBuilder().Build();

        var attributes = app.Services.GetRequiredService<TracerProvider>().GetResource().Attributes.ToList();

        attributes.Should().Contain(new KeyValuePair<string, object>("service.name", ServiceName));
        attributes.Should().Contain(new KeyValuePair<string, object>("service.environment", "test"));
        attributes.Should().Contain(new KeyValuePair<string, object>("service.instance.id", Environment.MachineName));
    }

    [Fact]
    public void tracer_resource_falls_back_to_the_application_name_when_service_name_is_not_configured()
    {
        using var app = CreateBuilder(new Dictionary<string, string?> { ["ObservabilityOptions:ServiceName"] = null })
            .Build();

        var attributes = app.Services.GetRequiredService<TracerProvider>().GetResource().Attributes;

        attributes.Should().Contain(new KeyValuePair<string, object>("service.name", ApplicationName));
    }

    [Theory]
    [InlineData(PersistMessageTracing.ActivitySourceName)]
    [InlineData("Yarp.ReverseProxy")]
    [InlineData(InstrumentationName)]
    public void tracing_subscribes_to_outbox_proxy_and_application_activity_sources(string sourceName)
    {
        using var app = CreateBuilder().Build();
        app.Services.GetRequiredService<TracerProvider>();
        using var source = new ActivitySource(sourceName);

        source.HasListeners().Should().BeTrue();
        using var activity = source.StartActivity("probe");
        activity.Should().NotBeNull();
        activity!.IsAllDataRequested.Should().BeTrue();
    }

    [Fact]
    public void tracing_does_not_subscribe_to_unrelated_activity_sources()
    {
        using var app = CreateBuilder().Build();
        app.Services.GetRequiredService<TracerProvider>();
        using var source = new ActivitySource("Unit.Test.Unrelated." + Guid.NewGuid());

        source.HasListeners().Should().BeFalse();
    }

    [Fact]
    public async Task probe_requests_are_excluded_from_server_traces_but_api_requests_are_recorded()
    {
        var recorded = new ConcurrentBag<Activity>();
        var tracedInHandler = new ConcurrentDictionary<string, bool>();
        var builder = CreateBuilder();
        builder.Services.ConfigureOpenTelemetryTracerProvider(tracing =>
            tracing.AddProcessor(new CollectingProcessor(recorded))
        );
        await using var app = builder.Build();
        app.UseCustomObservability();
        app.MapGet(
            "/{**path}",
            (HttpContext context) =>
            {
                tracedInHandler[context.Request.Path] =
                    Activity.Current is { IsAllDataRequested: true, Recorded: true };
                return "ok";
            }
        );
        await app.StartAsync();
        using var client = app.GetTestClient();

        foreach (var path in new[] { "/health", "/alive", "/ready", "/health/live", "/api/ping" })
        {
            (await client.GetAsync(path)).EnsureSuccessStatusCode();
        }

        tracedInHandler
            .Should()
            .BeEquivalentTo(
                new Dictionary<string, bool>
                {
                    ["/health"] = false,
                    ["/alive"] = false,
                    ["/ready"] = false,
                    ["/health/live"] = false,
                    ["/api/ping"] = true,
                }
            );

        // the server span is ended after the response has been handed to the client, so wait for it
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!recorded.Any(a => a.Kind == ActivityKind.Server) && DateTime.UtcNow < deadline)
        {
            await Task.Delay(20);
        }

        recorded
            .Where(a => a.Kind == ActivityKind.Server)
            .Select(a => a.GetTagItem("url.path")?.ToString())
            .Should()
            .BeEquivalentTo("/api/ping");
    }

    [Theory]
    [InlineData("/health", true)]
    [InlineData("/alive", true)]
    [InlineData("/ready", true)]
    [InlineData("/metrics", true)]
    [InlineData("/api/ping", false)]
    [InlineData("/healthz", false)]
    public async Task probe_and_scrape_requests_opt_out_of_http_metrics(string path, bool expectedDisabled)
    {
        var observed = new ConcurrentDictionary<string, bool>();
        await using var app = CreateBuilder().Build();
        app.Use(
            async (context, next) =>
            {
                var feature = new FakeMetricsTagsFeature();
                context.Features.Set<IHttpMetricsTagsFeature>(feature);
                await next(context);
                observed[context.Request.Path] = feature.MetricsDisabled;
            }
        );
        app.UseCustomObservability();
        app.MapGet("/{**path}", () => "ok");
        await app.StartAsync();
        using var client = app.GetTestClient();

        (await client.GetAsync(path)).EnsureSuccessStatusCode();

        observed.Should().ContainKey(path).WhoseValue.Should().Be(expectedDisabled);
    }

    private static WebApplicationBuilder CreateBuilder(IDictionary<string, string?>? overrides = null)
    {
        var builder = WebApplication.CreateBuilder(
            new WebApplicationOptions { EnvironmentName = "test", ApplicationName = ApplicationName }
        );
        builder.WebHost.UseTestServer();

        var settings = new Dictionary<string, string?>
        {
            ["ObservabilityOptions:InstrumentationName"] = InstrumentationName,
            ["ObservabilityOptions:ServiceName"] = ServiceName,
            ["ObservabilityOptions:MetricsEnabled"] = "false",
            ["ObservabilityOptions:LoggingEnabled"] = "false",
            ["ObservabilityOptions:UsePrometheusExporter"] = "false",
            ["ObservabilityOptions:UseOTLPExporter"] = "false",
            ["ObservabilityOptions:UseAspireOTLPExporter"] = "false",
        };
        foreach (var (key, value) in overrides ?? new Dictionary<string, string?>())
        {
            settings[key] = value;
        }

        builder.Configuration.AddInMemoryCollection(settings);
        builder.AddCustomObservability();
        return builder;
    }

    private sealed class CollectingProcessor(ConcurrentBag<Activity> sink) : BaseProcessor<Activity>
    {
        public override void OnEnd(Activity data) => sink.Add(data);
    }

    private sealed class FakeMetricsTagsFeature : IHttpMetricsTagsFeature
    {
        public ICollection<KeyValuePair<string, object?>> Tags { get; } = new List<KeyValuePair<string, object?>>();
        public bool MetricsDisabled { get; set; }
    }
}
