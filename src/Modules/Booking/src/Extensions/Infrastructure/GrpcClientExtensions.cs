using Booking.Configuration;
using Flight;
using Grpc.Core;
using Grpc.Health.V1;
using Grpc.Net.Client.Configuration;
using Grpc.Net.ClientFactory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Passenger;

namespace Booking.Extensions.Infrastructure;

public static class GrpcClientExtensions
{
    /// <summary>Tag on readiness checks that call another service; servers exclude them from their own gRPC health status.</summary>
    public const string GrpcDependencyHealthTag = "grpc-dependency";

    public const string ReadyHealthTag = "ready";

    internal const string FlightClientName = "flight";
    internal const string PassengerClientName = "passenger";

    // Only idempotent reads are retried; ReserveSeat mutates inventory and is attempted exactly once.
    private static readonly string[] FlightRetriedMethods = ["GetById", "GetAvailableSeats"];
    private static readonly string[] PassengerRetriedMethods = ["GetById"];

    public static IServiceCollection AddGrpcClients(this IServiceCollection services)
    {
        services.AddOptions<GrpcOptions>()
            .BindConfiguration(GrpcOptions.SectionName)
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<GrpcOptions>, GrpcOptionsValidator>();

        services.AddServiceDiscovery();

        services.AddGrpcClient<FlightGrpcService.FlightGrpcServiceClient>(FlightClientName, (sp, o) => o.Address = Endpoint(sp, FlightClientName).AddressUri())
            .ConfigureGrpcChannel(FlightClientName, FlightGrpcService.Descriptor.FullName, FlightRetriedMethods);

        services.AddGrpcClient<PassengerGrpcService.PassengerGrpcServiceClient>(PassengerClientName, (sp, o) => o.Address = Endpoint(sp, PassengerClientName).AddressUri())
            .ConfigureGrpcChannel(PassengerClientName, PassengerGrpcService.Descriptor.FullName, PassengerRetriedMethods);

        // Typed clients are resolved by the handlers; map them to the named registrations above.
        services.AddTransient(sp => sp.GetRequiredService<GrpcClientFactory>()
            .CreateClient<FlightGrpcService.FlightGrpcServiceClient>(FlightClientName));
        services.AddTransient(sp => sp.GetRequiredService<GrpcClientFactory>()
            .CreateClient<PassengerGrpcService.PassengerGrpcServiceClient>(PassengerClientName));

        services.AddHealthChecks()
            .AddGrpcDependency(services, FlightClientName)
            .AddGrpcDependency(services, PassengerClientName);

        return services;
    }

    private static IHealthChecksBuilder AddGrpcDependency(this IHealthChecksBuilder builder, IServiceCollection services, string client)
    {
        var healthClientName = $"{client}-health";

        services.AddGrpcClient<Health.HealthClient>(healthClientName, (sp, o) => o.Address = Endpoint(sp, client).AddressUri())
            .RemoveStandardResilience();

        return builder.Add(new HealthCheckRegistration(
            $"{client}-grpc",
            sp => new GrpcDependencyHealthCheck(
                sp.GetRequiredService<GrpcClientFactory>().CreateClient<Health.HealthClient>(healthClientName),
                Endpoint(sp, client).HealthCheckTimeout),
            HealthStatus.Unhealthy,
            [ReadyHealthTag, GrpcDependencyHealthTag]));
    }

    private static IHttpClientBuilder ConfigureGrpcChannel(this IHttpClientBuilder builder, string client, string serviceName, string[] retriedMethods)
    {
        return builder
            .RemoveStandardResilience()
            .ConfigureChannel((sp, channel) =>
            {
                var endpoint = Endpoint(sp, client);
                channel.MaxRetryAttempts = endpoint.MaxAttempts;
                channel.ServiceConfig = new ServiceConfig
                {
                    MethodConfigs =
                    {
                        RetriedMethods(serviceName, retriedMethods, endpoint),
                    },
                };
            })
            .AddInterceptor(InterceptorScope.Client, sp => new DefaultDeadlineInterceptor(Endpoint(sp, client).Deadline));
    }

    private static MethodConfig RetriedMethods(string serviceName, string[] methods, GrpcEndpointOptions endpoint)
    {
        var config = new MethodConfig();
        foreach (var method in methods)
            config.Names.Add(new MethodName { Service = serviceName, Method = method });

        if (endpoint.MaxAttempts > 1)
        {
            config.RetryPolicy = new RetryPolicy
            {
                MaxAttempts = endpoint.MaxAttempts,
                InitialBackoff = endpoint.InitialBackoff,
                MaxBackoff = endpoint.MaxBackoff,
                BackoffMultiplier = 2,
                RetryableStatusCodes = { StatusCode.Unavailable },
            };
        }

        return config;
    }

    // gRPC owns retries/deadlines here: an HTTP-level resilience handler (added for every HttpClient by ServiceDefaults)
    // can't see grpc-status trailers and would also replay the non-idempotent ReserveSeat on transport errors.
    private static IHttpClientBuilder RemoveStandardResilience(this IHttpClientBuilder builder)
    {
#pragma warning disable EXTEXP0001
        return builder.RemoveAllResilienceHandlers();
#pragma warning restore EXTEXP0001
    }

    private static GrpcEndpointOptions Endpoint(IServiceProvider sp, string client)
    {
        var options = sp.GetRequiredService<IOptions<GrpcOptions>>().Value;
        return client == FlightClientName ? options.Flight : options.Passenger;
    }

    private static Uri AddressUri(this GrpcEndpointOptions endpoint) => new(endpoint.Address!);
}
