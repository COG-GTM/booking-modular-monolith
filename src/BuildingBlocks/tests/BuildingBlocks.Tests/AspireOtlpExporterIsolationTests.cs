using BuildingBlocks.OpenTelemetryCollector;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenTelemetry.Exporter;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using Xunit;

namespace BuildingBlocks.Tests;

[Collection("OTLP environment")]
public class AspireOtlpExporterIsolationTests
{
    [Theory]
    [InlineData("tracing", null, "http://localhost:4319")]
    [InlineData("metrics", null, "http://localhost:4319")]
    [InlineData("tracing", "http://localhost:18889", "http://localhost:18889")]
    [InlineData("metrics", "http://localhost:18889", "http://localhost:18889")]
    public void Aspire_exporter_options_are_named_and_honor_otlp_environment_endpoint(
        string signal,
        string? environmentEndpoint,
        string expectedAspireEndpoint
    )
    {
        WithEnvironmentEndpoint(
            environmentEndpoint,
            () =>
            {
                var builder = CreateBuilder(signal);
                using var app = builder.Build();
                if (signal == "tracing")
                    app.Services.GetRequiredService<TracerProvider>();
                if (signal == "metrics")
                    app.Services.GetRequiredService<MeterProvider>();

                var options = app.Services.GetRequiredService<IOptionsMonitor<OtlpExporterOptions>>();
                Assert.Equal(new Uri(expectedAspireEndpoint), options.Get("aspire").Endpoint);
            }
        );
    }

    private static WebApplicationBuilder CreateBuilder(string? signal, bool loggingEnabled = false)
    {
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.Configuration.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["ObservabilityOptions:InstrumentationName"] = "aspire-otlp-isolation-test",
                ["ObservabilityOptions:TracingEnabled"] = (signal == "tracing").ToString(),
                ["ObservabilityOptions:MetricsEnabled"] = (signal == "metrics").ToString(),
                ["ObservabilityOptions:LoggingEnabled"] = loggingEnabled.ToString(),
                ["ObservabilityOptions:UsePrometheusExporter"] = "false",
                ["ObservabilityOptions:UseOTLPExporter"] = "true",
                ["ObservabilityOptions:UseAspireOTLPExporter"] = "true",
                ["ObservabilityOptions:OTLPOptions:OTLPGrpcExporterEndpoint"] = "http://otel-collector:4317",
                ["ObservabilityOptions:AspireDashboardOTLPOptions:OTLPGrpcExporterEndpoint"] = "http://localhost:4319",
            }
        );
        builder.AddCustomObservability();
        return builder;
    }

    private static void WithEnvironmentEndpoint(string? endpoint, Action action)
    {
        var previous = Environment.GetEnvironmentVariable("OTEL_EXPORTER_OTLP_ENDPOINT");
        try
        {
            Environment.SetEnvironmentVariable("OTEL_EXPORTER_OTLP_ENDPOINT", endpoint);
            action();
        }
        finally
        {
            Environment.SetEnvironmentVariable("OTEL_EXPORTER_OTLP_ENDPOINT", previous);
        }
    }
}
