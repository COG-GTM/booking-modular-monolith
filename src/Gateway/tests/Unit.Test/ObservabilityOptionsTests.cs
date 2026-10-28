using BuildingBlocks.OpenTelemetryCollector;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Gateway.Unit.Test;

public class ObservabilityOptionsTests
{
    [Theory]
    [InlineData("environment", "configured", "environment")]
    [InlineData(null, "configured", "configured")]
    [InlineData("", "", "application")]
    [InlineData(null, null, "application")]
    public void Service_name_precedence(string? environment, string? configured, string expected)
    {
        var options = new ObservabilityOptions { ServiceName = configured };
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["OTEL_SERVICE_NAME"] = environment })
            .Build();
        options.ApplyOverrides(configuration, "application");
        Assert.Equal(expected, options.ServiceName);
    }

    [Theory]
    [InlineData("http://collector:4317", "http://collector:4317", true)]
    [InlineData(null, "http://configured:4317", false)]
    [InlineData("", "http://configured:4317", false)]
    public void Endpoint_override_enables_export(string? environment, string expected, bool enabled)
    {
        var options = new ObservabilityOptions
        {
            UseOTLPExporter = false,
            OTLPOptions = new OTLPOptions { OTLPGrpcExporterEndpoint = "http://configured:4317" },
        };
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["OTEL_EXPORTER_OTLP_ENDPOINT"] = environment })
            .Build();
        options.ApplyOverrides(configuration, "application");
        Assert.Equal(expected, options.OTLPOptions.OTLPGrpcExporterEndpoint);
        Assert.Equal(enabled, options.UseOTLPExporter);
    }
}
