using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Integration.Test;

public class GrpcEndpointConfigurationTests
{
    [Theory]
    [InlineData("Production", 3002, 5000)]
    [InlineData("Staging", 3002, 5000)]
    [InlineData("Development", 3002, 3001)]
    [InlineData("docker", 81, 80)]
    public void monolith_discovery_matches_the_http2_listener_in_every_environment(
        string environment,
        int grpcPort,
        int httpPort
    )
    {
        using var configuration = new ConfigurationManager();
        configuration
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json")
            .AddJsonFile($"appsettings.{environment}.json", optional: true);

        configuration["Kestrel:Endpoints:Grpc:Protocols"].Should().Be("Http2");
        var grpcListener = configuration["Kestrel:Endpoints:Grpc:Url"];
        grpcListener.Should().Be(environment == "docker" ? "http://+:81" : "http://localhost:3002");
        new Uri(configuration["Kestrel:Endpoints:Http:Url"]!.Replace("+", "localhost", StringComparison.Ordinal))
            .Port.Should()
            .Be(httpPort);

        foreach (var service in new[] { "flight", "passenger" })
        {
            var address = new Uri(configuration[$"Services:{service}:grpc:0"]!);
            address.Scheme.Should().Be("http");
            address.Host.Should().Be("localhost");
            address.Port.Should().Be(grpcPort);
        }
    }
}
