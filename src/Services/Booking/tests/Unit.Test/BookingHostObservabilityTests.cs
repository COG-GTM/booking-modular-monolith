using Booking.Host.Extensions;
using BuildingBlocks.OpenTelemetryCollector;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using OpenTelemetry;
using OpenTelemetry.Trace;
using Xunit;

namespace Booking.Host.Unit.Test;

// Booking.Host must report its own service identity to the tracing backend and bind its OTLP
// endpoints from the corrected `OTLPGrpcExporterEndpoint` key.
public class BookingHostObservabilityTests
{
    private const string LegacyOtlpEndpointKey = "OTLPGrpExporterEndpoint";

    [Fact]
    public void observability_options_identify_the_booking_service()
    {
        using var app = BuildHost();

        var options = app.Services.GetRequiredService<IOptions<ObservabilityOptions>>().Value;

        options.ServiceName.Should().Be("booking-service");
        options.InstrumentationName.Should().Be("booking_service");
        options.OTLPOptions.OTLPGrpcExporterEndpoint.Should().Be("http://localhost:4317");
        options.AspireDashboardOTLPOptions.OTLPGrpcExporterEndpoint.Should().Be("http://localhost:4319");
    }

    [Fact]
    public void tracer_resource_reports_the_booking_service_name()
    {
        using var app = BuildHost();

        var attributes = app.Services.GetRequiredService<TracerProvider>().GetResource().Attributes;

        attributes.Should().Contain(new KeyValuePair<string, object>("service.name", "booking-service"));
        attributes.Should().Contain(new KeyValuePair<string, object>("service.environment", "test"));
    }

    [Fact]
    public void docker_profile_binds_compose_exporter_endpoints()
    {
        var configuration = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.docker.json", optional: false)
            .Build();
        var options = configuration.GetSection(nameof(ObservabilityOptions)).Get<ObservabilityOptions>();

        options.Should().NotBeNull();
        options!.OTLPOptions.OTLPGrpcExporterEndpoint.Should().Be("http://otel-collector:4317");
        options.AspireDashboardOTLPOptions.OTLPGrpcExporterEndpoint.Should().Be("http://otel-collector:4319");
        options.ZipkinOptions.HttpExporterEndpoint.Should().Be("http://zipkin-all-in-one:9411/api/v2/spans");
        options.JaegerOptions.OTLPGrpcExporterEndpoint.Should().Be("http://jaeger-all-in-one:4317");
        options.JaegerOptions.HttpExporterEndpoint.Should().Be("http://jaeger-all-in-one:14268/api/traces");
    }

    [Theory]
    [InlineData("appsettings.json")]
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
            new WebApplicationOptions
            {
                ApplicationName = typeof(Program).Assembly.GetName().Name,
                ContentRootPath = AppContext.BaseDirectory,
                EnvironmentName = "test",
            }
        );
        builder.Configuration.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["ObservabilityOptions:UsePrometheusExporter"] = "false",
                ["ObservabilityOptions:UseOTLPExporter"] = "false",
                ["ObservabilityOptions:UseAspireOTLPExporter"] = "false",
            }
        );

        builder.AddBookingHost();

        return builder.Build();
    }

    private static IEnumerable<string> AllKeys(IConfiguration configuration) =>
        configuration.GetChildren().SelectMany(section => new[] { section.Key }.Concat(AllKeys(section)));
}
