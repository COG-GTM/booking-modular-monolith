using BuildingBlocks.OpenTelemetryCollector;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Gateway.Test;

public class GatewayObservabilityConfigurationTests
{
    [Theory]
    [InlineData(false, "http://localhost:4317")]
    [InlineData(true, "http://otel-collector:4317")]
    public void exporter_endpoints_bind_from_gateway_configuration(bool docker, string expectedEndpoint)
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "booking-modular-monolith.sln")))
            root = root.Parent;
        Assert.NotNull(root);

        var builder = new ConfigurationBuilder()
            .SetBasePath(Path.Combine(root.FullName, "src/Gateway/src"))
            .AddJsonFile("appsettings.json");
        if (docker)
            builder.AddJsonFile("appsettings.docker.json");
        var configuration = builder.Build();
        var section = configuration.GetSection(nameof(ObservabilityOptions));
        var options = section.Get<ObservabilityOptions>();

        Assert.NotNull(options);
        Assert.Equal(expectedEndpoint, options.OTLPOptions.OTLPGrpcExporterEndpoint);
        Assert.Equal("http://localhost:4319", options.AspireDashboardOTLPOptions.OTLPGrpcExporterEndpoint);
        Assert.DoesNotContain(
            section.AsEnumerable(),
            entry => entry.Key.Split(':').Contains("OTLPGrpExporterEndpoint", StringComparer.OrdinalIgnoreCase)
        );
    }
}
