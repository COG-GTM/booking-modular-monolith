using BuildingBlocks.MassTransit;
using FluentAssertions;
using MassTransit;
using Xunit;

namespace Unit.Test.Infrastructure;

using global::Flight;

// ConsumerTypeMap is what lets the scoped ConsumeFilter<T> find the module that owns a message's
// consumer, so it must be built from concrete IConsumer<T> implementations in the scanned assemblies.
public class ConsumerTypeMapTests
{
    public sealed class MappedMessage;

    public sealed class UnmappedMessage;

    public sealed class AbstractOnlyMessage;

    public sealed class MultiMessageA;

    public sealed class MultiMessageB;

    public sealed class MappedConsumer : IConsumer<MappedMessage>
    {
        public Task Consume(ConsumeContext<MappedMessage> context) => Task.CompletedTask;
    }

    public abstract class AbstractConsumer : IConsumer<AbstractOnlyMessage>
    {
        public Task Consume(ConsumeContext<AbstractOnlyMessage> context) => Task.CompletedTask;
    }

    public sealed class MultiMessageConsumer : IConsumer<MultiMessageA>, IConsumer<MultiMessageB>
    {
        public Task Consume(ConsumeContext<MultiMessageA> context) => Task.CompletedTask;

        public Task Consume(ConsumeContext<MultiMessageB> context) => Task.CompletedTask;
    }

    [Fact]
    public void from_assemblies_should_map_message_type_to_its_concrete_consumer()
    {
        var map = ConsumerTypeMap.FromAssemblies([typeof(ConsumerTypeMapTests).Assembly]);

        map.ResolveConsumerType(typeof(MappedMessage)).Should().Be<MappedConsumer>();
    }

    [Fact]
    public void from_assemblies_should_map_every_message_a_consumer_handles()
    {
        var map = ConsumerTypeMap.FromAssemblies([typeof(ConsumerTypeMapTests).Assembly]);

        map.ResolveConsumerType(typeof(MultiMessageA)).Should().Be<MultiMessageConsumer>();
        map.ResolveConsumerType(typeof(MultiMessageB)).Should().Be<MultiMessageConsumer>();
    }

    [Fact]
    public void from_assemblies_should_ignore_abstract_consumers()
    {
        var map = ConsumerTypeMap.FromAssemblies([typeof(ConsumerTypeMapTests).Assembly]);

        map.ResolveConsumerType(typeof(AbstractOnlyMessage)).Should().BeNull();
    }

    [Fact]
    public void unknown_message_type_should_resolve_to_null()
    {
        var map = ConsumerTypeMap.FromAssemblies([typeof(ConsumerTypeMapTests).Assembly]);

        map.ResolveConsumerType(typeof(UnmappedMessage)).Should().BeNull();
    }

    [Fact]
    public void assemblies_without_consumers_should_produce_an_empty_map()
    {
        var map = ConsumerTypeMap.FromAssemblies([typeof(FlightRoot).Assembly]);

        map.ResolveConsumerType(typeof(MappedMessage)).Should().BeNull();
    }

    [Fact]
    public void no_assemblies_should_produce_an_empty_map()
    {
        var map = ConsumerTypeMap.FromAssemblies([]);

        map.ResolveConsumerType(typeof(MappedMessage)).Should().BeNull();
    }

    [Fact]
    public void explicit_map_should_resolve_registered_consumer()
    {
        var map = new ConsumerTypeMap(
            new Dictionary<Type, Type> { [typeof(UnmappedMessage)] = typeof(MappedConsumer) }
        );

        map.ResolveConsumerType(typeof(UnmappedMessage)).Should().Be<MappedConsumer>();
        map.ResolveConsumerType(typeof(MappedMessage)).Should().BeNull();
    }
}
