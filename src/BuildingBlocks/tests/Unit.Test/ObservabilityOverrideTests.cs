using BuildingBlocks.OpenTelemetryCollector;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace BuildingBlocks.Unit.Test;

public class ObservabilityOverrideTests
{
    private static IConfiguration Configuration(Dictionary<string, string?> settings) =>
        new ConfigurationBuilder().AddInMemoryCollection(settings).Build();

    private sealed class TestEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Production";
        public string ApplicationName { get; set; } = "Booking.Host";
        public string ContentRootPath { get; set; } = "/";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    [Fact]
    public void service_name_values_are_trimmed()
    {
        Extensions
            .ResolveServiceName(
                Configuration(new() { ["OTEL_SERVICE_NAME"] = "  booking-service\n" }),
                new ObservabilityOptions(),
                new TestEnvironment()
            )
            .Should()
            .Be("booking-service");

        Extensions
            .ResolveServiceName(
                Configuration(new()),
                new ObservabilityOptions { ServiceName = " booking-service " },
                new TestEnvironment()
            )
            .Should()
            .Be("booking-service");
    }

    [Fact]
    public void blank_service_name_overrides_fall_through_to_the_next_source()
    {
        Extensions
            .ResolveServiceName(
                Configuration(new() { ["OTEL_SERVICE_NAME"] = "   " }),
                new ObservabilityOptions { ServiceName = "booking-service" },
                new TestEnvironment()
            )
            .Should()
            .Be("booking-service");

        Extensions
            .ResolveServiceName(
                Configuration(new() { ["OTEL_SERVICE_NAME"] = "" }),
                new ObservabilityOptions { ServiceName = " " },
                new TestEnvironment()
            )
            .Should()
            .Be("Booking.Host");
    }

    [Fact]
    public void otlp_endpoint_override_creates_the_options_section_when_missing()
    {
        var options = new ObservabilityOptions { OTLPOptions = null };

        Extensions.ApplyOtlpEndpointOverride(
            Configuration(new() { ["OTEL_EXPORTER_OTLP_ENDPOINT"] = " http://otel-collector:4317 " }),
            options
        );

        options.OTLPOptions.Should().NotBeNull();
        options.OTLPOptions!.OTLPGrpcExporterEndpoint.Should().Be("http://otel-collector:4317");
        options.OTLPOptions.OTLPHttpExporterEndpoint.Should().Be("http://localhost:4318");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void otlp_endpoint_is_left_alone_without_an_environment_override(string? value)
    {
        var options = new ObservabilityOptions
        {
            OTLPOptions = new OTLPOptions { OTLPGrpcExporterEndpoint = "http://configured:4317" },
        };
        var settings = new Dictionary<string, string?>();
        if (value is not null)
            settings["OTEL_EXPORTER_OTLP_ENDPOINT"] = value;

        Extensions.ApplyOtlpEndpointOverride(Configuration(settings), options);

        options.OTLPOptions.OTLPGrpcExporterEndpoint.Should().Be("http://configured:4317");

        var untouched = new ObservabilityOptions { OTLPOptions = null };
        Extensions.ApplyOtlpEndpointOverride(Configuration(settings), untouched);
        untouched.OTLPOptions.Should().BeNull();
    }

    [Fact]
    public void correctly_spelled_otlp_keys_bind_and_a_blank_legacy_alias_keeps_the_default()
    {
        var options = new ObservabilityOptions();
        Configuration(
                new()
                {
                    ["ObservabilityOptions:OTLPOptions:OTLPGrpcExporterEndpoint"] = "http://otel-collector:4317",
                    ["ObservabilityOptions:AspireDashboardOTLPOptions:OTLPGrpExporterEndpoint"] = "",
                }
            )
            .GetSection(nameof(ObservabilityOptions))
            .Bind(options);

        options.OTLPOptions!.OTLPGrpcExporterEndpoint.Should().Be("http://otel-collector:4317");
        options.OTLPOptions.OTLPGrpExporterEndpoint.Should().BeNull();
        options.AspireDashboardOTLPOptions!.OTLPGrpcExporterEndpoint.Should().Be("http://localhost:4319");
    }
}
