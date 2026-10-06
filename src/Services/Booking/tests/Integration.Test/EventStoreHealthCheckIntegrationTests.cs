using Booking.Host.Extensions;
using EventStore.Client;
using FluentAssertions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Testcontainers.EventStoreDb;
using Xunit;

namespace Booking.Host.Integration.Test;

public class EventStoreHealthCheckIntegrationTests : IAsyncLifetime
{
    private readonly EventStoreDbContainer _container = new EventStoreDbBuilder()
        .WithImage("eventstore/eventstore:24.2.0-jammy")
        .Build();

    public Task InitializeAsync() => _container.StartAsync();

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();

    [Fact]
    public async Task reports_healthy_against_a_running_eventstore_without_writing_any_events()
    {
        await using var client = new EventStoreClient(
            EventStoreClientSettings.Create(_container.GetConnectionString())
        );
        var healthCheck = new EventStoreHealthCheck(client);

        var result = await healthCheck.CheckHealthAsync(CreateContext(healthCheck));

        result.Status.Should().Be(HealthStatus.Healthy);
        result.Exception.Should().BeNull();
    }

    [Fact]
    public async Task reports_healthy_when_only_system_events_exist_in_all_stream()
    {
        await using var client = new EventStoreClient(
            EventStoreClientSettings.Create(_container.GetConnectionString())
        );
        var healthCheck = new EventStoreHealthCheck(client);

        var tail = await client.ReadAllAsync(Direction.Backwards, Position.End, maxCount: 1).ToListAsync();
        tail.Should().ContainSingle("a fresh EventStoreDB still has system events in $all");

        var result = await healthCheck.CheckHealthAsync(CreateContext(healthCheck));

        result.Status.Should().Be(HealthStatus.Healthy);
    }

    [Fact]
    public async Task reports_unhealthy_once_eventstore_is_stopped()
    {
        var settings = EventStoreClientSettings.Create(_container.GetConnectionString());
        settings.DefaultDeadline = TimeSpan.FromSeconds(5);
        settings.ConnectivitySettings.MaxDiscoverAttempts = 1;
        await using var client = new EventStoreClient(settings);
        var healthCheck = new EventStoreHealthCheck(client);

        (await healthCheck.CheckHealthAsync(CreateContext(healthCheck))).Status.Should().Be(HealthStatus.Healthy);

        await _container.StopAsync();

        var result = await healthCheck.CheckHealthAsync(CreateContext(healthCheck));

        result.Status.Should().Be(HealthStatus.Unhealthy);
        result.Exception.Should().NotBeNull();
    }

    private static HealthCheckContext CreateContext(IHealthCheck healthCheck) =>
        new()
        {
            Registration = new HealthCheckRegistration(
                HealthCheckExtensions.EventStoreCheckName,
                healthCheck,
                HealthStatus.Unhealthy,
                tags: null
            ),
        };
}
