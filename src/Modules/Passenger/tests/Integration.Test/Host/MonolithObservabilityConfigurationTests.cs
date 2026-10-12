using BuildingBlocks.OpenTelemetryCollector;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Integration.Test.Host;

// The monolith host must be identifiable in traces as its own service and must bind its OTLP endpoints
// from the corrected `OTLPGrpcExporterEndpoint` key in every profile, including the docker overrides
// that point at the compose collector. Reads the Api appsettings files copied next to the test assembly
// and does not start any container.
public class MonolithObservabilityConfigurationTests
{
    private const string LegacyOtlpEndpointKey = "OTLPGrpExporterEndpoint";

    [Fact]
    public void observability_options_should_identify_the_monolith_service()
    {
        var options = LoadObservabilityOptions();

        options.ServiceName.Should().Be("booking-modular-monolith");
        options.InstrumentationName.Should().Be("booking_modular_monolith_service");
    }

    [Fact]
    public void default_profile_should_export_to_local_otlp_endpoints()
    {
        var options = LoadObservabilityOptions();

        options.OTLPOptions.OTLPGrpcExporterEndpoint.Should().Be("http://localhost:4317");
        options.AspireDashboardOTLPOptions.OTLPGrpcExporterEndpoint.Should().Be("http://localhost:4319");
        options.JaegerOptions.OTLPGrpcExporterEndpoint.Should().Be("http://localhost:14317");
    }

    [Fact]
    public void docker_profile_should_export_to_the_compose_collector_and_jaeger()
    {
        var options = LoadObservabilityOptions("docker");

        options.ServiceName.Should().Be("booking-modular-monolith");
        options.OTLPOptions.OTLPGrpcExporterEndpoint.Should().Be("http://otel-collector:4317");
        options.AspireDashboardOTLPOptions.OTLPGrpcExporterEndpoint.Should().Be("http://otel-collector:4319");
        options.ZipkinOptions.HttpExporterEndpoint.Should().Be("http://zipkin-all-in-one:9411/api/v2/spans");
        options.JaegerOptions.OTLPGrpcExporterEndpoint.Should().Be("http://jaeger-all-in-one:4317");
        options.JaegerOptions.HttpExporterEndpoint.Should().Be("http://jaeger-all-in-one:14268/api/traces");
    }

    [Fact]
    public void docker_profile_should_keep_the_default_exporter_switches()
    {
        var defaults = LoadObservabilityOptions();
        var docker = LoadObservabilityOptions("docker");

        docker.UseOTLPExporter.Should().Be(defaults.UseOTLPExporter);
        docker.UseAspireOTLPExporter.Should().Be(defaults.UseAspireOTLPExporter);
        docker.UsePrometheusExporter.Should().Be(defaults.UsePrometheusExporter);
        docker.UseGrafanaExporter.Should().Be(defaults.UseGrafanaExporter);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("docker")]
    public void settings_profiles_should_not_use_the_misspelled_otlp_endpoint_key(string? environment)
    {
        var configuration = LoadHostConfiguration(environment);

        AllKeys(configuration).Should().NotContain(LegacyOtlpEndpointKey);
    }

    private static ObservabilityOptions LoadObservabilityOptions(string? environment = null)
    {
        var options = LoadHostConfiguration(environment)
            .GetSection(nameof(ObservabilityOptions))
            .Get<ObservabilityOptions>();

        options.Should().NotBeNull();
        return options!;
    }

    private static IConfiguration LoadHostConfiguration(string? environment)
    {
        var builder = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: false);

        if (environment is not null)
        {
            builder.AddJsonFile($"appsettings.{environment}.json", optional: false);
        }

        return builder.Build();
    }

    private static IEnumerable<string> AllKeys(IConfiguration configuration) =>
        configuration.GetChildren().SelectMany(section => new[] { section.Key }.Concat(AllKeys(section)));
}
