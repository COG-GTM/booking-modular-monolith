using System.Diagnostics;
using BuildingBlocks.OpenTelemetryCollector;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using OpenTelemetry;
using OpenTelemetry.Instrumentation.AspNetCore;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Xunit;

namespace Gateway.Unit.Test;

public class ObservabilityRegistrationTests
{
    [Theory]
    [InlineData("otel-service", "configured", "otel-service")]
    [InlineData(null, "configured", "configured")]
    [InlineData(null, null, "registration-test")]
    public async Task Resource_contains_service_identity_and_environment(
        string? otelName,
        string? configured,
        string expected
    )
    {
        var builder = CreateBuilder();
        builder.Configuration.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["OTEL_SERVICE_NAME"] = otelName,
                ["ObservabilityOptions:ServiceName"] = configured,
            }
        );
        builder.AddCustomObservability();
        await using var app = builder.Build();
        var provider = app.Services.GetRequiredService<TracerProvider>();
        var attributes = provider.GetResource().Attributes.ToDictionary(pair => pair.Key, pair => pair.Value);
        Assert.Equal(expected, attributes["service.name"]);
        Assert.Equal("Testing", attributes["service.environment"]);
        Assert.Equal("Testing", attributes["deployment.environment"]);
        Assert.Equal(ActivityIdFormat.W3C, Activity.DefaultIdFormat);
        Assert.True(Activity.ForceDefaultIdFormat);
    }

    [Theory]
    [InlineData("/health", false)]
    [InlineData("/HEALTH/ready", false)]
    [InlineData("/health/live", false)]
    [InlineData("/alive", false)]
    [InlineData("/alive/details", false)]
    [InlineData("/metrics", false)]
    [InlineData("/METRICS/details", false)]
    [InlineData("/api/v1/flight", true)]
    [InlineData("/metrics-report", true)]
    [InlineData("/aliveness", true)]
    [InlineData("", true)]
    public async Task Request_filter_excludes_only_probe_paths(string path, bool expected)
    {
        var builder = CreateBuilder();
        builder.AddCustomObservability();
        await using var app = builder.Build();
        _ = app.Services.GetRequiredService<TracerProvider>();
        var options = app.Services.GetRequiredService<IOptions<AspNetCoreTraceInstrumentationOptions>>().Value;
        var context = new DefaultHttpContext();
        context.Request.Path = path;
        Assert.Equal(expected, options.Filter!(context));
    }

    [Theory]
    [InlineData(null, null, true)]
    [InlineData(null, true, true)]
    [InlineData(null, false, false)]
    [InlineData("always_off", null, false)]
    [InlineData("always_on", false, true)]
    public async Task Sampler_honors_parent_by_default_and_explicit_configuration(
        string? sampler,
        bool? sampledParent,
        bool expected
    )
    {
        var builder = CreateBuilder();
        builder.Configuration.AddInMemoryCollection(
            new Dictionary<string, string?> { ["OTEL_TRACES_SAMPLER"] = sampler }
        );
        builder.AddCustomObservability();
        await using var app = builder.Build();
        _ = app.Services.GetRequiredService<TracerProvider>();
        using var source = new ActivitySource("registration-tests");
        var parent = sampledParent.HasValue
            ? new ActivityContext(
                ActivityTraceId.CreateRandom(),
                ActivitySpanId.CreateRandom(),
                sampledParent.Value ? ActivityTraceFlags.Recorded : ActivityTraceFlags.None,
                isRemote: true
            )
            : default;
        using var activity = source.StartActivity("sample", ActivityKind.Server, parent);
        Assert.Equal(expected, activity?.Recorded == true);
    }

    private static WebApplicationBuilder CreateBuilder()
    {
        var builder = WebApplication.CreateBuilder(
            new WebApplicationOptions { ApplicationName = "registration-test", EnvironmentName = "Testing" }
        );
        builder.Configuration.Sources.Clear();
        builder.Configuration.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["ObservabilityOptions:InstrumentationName"] = "registration-tests",
                ["ObservabilityOptions:MetricsEnabled"] = "false",
                ["ObservabilityOptions:LoggingEnabled"] = "false",
                ["ObservabilityOptions:UseOTLPExporter"] = "false",
                ["ObservabilityOptions:UseAspireOTLPExporter"] = "false",
                ["ObservabilityOptions:UsePrometheusExporter"] = "false",
            }
        );
        return builder;
    }
}
