using BuildingBlocks.OpenTelemetryCollector;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using OpenTelemetry;
using OpenTelemetry.Trace;
using Xunit;

namespace Gateway.Unit.Test;

// The gateway must show up in traces under its own service identity, and its OTLP endpoints must be
// bound from the corrected `OTLPGrpcExporterEndpoint` key in every settings profile.
public class GatewayObservabilityOptionsTests
{
    private const string LegacyOtlpEndpointKey = "OTLPGrpExporterEndpoint";

    [Fact]
    public void observability_options_identify_the_gateway_service()
    {
        using var factory = CreateFactory();

        var options = factory.Services.GetRequiredService<IOptions<ObservabilityOptions>>().Value;

        Assert.Equal("gateway", options.ServiceName);
        Assert.Equal("booking_gateway", options.InstrumentationName);
    }

    [Fact]
    public void tracer_resource_reports_the_gateway_service_name()
    {
        using var factory = CreateFactory();

        var attributes = factory.Services.GetRequiredService<TracerProvider>().GetResource().Attributes;

        Assert.Contains(attributes, a => a.Key == "service.name" && Equals(a.Value, "gateway"));
        Assert.Contains(attributes, a => a.Key == "service.environment" && Equals(a.Value, "test"));
    }

    [Theory]
    [InlineData(null, "http://localhost:4317", "http://localhost:4319")]
    [InlineData("docker", "http://otel-collector:4317", "http://localhost:4319")]
    public void otlp_endpoints_bind_from_the_corrected_key_in_each_profile(
        string? environment,
        string expectedOtlpEndpoint,
        string expectedAspireEndpoint
    )
    {
        var configuration = LoadHostConfiguration(environment);

        var options = configuration.GetSection(nameof(ObservabilityOptions)).Get<ObservabilityOptions>();

        Assert.NotNull(options);
        Assert.Equal(expectedOtlpEndpoint, configuration["ObservabilityOptions:OTLPOptions:OTLPGrpcExporterEndpoint"]);
        Assert.Equal(expectedOtlpEndpoint, options.OTLPOptions.OTLPGrpcExporterEndpoint);
        Assert.Equal(expectedAspireEndpoint, options.AspireDashboardOTLPOptions.OTLPGrpcExporterEndpoint);
    }

    [Fact]
    public void docker_profile_sends_telemetry_to_the_collector_instead_of_the_aspire_dashboard()
    {
        var options = LoadHostConfiguration("docker")
            .GetSection(nameof(ObservabilityOptions))
            .Get<ObservabilityOptions>();

        Assert.NotNull(options);
        Assert.True(options.UseOTLPExporter);
        Assert.False(options.UseAspireOTLPExporter);
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

        Assert.DoesNotContain(LegacyOtlpEndpointKey, AllKeys(configuration));
    }

    private static WebApplicationFactory<Program> CreateFactory() =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("test");
            builder.UseSetting("ObservabilityOptions:UsePrometheusExporter", "false");
            builder.UseSetting("ObservabilityOptions:UseOTLPExporter", "false");
            builder.UseSetting("ObservabilityOptions:UseAspireOTLPExporter", "false");
        });

    private static IConfiguration LoadHostConfiguration(string? environment)
    {
        var builder = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: false);

        if (environment is not null)
            builder.AddJsonFile($"appsettings.{environment}.json", optional: false);

        return builder.Build();
    }

    private static IEnumerable<string> AllKeys(IConfiguration configuration) =>
        configuration.GetChildren().SelectMany(section => new[] { section.Key }.Concat(AllKeys(section)));
}
