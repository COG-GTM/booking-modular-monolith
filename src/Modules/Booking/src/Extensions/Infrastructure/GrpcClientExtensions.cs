using Booking.Configuration;
using Booking.GrpcClient;
using BuildingBlocks.Web;
using Contracts.Grpc.Flight;
using Contracts.Grpc.Passenger;
using Grpc.Core;
using Grpc.Health.V1;
using Grpc.Net.Client.Configuration;
using Grpc.Net.ClientFactory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Http.Resilience;
using Polly;

namespace Booking.Extensions.Infrastructure;

public static class GrpcClientExtensions
{
    public const string FlightClientName = "flight";
    public const string PassengerClientName = "passenger";
    public const string ReadinessTag = "ready";
    public const string GrpcDependencyTag = "grpc";

    public static IServiceCollection AddGrpcClients(this IServiceCollection services, IConfiguration configuration)
    {
        var grpcOptions = configuration.GetOptions<GrpcOptions>(GrpcOptions.SectionName);
        var flightAddress = GetRequiredAddress(grpcOptions.FlightAddress, nameof(GrpcOptions.FlightAddress));
        var passengerAddress = GetRequiredAddress(grpcOptions.PassengerAddress, nameof(GrpcOptions.PassengerAddress));

        services.AddServiceDiscovery();

        services.AddResilientGrpcClient<FlightGrpcService.FlightGrpcServiceClient>(
            FlightClientName,
            flightAddress,
            grpcOptions
        );
        services.AddResilientGrpcClient<PassengerGrpcService.PassengerGrpcServiceClient>(
            PassengerClientName,
            passengerAddress,
            grpcOptions
        );

        if (grpcOptions.HealthChecksEnabled)
        {
            services
                .AddHealthChecks()
                .AddGrpcDependencyHealthCheck(
                    FlightClientName,
                    FlightGrpcService.Descriptor.FullName,
                    flightAddress,
                    grpcOptions
                )
                .AddGrpcDependencyHealthCheck(
                    PassengerClientName,
                    PassengerGrpcService.Descriptor.FullName,
                    passengerAddress,
                    grpcOptions
                );
        }

        return services;
    }

    private static IHealthChecksBuilder AddGrpcDependencyHealthCheck(
        this IHealthChecksBuilder builder,
        string name,
        string serviceName,
        Uri address,
        GrpcOptions options
    )
    {
        var healthClientName = $"{name}-health";
        builder.Services.AddResilientGrpcClient<Health.HealthClient>(healthClientName, address, options);

        return builder.Add(
            new HealthCheckRegistration(
                $"{name}-grpc",
                sp => new GrpcDependencyHealthCheck(
                    sp.GetRequiredService<GrpcClientFactory>(),
                    healthClientName,
                    serviceName
                ),
                HealthStatus.Unhealthy,
                [ReadinessTag, GrpcDependencyTag],
                options.Deadline
            )
        );
    }

    // gRPC-native retries (Unavailable only, with backoff) + default deadline; the HTTP layer gets a
    // Polly circuit breaker instead of the host's standard resilience handler, whose HTTP retries
    // can't replay gRPC request streams.
    private static IHttpClientBuilder AddResilientGrpcClient<TClient>(
        this IServiceCollection services,
        string name,
        Uri address,
        GrpcOptions options
    )
        where TClient : class
    {
        var builder = services
            .AddGrpcClient<TClient>(name, o => o.Address = address)
            .ConfigureChannel(channel => channel.ServiceConfig = CreateServiceConfig(options))
            .ConfigurePrimaryHttpMessageHandler(() => CreatePrimaryHandler(options))
            .AddInterceptor(() => new GrpcDeadlineInterceptor(options.Deadline));

#pragma warning disable EXTEXP0001 // Experimental API: drops the host-wide standard handler for this client.
        builder.RemoveAllResilienceHandlers();
#pragma warning restore EXTEXP0001
        builder.AddResilienceHandler(
            $"grpc-{name}",
            pipeline => pipeline.AddCircuitBreaker(new HttpCircuitBreakerStrategyOptions())
        );
        builder.AddServiceDiscovery();

        return builder;
    }

    private static ServiceConfig CreateServiceConfig(GrpcOptions options)
    {
        var methodConfig = new MethodConfig { Names = { MethodName.Default } };

        if (options.MaxAttempts > 1)
        {
            methodConfig.RetryPolicy = new RetryPolicy
            {
                MaxAttempts = options.MaxAttempts,
                InitialBackoff = options.InitialBackoff,
                MaxBackoff = options.MaxBackoff,
                BackoffMultiplier = 2,
                RetryableStatusCodes = { StatusCode.Unavailable },
            };
        }

        return new ServiceConfig { MethodConfigs = { methodConfig } };
    }

    private static SocketsHttpHandler CreatePrimaryHandler(GrpcOptions options)
    {
        var handler = new SocketsHttpHandler
        {
            EnableMultipleHttp2Connections = true,
            PooledConnectionIdleTimeout = Timeout.InfiniteTimeSpan,
            KeepAlivePingDelay = TimeSpan.FromSeconds(60),
            KeepAlivePingTimeout = TimeSpan.FromSeconds(30),
        };

        if (options.AllowUntrustedServerCertificate)
        {
            handler.SslOptions.RemoteCertificateValidationCallback = (_, _, _, _) => true;
        }

        return handler;
    }

    private static Uri GetRequiredAddress(string? address, string key)
    {
        if (string.IsNullOrWhiteSpace(address))
        {
            throw new InvalidOperationException(
                $"Missing gRPC endpoint configuration '{GrpcOptions.SectionName}:{key}'."
            );
        }

        var uri = new Uri(address, UriKind.Absolute);

        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
        {
            throw new InvalidOperationException(
                $"'{GrpcOptions.SectionName}:{key}' must be a plain http(s) URL, got '{address}'."
            );
        }

        return uri;
    }
}
