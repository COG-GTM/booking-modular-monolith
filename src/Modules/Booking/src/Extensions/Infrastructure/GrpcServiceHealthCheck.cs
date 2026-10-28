using Grpc.Core;
using Grpc.Health.V1;
using Grpc.Net.ClientFactory;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Booking.Extensions.Infrastructure;

public abstract class GrpcServiceHealthCheck(GrpcClientFactory clientFactory, string clientName, string serviceName)
    : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default
    )
    {
        try
        {
            var client = clientFactory.CreateClient<Health.HealthClient>(clientName);
            var response = await client.CheckAsync(
                new HealthCheckRequest { Service = serviceName },
                deadline: DateTime.UtcNow.AddSeconds(3),
                cancellationToken: cancellationToken
            );

            return response.Status == HealthCheckResponse.Types.ServingStatus.Serving
                ? HealthCheckResult.Healthy()
                : HealthCheckResult.Unhealthy($"gRPC health status: {response.Status}.");
        }
        catch (RpcException exception)
        {
            return HealthCheckResult.Unhealthy($"gRPC health check failed: {exception.Status.Detail}", exception);
        }
    }
}

public sealed class FlightGrpcHealthCheck(GrpcClientFactory clientFactory)
    : GrpcServiceHealthCheck(clientFactory, "flight-health", "flight.FlightGrpcService");

public sealed class PassengerGrpcHealthCheck(GrpcClientFactory clientFactory)
    : GrpcServiceHealthCheck(clientFactory, "passenger-health", "passenger.PassengerGrpcService");
