using BuildingBlocks.Core.Event;
using BuildingBlocks.MassTransit;
using BuildingBlocks.PersistMessageProcessor;
using FluentAssertions;
using MassTransit;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;

namespace Unit.Test.Infrastructure;

using global::Flight;

// ConsumeFilter<T> is the message-only variant registered through UseConsumeFilter. It has no consumer
// type parameter, so it must look the consumer up in ConsumerTypeMap to pick the owning module's inbox.
public class ScopedInboxConsumeFilterTests
{
    public sealed class TestMessage
    {
        public string Value { get; init; } = "";
    }

    public sealed class OrphanMessage;

    // lives in this test assembly, so its "module" is the test assembly
    public sealed class TestConsumer : IConsumer<TestMessage>
    {
        public Task Consume(ConsumeContext<TestMessage> context) => Task.CompletedTask;
    }

    private readonly IPersistMessageProcessor _consumerModuleProcessor = Substitute.For<IPersistMessageProcessor>();
    private readonly IPersistMessageProcessor _flightProcessor = Substitute.For<IPersistMessageProcessor>();
    private readonly IPipe<ConsumeContext<TestMessage>> _next = Substitute.For<IPipe<ConsumeContext<TestMessage>>>();

    private IServiceProvider CreateServiceProvider()
    {
        var services = new ServiceCollection();
        services.AddKeyedSingleton(typeof(TestConsumer).Assembly, _consumerModuleProcessor);
        services.AddKeyedSingleton(typeof(FlightRoot).Assembly, _flightProcessor);

        return services.BuildServiceProvider();
    }

    private ConsumeFilter<TMessage> CreateFilter<TMessage>(ConsumerTypeMap? consumerTypeMap = null)
        where TMessage : class
    {
        consumerTypeMap ??= new ConsumerTypeMap(
            new Dictionary<Type, Type> { [typeof(TestMessage)] = typeof(TestConsumer) }
        );

        return new ConsumeFilter<TMessage>(CreateServiceProvider(), consumerTypeMap);
    }

    private static ConsumeContext<TMessage> CreateContext<TMessage>(TMessage message, params HeaderValue[] headers)
        where TMessage : class
    {
        var messageHeaders = Substitute.For<Headers>();
        messageHeaders.GetEnumerator().Returns(_ => headers.AsEnumerable().GetEnumerator());

        var context = Substitute.For<ConsumeContext<TMessage>>();
        context.Message.Returns(message);
        context.Headers.Returns(messageHeaders);

        return context;
    }

