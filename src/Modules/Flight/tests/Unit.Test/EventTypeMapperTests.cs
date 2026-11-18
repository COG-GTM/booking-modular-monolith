using BuildingBlocks.Core.Event;
using BuildingBlocks.EventStoreDB;
using BuildingBlocks.EventStoreDB.Events;
using BuildingBlocks.EventStoreDB.Subscriptions;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Unit.Test.EventStore;

public class EventTypeMapperTests
{
    [Fact]
    public void registration_should_only_include_concrete_closed_events()
    {
        EventTypeMapper.RegisterEventTypes(typeof(EventTypeMapperTests).Assembly);

        EventTypeMapper.ToType(StoredName(typeof(ConcreteEvent))).Should().Be(typeof(ConcreteEvent));
        foreach (
            var type in new[]
            {
                typeof(AbstractEvent),
                typeof(NotAnEvent),
                typeof(OpenEvent<>),
                typeof(DerivedStreamEvent),
                typeof(IEvent),
            }
        )
            EventTypeMapper.ToType(StoredName(type)).Should().BeNull();
    }

    [Fact]
    public void repeated_and_empty_registration_should_preserve_round_trip()
    {
        EventTypeMapper.RegisterEventTypes(typeof(EventTypeMapperTests).Assembly);
        var name = EventTypeMapper.ToName<ConcreteEvent>();

        EventTypeMapper.RegisterEventTypes();
        EventTypeMapper.RegisterEventTypes(
            typeof(EventTypeMapperTests).Assembly,
            typeof(EventTypeMapperTests).Assembly
        );

        name.Should().Be(StoredName(typeof(ConcreteEvent)));
        EventTypeMapper.ToName<ConcreteEvent>().Should().Be(name);
        EventTypeMapper.ToType(name).Should().Be(typeof(ConcreteEvent));
    }

    [Fact]
    public void runtime_type_names_should_not_resolve()
    {
        foreach (
            var name in new[]
            {
                typeof(System.Diagnostics.Process).AssemblyQualifiedName!,
                "System.Diagnostics.Process",
                "Process",
                "",
            }
        )
            EventTypeMapper.ToType(name).Should().BeNull();
    }

    [Fact]
    public void add_event_store_should_register_application_events_and_checkpoint()
    {
        var services = new ServiceCollection();
        services.AddEventStore(new ConfigurationBuilder().Build(), typeof(EventTypeMapperTests).Assembly);

        EventTypeMapper.ToType(StoredName(typeof(CheckpointStored))).Should().Be(typeof(CheckpointStored));
        EventTypeMapper.ToType(StoredName(typeof(ConcreteEvent))).Should().Be(typeof(ConcreteEvent));

        EventTypeMapper.RegisterEventTypes(typeof(StreamEvent).Assembly);
        EventTypeMapper.ToType(StoredName(typeof(StreamEvent))).Should().BeNull();
        EventTypeMapper.ToType(StoredName(typeof(StreamEvent<>))).Should().BeNull();
    }

    private static string StoredName(Type type) => type.FullName!.Replace(".", "_", StringComparison.Ordinal);

    public class ConcreteEvent : IEvent;

    public abstract class AbstractEvent : IEvent;

    public class OpenEvent<T> : IEvent;

    public class NotAnEvent;

    public class DerivedStreamEvent() : StreamEvent(new ConcreteEvent(), new EventMetadata(0, 0));
}
