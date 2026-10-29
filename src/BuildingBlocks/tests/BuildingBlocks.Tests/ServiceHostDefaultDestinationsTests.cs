using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace BuildingBlocks.Tests;

public class ServiceHostDefaultDestinationsTests
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();

    [Theory]
    [InlineData("Flight")]
    [InlineData("Passenger")]
    [InlineData("Booking")]
    public void local_and_docker_metadata_addresses_match_identity_host(string serviceHost)
    {
        var serviceDirectory = Path.Combine(RepositoryRoot, "src", "Services", $"{serviceHost}.Host");
        var identitySettingsPath = Path.Combine(RepositoryRoot, "src", "Services", "Identity.Host", "appsettings.json");
        using var identityDocument = JsonDocument.Parse(File.ReadAllText(identitySettingsPath));
        var identityUrl = identityDocument.RootElement.GetProperty("Urls").GetString();

        var localConfiguration = LoadConfiguration(serviceDirectory, includeDockerSettings: false);
        localConfiguration["Jwt:MetadataAddress"].Should().Be($"{identityUrl}/.well-known/openid-configuration");

        var dockerConfiguration = LoadConfiguration(serviceDirectory, includeDockerSettings: true);
        dockerConfiguration["Jwt:MetadataAddress"]
            .Should()
            .Be("http://identity-service:80/.well-known/openid-configuration");
    }

    [Fact]
    public void booking_local_grpc_destinations_match_flight_and_passenger_host_endpoints()
    {
        var bookingSettingsPath = Path.Combine(RepositoryRoot, "src", "Services", "Booking.Host", "appsettings.json");
        using var bookingDocument = JsonDocument.Parse(File.ReadAllText(bookingSettingsPath));
        var flightGrpcUrl = GrpcEndpointUrl("Flight", "appsettings.json");
        var passengerGrpcUrl = GrpcEndpointUrl("Passenger", "appsettings.Development.json");

        bookingDocument
            .RootElement.GetProperty("Services")
            .GetProperty("flight")
            .GetProperty("grpc")[0]
            .GetString()
            .Should()
            .Be(flightGrpcUrl);
        bookingDocument
            .RootElement.GetProperty("Services")
            .GetProperty("passenger")
            .GetProperty("grpc")[0]
            .GetString()
            .Should()
            .Be(passengerGrpcUrl);
    }

    [Fact]
    public void service_and_gateway_appsettings_no_longer_reference_monolith_only_addresses()
    {
        var serviceSettings = Directory.GetFiles(
            Path.Combine(RepositoryRoot, "src", "Services"),
            "appsettings*.json",
            SearchOption.AllDirectories
        );
        var gatewaySettings = Directory.GetFiles(
            Path.Combine(RepositoryRoot, "src", "Gateway", "src"),
            "appsettings*.json",
            SearchOption.AllDirectories
        );
        var settingsContents = string.Join(
            Environment.NewLine,
            serviceSettings.Concat(gatewaySettings).Select(File.ReadAllText)
        );

        settingsContents.Should().NotContain("booking_modular_monolith");
        settingsContents.Should().NotContain("localhost:3001");
        settingsContents.Should().NotContain("localhost:3002");
        Directory.Exists(Path.Combine(RepositoryRoot, "src", "Api")).Should().BeFalse();
    }

    private static IConfigurationRoot LoadConfiguration(string hostDirectory, bool includeDockerSettings)
    {
        var builder = new ConfigurationBuilder().SetBasePath(hostDirectory).AddJsonFile("appsettings.json");
        if (includeDockerSettings)
            builder.AddJsonFile("appsettings.docker.json");
        return builder.Build();
    }

    private static string? GrpcEndpointUrl(string serviceHost, string settingsFile)
    {
        var settingsPath = Path.Combine(RepositoryRoot, "src", "Services", $"{serviceHost}.Host", settingsFile);
        using var document = JsonDocument.Parse(File.ReadAllText(settingsPath));
        var endpoints = document.RootElement.GetProperty("Kestrel").GetProperty("Endpoints");
        var grpcEndpoint = endpoints
            .EnumerateObject()
            .Single(endpoint =>
                endpoint.Value.TryGetProperty("Protocols", out var protocols) && protocols.GetString() == "Http2"
            );
        return grpcEndpoint.Value.GetProperty("Url").GetString();
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "booking-modular-monolith.sln")))
            directory = directory.Parent;

        return directory?.FullName
            ?? throw new DirectoryNotFoundException("Could not locate booking-modular-monolith.sln.");
    }
}
