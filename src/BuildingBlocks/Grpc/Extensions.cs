using Grpc.Core;
using Grpc.Health.V1;
using Grpc.Net.Client.Configuration;
using Grpc.Net.ClientFactory;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace BuildingBlocks.Grpc;

public static class Extensions
{
    public const string ReadinessTag = "ready";
    public const string GrpcDependencyTag = "grpc";

    /// <summary>
    /// Registers a gRPC client whose address is a logical service URI resolved through service discovery
    /// (Aspire / configuration / DNS), with a default deadline, gRPC retries on <see cref="StatusCode.Unavailable"/>
    /// and an HTTP/2 handler suitable for TLS. Transport-level resilience (Polly retry/circuit breaker/timeouts)
    /// comes from the host's standard HTTP resilience handler.
    /// </summary>
    public static IHttpClientBuilder AddResilientGrpcClient<TClient>(
        this IServiceCollection services,
        string name,
        GrpcClientOptions options
    )
        where TClient : class
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(options.Address);

        services.AddServiceDiscovery();

        return services
            .AddGrpcClient<TClient>(name, o => o.Address = new Uri(options.Address))
            .ConfigureChannel(channel => channel.ServiceConfig = CreateServiceConfig(options))
            .ConfigurePrimaryHttpMessageHandler(() => CreatePrimaryHandler(options))
            .AddInterceptor(() => new GrpcClientDeadlineInterceptor(options.Deadline))
            .AddServiceDiscovery();
    }

    /// <summary>
    /// Adds a readiness check that calls the standard gRPC health service (grpc.health.v1) of a dependency.
    /// </summary>
    public static IHealthChecksBuilder AddGrpcServiceHealthCheck(
        this IHealthChecksBuilder builder,
        string name,
        GrpcClientOptions options
    )
    {
        var clientName = $"{name}-health";
        builder.Services.AddResilientGrpcClient<Health.HealthClient>(clientName, options);

        return builder.Add(
            new HealthCheckRegistration(
                name,
                sp => new GrpcServiceHealthCheck(sp.GetRequiredService<GrpcClientFactory>(), clientName),
                HealthStatus.Unhealthy,
                [ReadinessTag, GrpcDependencyTag],
                options.Deadline
            )
        );
    }

    /// <summary>
    /// Exposes the host's health checks over the standard gRPC health service (grpc.health.v1).
    /// Outbound gRPC dependency checks are excluded so a host that both serves and consumes gRPC never
    /// reports on itself.
    /// </summary>
    public static IServiceCollection AddGrpcHealthService(this IServiceCollection services)
    {
        services.AddGrpcHealthChecks(options =>
        {
            options.Services.Map(string.Empty, registration => !registration.Tags.Contains(GrpcDependencyTag));
        });

        return services;
    }

    public static IEndpointRouteBuilder MapGrpcHealthService(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGrpcHealthChecksService();
        return endpoints;
    }

    private static ServiceConfig CreateServiceConfig(GrpcClientOptions options)
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

    private static SocketsHttpHandler CreatePrimaryHandler(GrpcClientOptions options)
    {
        var handler = new SocketsHttpHandler
        {
            EnableMultipleHttp2Connections = true,
            PooledConnectionIdleTimeout = Timeout.InfiniteTimeSpan,
            KeepAlivePingDelay = TimeSpan.FromSeconds(60),
            KeepAlivePingTimeout = TimeSpan.FromSeconds(30),
        };

        if (options.AcceptAnyServerCertificate)
        {
            handler.SslOptions.RemoteCertificateValidationCallback = (_, _, _, _) => true;
        }

        return handler;
    }
}
