using BuildingBlocks.OpenTelemetryCollector;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Integration.Test.Host;

// Flight.Host must be identifiable in traces as its own service and must bind its OTLP endpoints from
// the corrected `OTLPGrpcExporterEndpoint` key. Reads the host's appsettings files copied next to the
// test assembly and does not start any container.
public class FlightHostObservabilityConfigurationTests
{
    private const string LegacyOtlpEndpointKey = "OTLPGrpExporterEndpoint";

    [Fact]
    public void observability_options_should_identify_the_flight_service()
    {
        var options = LoadObservabilityOptions();

        options.ServiceName.Should().Be("flight-service");
        options.InstrumentationName.Should().Be("flight_service");
    }

    [Fact]
    public void default_profile_should_export_to_local_otlp_endpoints()
    {
        var options = LoadObservabilityOptions();

        options.OTLPOptions.OTLPGrpcExporterEndpoint.Should().Be("http://localhost:4317");
        options.AspireDashboardOTLPOptions.OTLPGrpcExporterEndpoint.Should().Be("http://localhost:4319");
    }

    [Fact]
    public void docker_profile_should_export_to_the_compose_collector_and_jaeger()
    {
        var options = LoadObservabilityOptions("docker");

        options.ServiceName.Should().Be("flight-service");
        options.OTLPOptions.OTLPGrpcExporterEndpoint.Should().Be("http://otel-collector:4317");
        options.AspireDashboardOTLPOptions.OTLPGrpcExporterEndpoint.Should().Be("http://otel-collector:4319");
        options.JaegerOptions.OTLPGrpcExporterEndpoint.Should().Be("http://jaeger-all-in-one:4317");
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
