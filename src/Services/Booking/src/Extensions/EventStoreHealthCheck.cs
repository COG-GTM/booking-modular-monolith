using EventStore.Client;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Booking.Host.Extensions;

public sealed class EventStoreHealthCheck(EventStoreClient client) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default
    )
    {
        try
        {
            await client
                .ReadAllAsync(Direction.Backwards, Position.End, maxCount: 1, cancellationToken: cancellationToken)
                .AnyAsync(cancellationToken);

            return HealthCheckResult.Healthy();
        }
        catch (Exception ex)
        {
            return new HealthCheckResult(context.Registration.FailureStatus, exception: ex);
        }
    }
}
