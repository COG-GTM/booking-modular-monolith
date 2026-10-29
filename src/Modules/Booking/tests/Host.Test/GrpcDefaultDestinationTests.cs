using Booking.Extensions.Infrastructure;
using Contracts.Grpc.Flight;
using Contracts.Grpc.Passenger;
using Grpc.Net.ClientFactory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Host.Test;

public class GrpcDefaultDestinationTests
{
    [Theory]
    [InlineData("Flight", nameof(FlightGrpcService.FlightGrpcServiceClient), "flight-health")]
    [InlineData("Passenger", nameof(PassengerGrpcService.PassengerGrpcServiceClient), "passenger-health")]
    public void DefaultRpcAndHealthClientsTargetTheStandaloneHostsGrpcListener(
        string module,
        string clientName,
        string healthClientName
    )
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "booking-modular-monolith.sln")))
            root = root.Parent;
        Assert.NotNull(root);
        var servicesPath = Path.Combine(root.FullName, "src", "Services");
        var configuration = new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(servicesPath, "Booking.Host", "appsettings.json"))
            .Build();
        var upstream = new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(servicesPath, $"{module}.Host", "appsettings.json"))
            .Build();
        var expected = new Uri(upstream["Kestrel:Endpoints:Grpc:Url"]!);
        Assert.Equal("Http2", upstream["Kestrel:Endpoints:Grpc:Protocols"]);
        Assert.NotEqual(new Uri(upstream["Kestrel:Endpoints:Http:Url"]!), expected);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddGrpcClients(configuration);
        using var provider = services.BuildServiceProvider();
        var clients = provider.GetRequiredService<IOptionsMonitor<GrpcClientFactoryOptions>>();

        Assert.Equal(expected, clients.Get(clientName).Address);
        Assert.Equal(expected, clients.Get(healthClientName).Address);
    }
}
