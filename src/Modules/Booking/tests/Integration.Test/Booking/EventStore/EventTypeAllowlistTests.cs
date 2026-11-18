using System;
using System.Collections.Generic;
using System.Text;
using Booking;
using Booking.Booking.Features.CreatingBook.V1;
using Booking.Booking.ValueObjects;
using BuildingBlocks.Contracts.EventBus.Messages;
using BuildingBlocks.Core.Event;
using BuildingBlocks.EventStoreDB;
using BuildingBlocks.EventStoreDB.Events;
using BuildingBlocks.EventStoreDB.Serialization;
using BuildingBlocks.EventStoreDB.Subscriptions;
using BuildingBlocks.Utils;
using EventStore.Client;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;
using Xunit;

namespace Integration.Test.Booking.EventStore;

public class EventTypeAllowlistTests
{
    private const string BookingCreatedEventTypeName =
        "Booking_Booking_Features_CreatingBook_V1_BookingCreatedDomainEvent";

    public EventTypeAllowlistTests()
    {
        new ServiceCollection().AddEventStore(new ConfigurationBuilder().Build(), typeof(BookingRoot).Assembly);
    }

    [Theory]
    [InlineData("FileStream")]
    [InlineData("System_IO_FileStream")]
    [InlineData("System_IO_FileInfo")]
    [InlineData("System_Diagnostics_Process")]
    [InlineData("System_Net_WebClient")]
    public void to_type_should_not_resolve_unregistered_framework_types(string eventTypeName)
    {
        EventTypeMapper.ToType(eventTypeName).Should().BeNull();
    }

    [Fact]
    public void to_type_should_resolve_registered_events()
    {
        EventTypeMapper.ToType(BookingCreatedEventTypeName).Should().Be(typeof(BookingCreatedDomainEvent));
        EventTypeMapper.ToType(EventTypeMapper.ToName<CheckpointStored>()).Should().Be(typeof(CheckpointStored));
    }

    [Fact]
    public void stream_event_with_unregistered_type_should_be_ignored()
    {
        var resolvedEvent = CreateResolvedEvent("System_IO_FileInfo", "{\"fileName\":\"/tmp/pwned\"}");

        resolvedEvent.Deserialize().Should().BeNull();
        resolvedEvent.ToStreamEvent().Should().BeNull();
    }

    [Fact]
    public void registered_booking_event_should_round_trip_through_event_store_serializer()
    {
        var @event = new BookingCreatedDomainEvent(
            Guid.NewGuid(),
            PassengerInfo.Of("Mallory"),
            Trip.Of("BZ1", Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), new DateTime(2026, 11, 18, 0, 0, 0, DateTimeKind.Utc), 100m, "desc", "12C"));

        var eventData = @event.ToJsonEventData();
        var resolvedEvent = CreateResolvedEvent(eventData.Type, Encoding.UTF8.GetString(eventData.Data.Span));

        var streamEvent = resolvedEvent.ToStreamEvent();

        eventData.Type.Should().Be(BookingCreatedEventTypeName);
        streamEvent.Should().BeOfType<StreamEvent<BookingCreatedDomainEvent>>();
        var data = ((StreamEvent<BookingCreatedDomainEvent>)streamEvent!).Data;
        data.Id.Should().Be(@event.Id);
        data.PassengerInfo.Should().Be(@event.PassengerInfo);
        data.Trip.Should().Be(@event.Trip);
    }

    [Fact]
    public void contract_resolver_should_not_invoke_non_public_constructors_without_json_constructor()
    {
        NonPublicConstructorProbe.Invoked = false;

        var act = () => "{\"path\":\"/tmp/pwned\"}".FromJson(typeof(NonPublicConstructorProbe));

        act.Should().Throw<JsonSerializationException>();
        NonPublicConstructorProbe.Invoked.Should().BeFalse();
    }

    [Fact]
    public void persist_message_type_lookup_should_only_resolve_expected_message_types()
    {
        TypeProvider.GetFirstMatchingTypeFromCurrentDomainAssembly<IEvent>("System.IO.FileInfo").Should().BeNull();
        TypeProvider.GetFirstMatchingTypeFromCurrentDomainAssembly<IEvent>("FileInfo").Should().BeNull();
        TypeProvider.GetFirstMatchingTypeFromCurrentDomainAssembly<IInternalCommand>(typeof(BookingCreated).FullName!)
            .Should().BeNull();
        TypeProvider.GetFirstMatchingTypeFromCurrentDomainAssembly<IEvent>(typeof(BookingCreated).FullName!)
            .Should().Be(typeof(BookingCreated));
    }

    private static ResolvedEvent CreateResolvedEvent(string eventType, string json)
    {
        var metadata = new Dictionary<string, string>
        {
            ["type"] = eventType,
            ["created"] = DateTime.UtcNow.Ticks.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["content-type"] = "application/json",
        };

        var record = new EventRecord(
            "attacker-stream",
            Uuid.NewUuid(),
            StreamPosition.Start,
            new Position(1, 1),
            metadata,
            Encoding.UTF8.GetBytes(json),
            Array.Empty<byte>());

        return new ResolvedEvent(record, null, null);
    }

    private sealed class NonPublicConstructorProbe
    {
        private NonPublicConstructorProbe(string path)
        {
            Path = path;
            Invoked = true;
        }

        public static bool Invoked { get; set; }

        public string Path { get; }
    }
}
