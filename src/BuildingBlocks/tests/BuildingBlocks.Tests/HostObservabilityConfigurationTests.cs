using BuildingBlocks.OpenTelemetryCollector;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace BuildingBlocks.Tests;

public class HostObservabilityConfigurationTests
{
    [Theory]
    [InlineData("src/Gateway/src", "booking-gateway")]
    [InlineData("src/Services/Booking.Host", "booking-service")]
    [InlineData("src/Services/Flight.Host", "flight-service")]
    [InlineData("src/Services/Identity.Host", "identity-service")]
    [InlineData("src/Services/Passenger.Host", "passenger-service")]
    public void docker_overlay_binds_collector_endpoint_and_preserves_host_identity(
        string hostDirectory,
        string serviceName
    )
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "booking-modular-monolith.sln")))
            root = root.Parent;
        root.Should().NotBeNull();
        var options = new ConfigurationBuilder()
            .SetBasePath(Path.Combine(root!.FullName, hostDirectory))
            .AddJsonFile("appsettings.json")
            .AddJsonFile("appsettings.docker.json")
            .Build()
            .GetSection(nameof(ObservabilityOptions))
            .Get<ObservabilityOptions>()!;

        options.ServiceName.Should().Be(serviceName);
        options.OTLPOptions.OTLPGrpcExporterEndpoint.Should().Be("http://otel-collector:4317");
        options.UseOTLPExporter.Should().BeTrue();
        options.UseAspireOTLPExporter.Should().BeFalse();
    }
}
