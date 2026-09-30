using Grpc.Core;
using Grpc.Health.V1;
using Grpc.Net.ClientFactory;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace BuildingBlocks.Grpc;

public class GrpcServiceHealthCheck : IHealthCheck
{
    private readonly GrpcClientFactory _clientFactory;
    private readonly string _clientName;

    public GrpcServiceHealthCheck(GrpcClientFactory clientFactory, string clientName)
    {
        _clientFactory = clientFactory;
        _clientName = clientName;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default
    )
    {
        try
        {
            var client = _clientFactory.CreateClient<Health.HealthClient>(_clientName);
            var response = await client.CheckAsync(new HealthCheckRequest(), cancellationToken: cancellationToken);

            return response.Status == HealthCheckResponse.Types.ServingStatus.Serving
                ? HealthCheckResult.Healthy()
                : new HealthCheckResult(context.Registration.FailureStatus, $"gRPC health status: {response.Status}");
        }
        catch (RpcException exception)
        {
            return new HealthCheckResult(context.Registration.FailureStatus, exception.Status.ToString(), exception);
        }
    }
}
