using System;
using System.Linq;
using System.Threading.Tasks;
using Api;
using Booking.Booking.Features.CreatingBook.V1;
using Booking.Booking.ValueObjects;
using Booking.Data;
using BuildingBlocks.EventStoreDB.Events;
using BuildingBlocks.EventStoreDB.Serialization;
using BuildingBlocks.EventStoreDB.Subscriptions;
using BuildingBlocks.TestBase;
using BuildingBlocks.Web;
using EventStore.Client;
using FluentAssertions;
using Humanizer;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Integration.Test.Booking;

public class EventStoreSubscriptionCheckpointTests : BookingIntegrationTestBase
{
    private static readonly TimeSpan CheckpointTimeout = TimeSpan.FromSeconds(60);

    public EventStoreSubscriptionCheckpointTests(TestReadFixture<Program, BookingReadDbContext> integrationTestFixture)
        : base(integrationTestFixture) { }

    [Fact]
    public async Task should_checkpoint_the_subscription_to_all_under_the_kebab_cased_app_name()
    {
        // Arrange
        var appName = Fixture.Configuration.GetOptions<AppOptions>(nameof(AppOptions)).Name;
        var expectedSubscriptionId = appName.Kebaberize();
        var expectedStream = $"checkpoint_{expectedSubscriptionId}";
        var eventStoreClient = Fixture.ServiceProvider.GetRequiredService<EventStoreClient>();

        var bookingId = Guid.NewGuid();
        var @event = new BookingCreatedDomainEvent(
            bookingId,
            PassengerInfo.Of("Checkpoint Passenger"),
            Trip.Of("BA123", Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), DateTime.UtcNow.AddDays(1), 100m,
                "checkpoint test", "12A"));

        // Act
        await eventStoreClient.AppendToStreamAsync(
            StreamNameMapper.ToStreamId<global::Booking.Booking.Models.Booking>(bookingId),
            StreamState.NoStream,
            new[] { @event.ToJsonEventData() });

        var checkpoint = await WaitForCheckpointAsync(eventStoreClient, expectedStream);

        // Assert
        expectedSubscriptionId.Should().Be("booking-modular-monolith");
        checkpoint.Should().NotBeNull();
        checkpoint!.SubscriptionId.Should().Be(expectedSubscriptionId);
        checkpoint.Position.Should().NotBeNull();

        (await ReadLastCheckpointAsync(eventStoreClient, "checkpoint_default")).Should().BeNull();
    }

    private static async Task<CheckpointStored?> WaitForCheckpointAsync(EventStoreClient client, string streamName)
    {
        var deadline = DateTime.UtcNow + CheckpointTimeout;

        while (DateTime.UtcNow < deadline)
        {
            var checkpoint = await ReadLastCheckpointAsync(client, streamName);

            if (checkpoint is not null)
            {
                return checkpoint;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(500));
        }

        return null;
    }

    private static async Task<CheckpointStored?> ReadLastCheckpointAsync(EventStoreClient client, string streamName)
    {
        var result = client.ReadStreamAsync(Direction.Backwards, streamName, StreamPosition.End, 1);

        if (await result.ReadState == ReadState.StreamNotFound)
        {
            return null;
        }

        var resolvedEvent = await result.FirstOrDefaultAsync();

        return resolvedEvent.Event is null ? null : resolvedEvent.Deserialize<CheckpointStored>();
    }
}
