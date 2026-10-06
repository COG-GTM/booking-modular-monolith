using Booking.Extensions.Infrastructure;
using BuildingBlocks.Grpc;
using Contracts.Grpc.Flight.V1;
using Contracts.Grpc.Passenger.V1;
using FluentAssertions;
using Grpc.Net.ClientFactory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.ServiceDiscovery;
using Xunit;

namespace Unit.Test.Grpc;

public class GrpcClientExtensionsTests
{
    [Fact]
    public void should_register_clients_with_logical_service_addresses_by_default()
    {
        var provider = BuildProvider(new Dictionary<string, string?>());

        provider.GetRequiredService<FlightGrpcService.FlightGrpcServiceClient>().Should().NotBeNull();
        provider.GetRequiredService<PassengerGrpcService.PassengerGrpcServiceClient>().Should().NotBeNull();

        var clientOptions = provider.GetRequiredService<IOptionsMonitor<GrpcClientFactoryOptions>>();
        clientOptions.Get("flight").Address.Should().Be(new Uri("https://flight"));
        clientOptions.Get("passenger").Address.Should().Be(new Uri("https://passenger"));
    }

    [Fact]
    public void should_take_service_addresses_from_configuration()
    {
        var provider = BuildProvider(
            new Dictionary<string, string?>
            {
                ["Grpc:Flight:Address"] = "http://flight-service:80",
                ["Grpc:Passenger:Address"] = "https://passenger.booking.svc.cluster.local",
            }
        );

        var clientOptions = provider.GetRequiredService<IOptionsMonitor<GrpcClientFactoryOptions>>();
        clientOptions.Get("flight").Address.Should().Be(new Uri("http://flight-service:80"));
        clientOptions.Get("passenger").Address.Should().Be(new Uri("https://passenger.booking.svc.cluster.local"));
    }

    [Fact]
    public async Task should_resolve_logical_service_names_through_service_discovery()
    {
        var provider = BuildProvider(
            new Dictionary<string, string?>
            {
                ["Services:flight:https:0"] = "https://localhost:3000",
                ["Services:passenger:https:0"] = "https://passenger.internal:8443",
            }
        );

        var resolver = provider.GetRequiredService<ServiceEndpointResolver>();

        var flight = await resolver.GetEndpointsAsync("https://flight", CancellationToken.None);
        var passenger = await resolver.GetEndpointsAsync("https://passenger", CancellationToken.None);

        flight
            .Endpoints.Select(e => e.ToString())
            .Should()
            .ContainSingle()
            .Which.Should()
            .Be("https://localhost:3000/");
        passenger
            .Endpoints.Select(e => e.ToString())
            .Should()
            .ContainSingle()
            .Which.Should()
            .Be("https://passenger.internal:8443/");
    }

    [Fact]
    public void should_register_readiness_checks_for_each_grpc_dependency()
    {
        var provider = BuildProvider(new Dictionary<string, string?>());

        var registrations = provider
            .GetRequiredService<IOptions<HealthCheckServiceOptions>>()
            .Value.Registrations.Where(r => r.Tags.Contains(Extensions.GrpcDependencyTag))
            .ToList();

        registrations.Select(r => r.Name).Should().BeEquivalentTo("flight", "passenger");
        registrations.Should().OnlyContain(r => r.Tags.Contains(Extensions.ReadinessTag));
        registrations.Should().OnlyContain(r => r.Factory(provider) is GrpcServiceHealthCheck);
    }

    private static ServiceProvider BuildProvider(IDictionary<string, string?> settings)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddLogging();
        services.AddGrpcClients();

        return services.BuildServiceProvider();
    }
}