    [Fact]
    public async Task new_message_should_be_stored_in_the_resolved_module_inbox_then_consumed()
    {
        var messageId = Guid.NewGuid();
        var message = new TestMessage { Value = "hello" };
        var context = CreateContext(message, new HeaderValue("correlation", "abc"));

        _consumerModuleProcessor
            .AddReceivedMessageAsync(Arg.Any<MessageEnvelope>(), Arg.Any<CancellationToken>())
            .Returns(messageId);
        _consumerModuleProcessor
            .ExistMessageAsync(messageId, Arg.Any<CancellationToken>())
            .Returns((PersistMessage?)null);

        await CreateFilter<TestMessage>().Send(context, _next);

        await _consumerModuleProcessor
            .Received(1)
            .AddReceivedMessageAsync(
                Arg.Is<MessageEnvelope>(envelope =>
                    ReferenceEquals(envelope.Message, message)
                    && envelope.Headers.Count == 1
                    && Equals(envelope.Headers["correlation"], "abc")
                ),
                Arg.Any<CancellationToken>()
            );
        await _next.Received(1).Send(context);
        await _consumerModuleProcessor.Received(1).ProcessInboxAsync(messageId, Arg.Any<CancellationToken>());

        // another module's inbox is never touched
        _flightProcessor.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public async Task consumer_should_run_before_the_inbox_record_is_marked_processed()
    {
        var messageId = Guid.NewGuid();
        var context = CreateContext(new TestMessage { Value = "ordered" });
        var calls = new List<string>();

        _consumerModuleProcessor
            .AddReceivedMessageAsync(Arg.Any<MessageEnvelope>(), Arg.Any<CancellationToken>())
            .Returns(messageId);
        _consumerModuleProcessor
            .ExistMessageAsync(messageId, Arg.Any<CancellationToken>())
            .Returns((PersistMessage?)null);
        _consumerModuleProcessor
            .When(x => x.ProcessInboxAsync(messageId, Arg.Any<CancellationToken>()))
            .Do(_ => calls.Add("process-inbox"));
        _next.When(x => x.Send(context)).Do(_ => calls.Add("consumer"));

        await CreateFilter<TestMessage>().Send(context, _next);

        calls.Should().Equal("consumer", "process-inbox");
    }

    [Fact]
    public async Task already_processed_message_should_not_reach_the_consumer()
    {
        var messageId = Guid.NewGuid();
        var context = CreateContext(new TestMessage { Value = "duplicate" });

        _consumerModuleProcessor
            .AddReceivedMessageAsync(Arg.Any<MessageEnvelope>(), Arg.Any<CancellationToken>())
            .Returns(messageId);
        _consumerModuleProcessor
            .ExistMessageAsync(messageId, Arg.Any<CancellationToken>())
            .Returns(new PersistMessage(messageId, nameof(TestMessage), "{}", MessageDeliveryType.Inbox));

        await CreateFilter<TestMessage>().Send(context, _next);

        await _next.DidNotReceive().Send(Arg.Any<ConsumeContext<TestMessage>>());
        await _consumerModuleProcessor.DidNotReceive().ProcessInboxAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task message_without_a_mapped_consumer_should_fail_fast_and_skip_the_consumer()
    {
        var next = Substitute.For<IPipe<ConsumeContext<OrphanMessage>>>();
        var context = CreateContext(new OrphanMessage());

        var act = () => CreateFilter<OrphanMessage>().Send(context, next);

        (await act.Should().ThrowAsync<InvalidOperationException>()).WithMessage($"*{nameof(OrphanMessage)}*");
        await next.DidNotReceive().Send(Arg.Any<ConsumeContext<OrphanMessage>>());
        _consumerModuleProcessor.ReceivedCalls().Should().BeEmpty();
        _flightProcessor.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public async Task consumer_module_without_an_inbox_store_should_fail_fast()
    {
        var services = new ServiceCollection();
        services.AddKeyedSingleton(typeof(FlightRoot).Assembly, _flightProcessor);
        var consumerTypeMap = new ConsumerTypeMap(
            new Dictionary<Type, Type> { [typeof(TestMessage)] = typeof(TestConsumer) }
        );
        var filter = new ConsumeFilter<TestMessage>(services.BuildServiceProvider(), consumerTypeMap);
        var context = CreateContext(new TestMessage { Value = "no-store" });

        var act = () => filter.Send(context, _next);

        await act.Should().ThrowAsync<InvalidOperationException>();
        await _next.DidNotReceive().Send(Arg.Any<ConsumeContext<TestMessage>>());
        _flightProcessor.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public async Task filter_built_from_scanned_assemblies_should_route_to_the_consumer_module()
    {
        var messageId = Guid.NewGuid();
        var context = CreateContext(new TestMessage { Value = "scanned" });
        var consumerTypeMap = ConsumerTypeMap.FromAssemblies([typeof(ScopedInboxConsumeFilterTests).Assembly]);

        _consumerModuleProcessor
            .AddReceivedMessageAsync(Arg.Any<MessageEnvelope>(), Arg.Any<CancellationToken>())
            .Returns(messageId);
        _consumerModuleProcessor
            .ExistMessageAsync(messageId, Arg.Any<CancellationToken>())
            .Returns((PersistMessage?)null);

        await CreateFilter<TestMessage>(consumerTypeMap).Send(context, _next);

        await _next.Received(1).Send(context);
        await _consumerModuleProcessor.Received(1).ProcessInboxAsync(messageId, Arg.Any<CancellationToken>());
        _flightProcessor.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public void probe_should_describe_the_inbox_filter()
    {
        var probeContext = Substitute.For<ProbeContext>();

        CreateFilter<TestMessage>().Probe(probeContext);

        probeContext.Received(1).CreateFilterScope("persistMessageInbox");
    }
}
