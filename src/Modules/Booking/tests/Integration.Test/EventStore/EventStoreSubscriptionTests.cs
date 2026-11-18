using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Api;
using Booking.Booking.Features.CreatingBook.V1;
using Booking.Booking.ValueObjects;
using Booking.Data;
using BuildingBlocks.EventStoreDB.Events;
using BuildingBlocks.EventStoreDB.Serialization;
using BuildingBlocks.EventStoreDB.Subscriptions;
using BuildingBlocks.TestBase;
using EventStore.Client;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Driver;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Integration.Test.EventStore;

public class EventStoreSubscriptionTests : BookingIntegrationTestBase
{
    public EventStoreSubscriptionTests(TestReadFixture<Program, BookingReadDbContext> integrationTestFixture) : base(
        integrationTestFixture)
    {
    }

    [Theory]
    [InlineData("System_Diagnostics_Process")]
    [InlineData("System.Diagnostics.Process")]
    [InlineData("BuildingBlocks_Utils_TypeProvider")]
    [InlineData("Process")]
    public void event_type_names_outside_registered_events_should_not_resolve(string eventTypeName)
    {
        EventTypeMapper.ToType(eventTypeName).Should().BeNull();
    }

    [Fact]
    public void registered_event_type_names_should_resolve()
    {
        EventTypeMapper.ToType(EventTypeMapper.ToName<BookingCreatedDomainEvent>())
            .Should().Be(typeof(BookingCreatedDomainEvent));
    }

    [Fact]
    public async Task subscription_should_skip_unknown_and_park_malformed_events_and_keep_processing()
    {
        var client = Fixture.ServiceProvider.GetRequiredService<EventStoreClient>();
        var streamName = $"booking-test-{Guid.NewGuid():N}";
        var malformedEventId = Uuid.NewUuid();
        var validEvent = CreateBookingCreatedEvent();

        await client.AppendToStreamAsync(streamName, StreamState.Any, new[]
        {
            new EventData(Uuid.NewUuid(), "System_Diagnostics_Process",
                Encoding.UTF8.GetBytes("{\"StartInfo\":{\"FileName\":\"sh\"}}")),
            new EventData(malformedEventId, EventTypeMapper.ToName<BookingCreatedDomainEvent>(),
                Encoding.UTF8.GetBytes("{not json")),
            validEvent.ToJsonEventData()
        });

        var projected = await WaitUntilAsync(async () =>
            await Fixture.ExecuteReadContextAsync(db =>
                db.Booking.Find(x => x.BookId == validEvent.Id).AnyAsync()));

        projected.Should().BeTrue("events after an unknown or malformed event must still be projected");

        var parkedStream = EventStoreDBSubscriptionToAll.GetParkedStreamName(new EventStoreDBSubscriptionToAllOptions().SubscriptionId);
        var parked = await client.ReadStreamAsync(Direction.Forwards, parkedStream, StreamPosition.Start).ToListAsync();

        parked.Select(e => JObject.Parse(Encoding.UTF8.GetString(e.Event.Metadata.Span)))
            .Should().Contain(m => (string)m["OriginalEventId"] == malformedEventId.ToString());
    }

    private static BookingCreatedDomainEvent CreateBookingCreatedEvent() =>
        new(Guid.NewGuid(), PassengerInfo.Of("Test Passenger"),
            Trip.Of("FL-100", Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), DateTime.UtcNow.AddDays(1), 100,
                "test", "12A"));

    private static async Task<bool> WaitUntilAsync(Func<Task<bool>> condition, int timeoutSeconds = 30)
    {
        var deadline = DateTime.UtcNow.AddSeconds(timeoutSeconds);

        while (DateTime.UtcNow < deadline)
        {
            if (await condition())
                return true;

            await Task.Delay(500);
        }

        return false;
    }
}
