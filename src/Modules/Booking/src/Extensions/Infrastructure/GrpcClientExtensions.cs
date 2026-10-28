using Booking.Configuration;
using Contracts.Grpc.Flight;
using Contracts.Grpc.Passenger;
using Grpc.Core;
using Grpc.Net.Client.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.ServiceDiscovery;
using GrpcHealth = Grpc.Health.V1.Health;

namespace Booking.Extensions.Infrastructure;

public static class GrpcClientExtensions
{
    public static IServiceCollection AddGrpcClients(this IServiceCollection services, IConfiguration configuration)
    {
        var grpcOptions = configuration.GetSection("Grpc").Get<GrpcOptions>() ?? new GrpcOptions();
        var flightAddress = GetAddress(grpcOptions.FlightAddress, "Grpc:FlightAddress");
        var passengerAddress = GetAddress(grpcOptions.PassengerAddress, "Grpc:PassengerAddress");

        if (grpcOptions.Deadline <= TimeSpan.Zero)
        {
            throw new InvalidOperationException("Grpc:Deadline must be greater than zero.");
        }

        if (grpcOptions.MaxRetryAttempts is < 1 or > 5)
        {
            throw new InvalidOperationException("Grpc:MaxRetryAttempts must be between 1 and 5.");
        }

        services.AddServiceDiscovery();
        services.AddTransient(_ => new GrpcDeadlineInterceptor(grpcOptions.Deadline));

        // HTTP resilience handlers can replay non-idempotent RPCs; the gRPC retry policy below is authoritative.
#pragma warning disable EXTEXP0001
        services
            .AddGrpcClient<FlightGrpcService.FlightGrpcServiceClient>(options => options.Address = flightAddress)
            .ConfigureChannel(channel => channel.ServiceConfig = CreateServiceConfig(grpcOptions))
            .AddInterceptor<GrpcDeadlineInterceptor>()
            .AddServiceDiscovery()
            .RemoveAllResilienceHandlers();

        services
            .AddGrpcClient<PassengerGrpcService.PassengerGrpcServiceClient>(options =>
                options.Address = passengerAddress
            )
            .ConfigureChannel(channel => channel.ServiceConfig = CreateServiceConfig(grpcOptions))
            .AddInterceptor<GrpcDeadlineInterceptor>()
            .AddServiceDiscovery()
            .RemoveAllResilienceHandlers();

        if (grpcOptions.HealthChecksEnabled)
        {
            services
                .AddGrpcClient<GrpcHealth.HealthClient>("flight-health", options => options.Address = flightAddress)
                .AddServiceDiscovery()
                .RemoveAllResilienceHandlers();
            services
                .AddGrpcClient<GrpcHealth.HealthClient>(
                    "passenger-health",
                    options => options.Address = passengerAddress
                )
                .AddServiceDiscovery()
                .RemoveAllResilienceHandlers();

            services
                .AddHealthChecks()
                .AddCheck<FlightGrpcHealthCheck>("grpc-flight", tags: ["ready"])
                .AddCheck<PassengerGrpcHealthCheck>("grpc-passenger", tags: ["ready"]);
        }

        return services;
    }

    private static Uri GetAddress(string? value, string key)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var address) || address.Scheme is not ("http" or "https"))
        {
            throw new InvalidOperationException(
                $"{key} must be an absolute http or https URI; received '{value ?? "<missing>"}'."
            );
        }

        return address;
    }

    private static ServiceConfig CreateServiceConfig(GrpcOptions options)
    {
        if (options.MaxRetryAttempts == 1)
        {
            return new ServiceConfig();
        }

        var retryPolicy = new RetryPolicy
        {
            MaxAttempts = options.MaxRetryAttempts,
            InitialBackoff = options.InitialBackoff,
            MaxBackoff = options.MaxBackoff,
            BackoffMultiplier = 2,
        };
        retryPolicy.RetryableStatusCodes.Add(StatusCode.Unavailable);

        return new ServiceConfig
        {
            MethodConfigs =
            {
                new MethodConfig
                {
                    Names =
                    {
                        new MethodName { Service = "flight.FlightGrpcService", Method = "GetById" },
                    },
                    RetryPolicy = retryPolicy,
                },
                new MethodConfig
                {
                    Names =
                    {
                        new MethodName { Service = "flight.FlightGrpcService", Method = "GetAvailableSeats" },
                    },
                    RetryPolicy = retryPolicy,
                },
                new MethodConfig
                {
                    Names =
                    {
                        new MethodName { Service = "passenger.PassengerGrpcService", Method = "GetById" },
                    },
                    RetryPolicy = retryPolicy,
                },
            },
        };
    }
#pragma warning restore EXTEXP0001
}
