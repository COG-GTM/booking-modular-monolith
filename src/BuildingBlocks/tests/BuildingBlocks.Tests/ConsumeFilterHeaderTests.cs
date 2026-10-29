using BuildingBlocks.Core.Event;
using BuildingBlocks.MassTransit;
using BuildingBlocks.PersistMessageProcessor;
using FluentAssertions;
using MassTransit;
using NSubstitute;
using Xunit;

namespace BuildingBlocks.Tests;

public class ConsumeFilterHeaderTests
{
    [Fact]
    public async Task message_id_and_transport_headers_are_added_to_the_inbox_envelope()
    {
        var processor = CreateProcessor(out var id);
        var filter = new ConsumeFilter<First, Received>(processor);
        var context = Context(Guid.NewGuid(), new HeaderValue("x-correlation", "abc"));
        var next = Substitute.For<IPipe<ConsumeContext<Received>>>();

        await filter.Send(context, next);

        _ = processor
            .Received(1)
            .TryAddReceivedMessageAsync(
                Arg.Is<MessageEnvelope>(envelope =>
                    Equals(envelope.Headers["MessageId"], context.MessageId.Value)
                    && Equals(envelope.Headers["x-correlation"], "abc")
                ),
                InboxMessageId.For(context),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task null_message_id_is_rejected_before_recording_or_consuming()
    {
        var processor = CreateProcessor(out _);
        var filter = new ConsumeFilter<First, Received>(processor);
        var context = Context(null, new HeaderValue("x-correlation", "abc"));
        var next = Substitute.For<IPipe<ConsumeContext<Received>>>();

        var act = () => filter.Send(context, next);
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("Inbox requires a MessageId.");
        await next.DidNotReceive().Send(context);

        _ = processor
            .DidNotReceive()
            .TryAddReceivedMessageAsync(Arg.Any<MessageEnvelope>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task processed_inbox_message_skips_next_pipe_and_inbox_processing()
    {
        var processor = Substitute.For<IPersistMessageProcessor<First>>();
        var id = Guid.NewGuid();
        var processed = new PersistMessage(id, nameof(Received), "{}", MessageDeliveryType.Inbox);
        processed.ChangeState(MessageStatus.Processed);
        processor.GetInboxMessageAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(processed);
        var filter = new ConsumeFilter<First, Received>(processor);
        var context = Context(id);
        var next = Substitute.For<IPipe<ConsumeContext<Received>>>();

        await filter.Send(context, next);

        _ = next.DidNotReceive().Send(Arg.Any<ConsumeContext<Received>>());
        _ = processor.DidNotReceive().ProcessInboxAsync(id, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task unprocessed_inbox_message_runs_next_pipe_then_marks_inbox_processed()
    {
        var processor = Substitute.For<IPersistMessageProcessor<First>>();
        var id = Guid.NewGuid();
        processor
            .TryAddReceivedMessageAsync(Arg.Any<MessageEnvelope>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(true);
        var filter = new ConsumeFilter<First, Received>(processor);
        var context = Context(id);
        var next = Substitute.For<IPipe<ConsumeContext<Received>>>();

        await filter.Send(context, next);

        await next.Received(1).Send(context);
        _ = processor.Received(1).ProcessInboxAsync(InboxMessageId.For(context), Arg.Any<CancellationToken>());
    }

    private static IPersistMessageProcessor<First> CreateProcessor(out Guid id)
    {
        var processor = Substitute.For<IPersistMessageProcessor<First>>();
        id = Guid.NewGuid();
        processor
            .TryAddReceivedMessageAsync(Arg.Any<MessageEnvelope>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(true);
        return processor;
    }

    private static ConsumeContext<Received> Context(Guid? messageId, params HeaderValue[] headerValues)
    {
        var context = Substitute.For<ConsumeContext<Received>>();
        context.MessageId.Returns(messageId);
        context.Message.Returns(new Received(Guid.NewGuid()));
        var headers = Substitute.For<Headers>();
        headers.GetEnumerator().Returns(_ => headerValues.AsEnumerable().GetEnumerator());
        context.Headers.Returns(headers);
        return context;
    }

    public sealed class First;

    public sealed record Received(Guid Id) : IIntegrationEvent;
}
