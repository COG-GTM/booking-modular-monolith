using System.Text.Json;
using BuildingBlocks.OpenTelemetryCollector;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Gateway.Unit.Test;

public class HostObservabilityConfigurationTests
{
    [Theory]
    [InlineData("Booking", "appsettings.json")]
    [InlineData("Passenger", "appsettings.json")]
    [InlineData("Identity", "appsettings.json")]
    [InlineData("Identity", "appsettings.docker.json")]
    public void Host_otlp_endpoint_binds_from_the_correct_key(string host, string file)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "booking-modular-monolith.sln")))
            directory = directory.Parent;
        Assert.NotNull(directory);
        var path = Path.Combine(directory.FullName, "src", "Services", $"{host}.Host", file);
        var configuration = new ConfigurationBuilder().AddJsonFile(path).Build();
        var options = configuration.GetSection(nameof(ObservabilityOptions)).Get<ObservabilityOptions>();
        Assert.NotNull(options);
        using var document = JsonDocument.Parse(
            File.ReadAllText(path),
            new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true }
        );
        var otlp = document.RootElement.GetProperty(nameof(ObservabilityOptions)).GetProperty("OTLPOptions");
        var endpoint = Assert.Single(otlp.EnumerateObject());
        Assert.Equal(nameof(OTLPOptions.OTLPGrpcExporterEndpoint), endpoint.Name);
        Assert.Equal(endpoint.Value.GetString(), options.OTLPOptions.OTLPGrpcExporterEndpoint);
        Assert.True(Uri.TryCreate(options.OTLPOptions.OTLPGrpcExporterEndpoint, UriKind.Absolute, out _));
    }
}
