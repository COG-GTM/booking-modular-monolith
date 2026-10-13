using BuildingBlocks.OpenTelemetryCollector;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace BuildingBlocks.Unit.Test;

public class ObservabilityExtensionsTests
{
    private static IConfiguration Configuration(Dictionary<string, string?> settings) =>
        new ConfigurationBuilder().AddInMemoryCollection(settings).Build();

    private sealed class TestEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Production";
        public string ApplicationName { get; set; } = "Flight.Host";
        public string ContentRootPath { get; set; } = "/";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    [Fact]
    public void otel_service_name_environment_variable_wins()
    {
        var name = Extensions.ResolveServiceName(
            Configuration(new() { ["OTEL_SERVICE_NAME"] = "flight-service" }),
            new ObservabilityOptions { ServiceName = "Flight Service" },
            new TestEnvironment()
        );

        name.Should().Be("flight-service");
    }

    [Fact]
    public void configured_service_name_is_used_when_no_environment_override()
    {
        var name = Extensions.ResolveServiceName(
            Configuration(new()),
            new ObservabilityOptions { ServiceName = "booking-service" },
            new TestEnvironment()
        );

        name.Should().Be("booking-service");
    }

    [Fact]
    public void application_name_is_the_fallback()
    {
        var name = Extensions.ResolveServiceName(Configuration(new()), new ObservabilityOptions(), new TestEnvironment());

        name.Should().Be("Flight.Host");
    }

    [Fact]
    public void otel_exporter_endpoint_environment_variable_overrides_the_otlp_endpoint()
    {
        var options = new ObservabilityOptions { OTLPOptions = new OTLPOptions() };

        Extensions.ApplyOtlpEndpointOverride(
            Configuration(new() { ["OTEL_EXPORTER_OTLP_ENDPOINT"] = "http://otel-collector:4317" }),
            options
        );

        options.OTLPOptions.OTLPGrpcExporterEndpoint.Should().Be("http://otel-collector:4317");
    }

    [Fact]
    public void legacy_misspelled_otlp_key_still_binds()
    {
        var options = new ObservabilityOptions();
        Configuration(
                new()
                {
                    ["ObservabilityOptions:OTLPOptions:OTLPGrpExporterEndpoint"] = "http://otel-collector:4317",
                    ["ObservabilityOptions:AspireDashboardOTLPOptions:OTLPGrpExporterEndpoint"] = "http://aspire:18889",
                }
            )
            .GetSection(nameof(ObservabilityOptions))
            .Bind(options);

        options.OTLPOptions!.OTLPGrpcExporterEndpoint.Should().Be("http://otel-collector:4317");
        options.AspireDashboardOTLPOptions!.OTLPGrpcExporterEndpoint.Should().Be("http://aspire:18889");
    }
}
