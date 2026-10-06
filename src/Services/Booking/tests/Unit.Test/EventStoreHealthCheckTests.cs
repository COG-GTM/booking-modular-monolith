using Booking.Host.Extensions;
using EventStore.Client;
using FluentAssertions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Xunit;

namespace Booking.Host.Unit.Test;

public class EventStoreHealthCheckTests
{
    [Theory]
    [InlineData(HealthStatus.Unhealthy)]
    [InlineData(HealthStatus.Degraded)]
    public async Task reports_registration_failure_status_when_eventstore_is_unreachable(HealthStatus failureStatus)
    {
        using var client = CreateUnreachableClient();
        var healthCheck = new EventStoreHealthCheck(client);

        var result = await healthCheck.CheckHealthAsync(CreateContext(healthCheck, failureStatus));

        result.Status.Should().Be(failureStatus);
        result.Exception.Should().NotBeNull();
    }

    [Fact]
    public async Task reports_failure_status_instead_of_throwing_when_check_is_cancelled()
    {
        using var client = CreateUnreachableClient();
        var healthCheck = new EventStoreHealthCheck(client);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var result = await healthCheck.CheckHealthAsync(CreateContext(healthCheck, HealthStatus.Unhealthy), cts.Token);

        result.Status.Should().Be(HealthStatus.Unhealthy);
        result.Exception.Should().NotBeNull();
    }

    private static EventStoreClient CreateUnreachableClient()
    {
        var settings = EventStoreClientSettings.Create("esdb://127.0.0.1:1?tls=false");
        settings.DefaultDeadline = TimeSpan.FromSeconds(2);
        settings.ConnectivitySettings.MaxDiscoverAttempts = 1;
        settings.ConnectivitySettings.DiscoveryInterval = TimeSpan.FromMilliseconds(10);
        settings.ConnectivitySettings.GossipTimeout = TimeSpan.FromMilliseconds(500);

        return new EventStoreClient(settings);
    }

    private static HealthCheckContext CreateContext(IHealthCheck healthCheck, HealthStatus failureStatus) =>
        new()
        {
            Registration = new HealthCheckRegistration(
                HealthCheckExtensions.EventStoreCheckName,
                healthCheck,
                failureStatus,
                tags: null
            ),
        };
}
