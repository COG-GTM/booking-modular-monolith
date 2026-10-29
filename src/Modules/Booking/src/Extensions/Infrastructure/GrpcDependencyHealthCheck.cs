using Grpc.Core;
using Grpc.Health.V1;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Booking.Extensions.Infrastructure;

/// <summary>Readiness check: asks a downstream service's standard gRPC health service whether it is SERVING.</summary>
internal sealed class GrpcDependencyHealthCheck : IHealthCheck
{
    private readonly Health.HealthClient _client;
    private readonly TimeSpan _timeout;

    public GrpcDependencyHealthCheck(Health.HealthClient client, TimeSpan timeout)
    {
        _client = client;
        _timeout = timeout;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _client.CheckAsync(
                new HealthCheckRequest(),
                deadline: DateTime.UtcNow.Add(_timeout),
                cancellationToken: cancellationToken);

            return response.Status == HealthCheckResponse.Types.ServingStatus.Serving
                ? HealthCheckResult.Healthy()
                : new HealthCheckResult(context.Registration.FailureStatus, $"Dependency reports {response.Status}.");
        }
        catch (RpcException ex)
        {
            return new HealthCheckResult(context.Registration.FailureStatus, $"gRPC health call failed: {ex.StatusCode}.");
        }
    }
}
