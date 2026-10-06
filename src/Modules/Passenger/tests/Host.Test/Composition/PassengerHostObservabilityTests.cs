using BuildingBlocks.OpenTelemetryCollector;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using OpenTelemetry;
using OpenTelemetry.Trace;
using Passenger.Host.Extensions;
using Xunit;

namespace Host.Test.Composition;

// Passenger.Host must report its own service identity to the tracing backend and bind its OTLP
// endpoints from the corrected `OTLPGrpcExporterEndpoint` key.
public class PassengerHostObservabilityTests
{
    private const string LegacyOtlpEndpointKey = "OTLPGrpExporterEndpoint";

    [Fact]
    public void observability_options_identify_the_passenger_service()
    {
        using var app = BuildHost();

        var options = app.Services.GetRequiredService<IOptions<ObservabilityOptions>>().Value;

        options.ServiceName.Should().Be("passenger-service");
        options.InstrumentationName.Should().Be("passenger_service");
        options.OTLPOptions.OTLPGrpcExporterEndpoint.Should().Be("http://localhost:4317");
        options.AspireDashboardOTLPOptions.OTLPGrpcExporterEndpoint.Should().Be("http://localhost:4319");
    }

    [Fact]
    public void tracer_resource_reports_the_passenger_service_name()
    {
        using var app = BuildHost();

        var attributes = app.Services.GetRequiredService<TracerProvider>().GetResource().Attributes;

        attributes.Should().Contain(new KeyValuePair<string, object>("service.name", "passenger-service"));
        attributes.Should().Contain(new KeyValuePair<string, object>("service.environment", "test"));
    }

    [Theory]
    [InlineData("passenger-host-appsettings.json")]
    [InlineData("appsettings.docker.json")]
    public void settings_profiles_do_not_use_the_misspelled_otlp_endpoint_key(string file)
    {
        var configuration = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile(file, optional: false)
            .Build();

        AllKeys(configuration).Should().NotContain(LegacyOtlpEndpointKey);
    }

    private static WebApplication BuildHost()
    {
        var builder = WebApplication.CreateBuilder(
            new WebApplicationOptions { EnvironmentName = "test", ApplicationName = "Passenger.Host" }
        );
        builder.Configuration.AddJsonFile("passenger-host-appsettings.json");
        builder.Configuration.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["ObservabilityOptions:UsePrometheusExporter"] = "false",
                ["ObservabilityOptions:UseOTLPExporter"] = "false",
                ["ObservabilityOptions:UseAspireOTLPExporter"] = "false",
            }
        );

        builder.AddPassengerHost();

        return builder.Build();
    }

    private static IEnumerable<string> AllKeys(IConfiguration configuration) =>
        configuration.GetChildren().SelectMany(section => new[] { section.Key }.Concat(AllKeys(section)));
}
