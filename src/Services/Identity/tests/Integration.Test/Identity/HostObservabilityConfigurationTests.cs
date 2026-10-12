using BuildingBlocks.OpenTelemetryCollector;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Identity.Host.Integration.Test.Identity;

// Identity.Host must be identifiable in traces as its own service and must bind its OTLP endpoints
// from the corrected `OTLPGrpcExporterEndpoint` key. Reads the host's appsettings files copied next
// to the test assembly and does not use the container fixture.
public class HostObservabilityConfigurationTests
{
    private const string LegacyOtlpEndpointKey = "OTLPGrpExporterEndpoint";

    [Fact]
    public void observability_options_should_identify_the_identity_service()
    {
        var options = LoadObservabilityOptions();

        options.ServiceName.Should().Be("identity-service");
        options.InstrumentationName.Should().Be("identity_service");
    }

    [Fact]
    public void otlp_endpoints_should_bind_from_the_corrected_key()
    {
        var configuration = LoadHostConfiguration();
        var options = LoadObservabilityOptions();

        configuration["ObservabilityOptions:OTLPOptions:OTLPGrpcExporterEndpoint"].Should().Be("http://localhost:4317");
        configuration["ObservabilityOptions:AspireDashboardOTLPOptions:OTLPGrpcExporterEndpoint"]
            .Should()
            .Be("http://localhost:4319");
        options.OTLPOptions.OTLPGrpcExporterEndpoint.Should().Be("http://localhost:4317");
        options.AspireDashboardOTLPOptions.OTLPGrpcExporterEndpoint.Should().Be("http://localhost:4319");
    }

    [Fact]
    public void docker_profile_should_bind_compose_exporter_endpoints()
    {
        var configuration = LoadHostConfiguration("docker");
        var options = LoadObservabilityOptions("docker");

        configuration["ObservabilityOptions:OTLPOptions:OTLPGrpcExporterEndpoint"]
            .Should()
            .Be("http://otel-collector:4317");
        options.AspireDashboardOTLPOptions.OTLPGrpcExporterEndpoint.Should().Be("http://otel-collector:4319");
        options.ZipkinOptions.HttpExporterEndpoint.Should().Be("http://zipkin-all-in-one:9411/api/v2/spans");
        options.JaegerOptions.OTLPGrpcExporterEndpoint.Should().Be("http://jaeger-all-in-one:4317");
        options.JaegerOptions.HttpExporterEndpoint.Should().Be("http://jaeger-all-in-one:14268/api/traces");
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

    private static IConfiguration LoadHostConfiguration(string? environment = null)
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
