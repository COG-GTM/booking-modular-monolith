using Booking.Extensions.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Unit.Test.Grpc;

internal static class GrpcClientTestHost
{
    /// <summary>Builds Booking's gRPC clients the way the hosts do (ServiceDefaults' HttpClient defaults first, then the module).</summary>
    public static ServiceProvider Build(IDictionary<string, string?> settings)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddLogging();
        services.ConfigureHttpClientDefaults(http =>
        {
            http.AddStandardResilienceHandler();
            http.AddServiceDiscovery();
        });
        services.AddGrpcClients();

        return services.BuildServiceProvider(validateScopes: true);
    }

    /// <summary>Logical addresses resolved through service discovery (Services:&lt;name&gt;:grpc:0), as in appsettings.json.</summary>
    public static Dictionary<string, string?> DiscoverySettings(string flightEndpoint, string passengerEndpoint) => new()
    {
        ["Grpc:Flight:Address"] = "http://_grpc.flight",
        ["Grpc:Flight:InitialBackoff"] = "00:00:00.010",
        ["Grpc:Flight:MaxBackoff"] = "00:00:00.050",
        ["Grpc:Passenger:Address"] = "http://_grpc.passenger",
        ["Grpc:Passenger:InitialBackoff"] = "00:00:00.010",
        ["Grpc:Passenger:MaxBackoff"] = "00:00:00.050",
        ["Services:flight:grpc:0"] = flightEndpoint,
        ["Services:passenger:grpc:0"] = passengerEndpoint,
    };
}
