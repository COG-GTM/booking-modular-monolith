using System.Collections.Concurrent;
using BuildingBlocks.OpenTelemetryCollector;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace BuildingBlocks.Unit.Test;

public class ObservabilityMetricsMiddlewareTests
{
    private sealed class FakeMetricsFeature : IHttpMetricsTagsFeature
    {
        public ICollection<KeyValuePair<string, object?>> Tags { get; } = [];

        public bool MetricsDisabled { get; set; }
    }

    [Theory]
    [InlineData("/metrics", true)]
    [InlineData("/METRICS", true)]
    [InlineData("/health", true)]
    [InlineData("/health/live", true)]
    [InlineData("/health/ready", true)]
    [InlineData("/alive", true)]
    [InlineData("/api/v1.0/booking", false)]
    [InlineData("/healthy-flights", false)]
    public async Task http_metrics_are_disabled_only_for_infrastructure_requests(string path, bool disabled)
    {
        var builder = WebApplication.CreateBuilder(
            new WebApplicationOptions { EnvironmentName = "Production", ApplicationName = "BuildingBlocks.Unit.Test" }
        );
        builder.WebHost.UseTestServer();
        builder.Configuration.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["ObservabilityOptions:InstrumentationName"] = "unit_test",
                ["ObservabilityOptions:ServiceName"] = "flight-service",
                ["ObservabilityOptions:LoggingEnabled"] = "false",
                ["ObservabilityOptions:MetricsEnabled"] = "false",
                ["ObservabilityOptions:UsePrometheusExporter"] = "false",
                ["ObservabilityOptions:UseOTLPExporter"] = "false",
                ["ObservabilityOptions:UseAspireOTLPExporter"] = "false",
            }
        );
        builder.AddCustomObservability();
        await using var app = builder.Build();

        var features = new ConcurrentDictionary<string, FakeMetricsFeature>();
        app.Use(
            async (context, next) =>
            {
                context.Features.Set<IHttpMetricsTagsFeature>(features.GetOrAdd(context.Request.Path, _ => new()));
                await next(context);
            }
        );
        app.UseCustomObservability();
        app.MapGet("/{**path}", () => "ok");
        await app.StartAsync();

        (await app.GetTestClient().GetAsync(path)).EnsureSuccessStatusCode();

        features[path].MetricsDisabled.Should().Be(disabled);
    }
}
