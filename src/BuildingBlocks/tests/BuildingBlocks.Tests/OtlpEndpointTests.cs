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

[CollectionDefinition("OTLP environment", DisableParallelization = true)]
public class OtlpEnvironmentCollection;

[Collection("OTLP environment")]
public class OtlpEndpointTests
{
    [Theory]
    [InlineData("tracing", null)]
    [InlineData("metrics", null)]
    [InlineData("tracing", "http://localhost:18889")]
    [InlineData("metrics", "http://localhost:18889")]
    [InlineData("tracing", "not-a-uri")]
    [InlineData("metrics", "not-a-uri")]
    public void aspire_exporter_uses_environment_or_fallback(string signal, string? endpoint)
    {
        var previous = Environment.GetEnvironmentVariable("OTEL_EXPORTER_OTLP_ENDPOINT");
        try
        {
            Environment.SetEnvironmentVariable("OTEL_EXPORTER_OTLP_ENDPOINT", endpoint);
            if (endpoint == "not-a-uri")
                Assert.Throws<UriFormatException>(() => CheckExporter(signal, endpoint));
            else
                CheckExporter(signal, endpoint);
        }
        finally
        {
            Environment.SetEnvironmentVariable("OTEL_EXPORTER_OTLP_ENDPOINT", previous);
        }
    }

    private static void CheckExporter(string signal, string? endpoint)
    {
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.Configuration.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["ObservabilityOptions:InstrumentationName"] = "otlp-endpoint-test",
                ["ObservabilityOptions:TracingEnabled"] = (signal == "tracing").ToString(),
                ["ObservabilityOptions:MetricsEnabled"] = (signal == "metrics").ToString(),
                ["ObservabilityOptions:LoggingEnabled"] = "false",
                ["ObservabilityOptions:UsePrometheusExporter"] = "false",
                ["ObservabilityOptions:OTLPOptions:OTLPGrpcExporterEndpoint"] = "http://localhost:4317",
                ["ObservabilityOptions:AspireDashboardOTLPOptions:OTLPGrpcExporterEndpoint"] = "http://localhost:4319",
            }
        );
        builder.AddCustomObservability();
        using var app = builder.Build();
        if (signal == "tracing")
            app.Services.GetRequiredService<TracerProvider>();
        if (signal == "metrics")
            app.Services.GetRequiredService<MeterProvider>();
        var options = app.Services.GetRequiredService<IOptionsMonitor<OtlpExporterOptions>>();
        Assert.Equal(new Uri(endpoint ?? "http://localhost:4319"), options.Get("aspire").Endpoint);
        Assert.Equal(OtlpExportProtocol.Grpc, options.Get("aspire").Protocol);
    }
}
