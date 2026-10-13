using Grpc.Core;
using Grpc.Health.V1;
using Grpc.Net.ClientFactory;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Booking.GrpcClient;

// Readiness check that calls the dependency's standard grpc.health.v1 service.
public class GrpcDependencyHealthCheck : IHealthCheck
{
    private readonly GrpcClientFactory _clientFactory;
    private readonly string _clientName;
    private readonly string _serviceName;

    public GrpcDependencyHealthCheck(GrpcClientFactory clientFactory, string clientName, string serviceName)
    {
        _clientFactory = clientFactory;
        _clientName = clientName;
        _serviceName = serviceName;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default
    )
    {
        try
        {
            var client = _clientFactory.CreateClient<Health.HealthClient>(_clientName);
            var response = await client.CheckAsync(
                new HealthCheckRequest { Service = _serviceName },
                cancellationToken: cancellationToken
            );

            return response.Status == HealthCheckResponse.Types.ServingStatus.Serving
                ? HealthCheckResult.Healthy($"{_serviceName} is serving")
                : new HealthCheckResult(
                    context.Registration.FailureStatus,
                    $"{_serviceName} gRPC health status: {response.Status}"
                );
        }
        catch (RpcException exception)
        {
            return new HealthCheckResult(
                context.Registration.FailureStatus,
                $"{_serviceName} gRPC health check failed: {exception.Status}",
                exception
            );
        }
    }
}
