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

// The inbox filter writes the received message to the store owned by the consumer's module and only
// lets the consumer run for messages that were not already processed.
public class InboxConsumeFilterTests
{
    public sealed class TestMessage
    {
        public string Value { get; init; } = "";
    }

    // lives in this test assembly, so its "module" is the test assembly
    public sealed class TestConsumer : IConsumer<TestMessage>
    {
        public Task Consume(ConsumeContext<TestMessage> context) => Task.CompletedTask;
    }

    private readonly IPersistMessageProcessor _consumerModuleProcessor = Substitute.For<IPersistMessageProcessor>();
    private readonly IPersistMessageProcessor _flightProcessor = Substitute.For<IPersistMessageProcessor>();
    private readonly IPipe<ConsumerConsumeContext<TestConsumer, TestMessage>> _next = Substitute.For<
        IPipe<ConsumerConsumeContext<TestConsumer, TestMessage>>
    >();

    private ConsumeFilter<TestConsumer, TestMessage> CreateFilter()
    {
        var services = new ServiceCollection();
        services.AddKeyedSingleton(typeof(TestConsumer).Assembly, _consumerModuleProcessor);
        services.AddKeyedSingleton(typeof(FlightRoot).Assembly, _flightProcessor);

        return new ConsumeFilter<TestConsumer, TestMessage>(services.BuildServiceProvider());
    }

    private static ConsumerConsumeContext<TestConsumer, TestMessage> CreateContext(
        TestMessage message,
        params HeaderValue[] headers
    )
    {
        var messageHeaders = Substitute.For<Headers>();
        messageHeaders.GetEnumerator().Returns(_ => headers.AsEnumerable().GetEnumerator());

        var context = Substitute.For<ConsumerConsumeContext<TestConsumer, TestMessage>>();
        context.Message.Returns(message);
        context.Headers.Returns(messageHeaders);

        return context;
    }

    [Fact]
    public async Task new_message_should_be_stored_in_the_consumer_module_inbox_then_consumed()
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

        await CreateFilter().Send(context, _next);

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

        await CreateFilter().Send(context, _next);

        await _next.DidNotReceive().Send(Arg.Any<ConsumerConsumeContext<TestConsumer, TestMessage>>());
        await _consumerModuleProcessor.DidNotReceive().ProcessInboxAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void consumer_module_without_an_inbox_store_should_fail_fast()
    {
        var services = new ServiceCollection();
        services.AddKeyedSingleton(typeof(FlightRoot).Assembly, _flightProcessor);

        var act = () => new ConsumeFilter<TestConsumer, TestMessage>(services.BuildServiceProvider());

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void probe_should_describe_the_inbox_filter()
    {
        var probeContext = Substitute.For<ProbeContext>();

        CreateFilter().Probe(probeContext);

        probeContext.Received(1).CreateFilterScope("persistMessageInbox");
    }
}
