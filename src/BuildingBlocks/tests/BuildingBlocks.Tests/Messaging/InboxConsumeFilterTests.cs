using BuildingBlocks.Core.Event;
using BuildingBlocks.MassTransit;
using BuildingBlocks.PersistMessageProcessor;
using FluentAssertions;
using MassTransit;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace BuildingBlocks.Tests.Messaging;

public class InboxConsumeFilterTests
{
    private static readonly Uri InputAddress = new("rabbitmq://localhost/passenger.register-new-user-handler");

    [Fact]
    public void inbox_id_is_stable_per_endpoint_and_message()
    {
        var messageId = Guid.NewGuid();

        InboxMessageId.For("passenger.a", messageId).Should().Be(InboxMessageId.For("passenger.a", messageId));
        InboxMessageId.For("passenger.a", messageId).Should().NotBe(InboxMessageId.For("booking.a", messageId));
        InboxMessageId.For("passenger.a", messageId).Should().NotBe(messageId);
    }

    [Fact]
    public async Task first_delivery_is_recorded_consumed_and_marked_processed()
    {
        var (processor, context, next, inboxId) = Arrange(existing: null);

        await new ConsumeFilter<TestEvent>(processor, NullLogger<ConsumeFilter<TestEvent>>.Instance).Send(
            context,
            next
        );

        await processor
            .Received(1)
            .AddReceivedMessageAsync(Arg.Any<MessageEnvelope>(), inboxId, Arg.Any<CancellationToken>());
        await next.Received(1).Send(context);
        await processor.Received(1).ProcessInboxAsync(inboxId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task redelivery_of_a_processed_message_is_skipped()
    {
        var (processor, context, next, inboxId) = Arrange(Processed());

        await new ConsumeFilter<TestEvent>(processor, NullLogger<ConsumeFilter<TestEvent>>.Instance).Send(
            context,
            next
        );

        await next.DidNotReceive().Send(Arg.Any<ConsumeContext<TestEvent>>());
        await processor
            .DidNotReceive()
            .AddReceivedMessageAsync(Arg.Any<MessageEnvelope>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        await processor.DidNotReceive().ProcessInboxAsync(inboxId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task retry_of_an_in_progress_message_reuses_the_inbox_row()
    {
        var (processor, context, next, inboxId) = Arrange(
            new PersistMessage(Guid.NewGuid(), "t", "{}", MessageDeliveryType.Inbox)
        );

        await new ConsumeFilter<TestEvent>(processor, NullLogger<ConsumeFilter<TestEvent>>.Instance).Send(
            context,
            next
        );

        await processor
            .DidNotReceive()
            .AddReceivedMessageAsync(Arg.Any<MessageEnvelope>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        await next.Received(1).Send(context);
        await processor.Received(1).ProcessInboxAsync(inboxId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task failed_consumer_leaves_the_message_unprocessed_so_retry_can_run_it_again()
    {
        var (processor, context, next, inboxId) = Arrange(existing: null);
        next.Send(context).Returns(Task.FromException(new InvalidOperationException("boom")));

        var act = () =>
            new ConsumeFilter<TestEvent>(processor, NullLogger<ConsumeFilter<TestEvent>>.Instance).Send(context, next);

        await act.Should().ThrowAsync<InvalidOperationException>();
        await processor.DidNotReceive().ProcessInboxAsync(inboxId, Arg.Any<CancellationToken>());
    }

    private static PersistMessage Processed()
    {
        var message = new PersistMessage(Guid.NewGuid(), "t", "{}", MessageDeliveryType.Inbox);
        message.ChangeState(MessageStatus.Processed);
        return message;
    }

    private static (
        IPersistMessageProcessor,
        ConsumeContext<TestEvent>,
        IPipe<ConsumeContext<TestEvent>>,
        Guid
    ) Arrange(PersistMessage? existing)
    {
        var messageId = Guid.NewGuid();
        var inboxId = InboxMessageId.For(InputAddress.AbsolutePath.Trim('/'), messageId);

        var receiveContext = Substitute.For<ReceiveContext>();
        receiveContext.InputAddress.Returns(InputAddress);

        var context = Substitute.For<ConsumeContext<TestEvent>>();
        context.MessageId.Returns(messageId);
        context.Message.Returns(new TestEvent("x"));
        context.ReceiveContext.Returns(receiveContext);
        context.Headers.Returns(Substitute.For<Headers>());
        context.CancellationToken.Returns(CancellationToken.None);

        var processor = Substitute.For<IPersistMessageProcessor>();
        processor.GetInboxMessageAsync(inboxId, Arg.Any<CancellationToken>()).Returns(existing);
        processor
            .AddReceivedMessageAsync(Arg.Any<MessageEnvelope>(), inboxId, Arg.Any<CancellationToken>())
            .Returns(inboxId);

        var next = Substitute.For<IPipe<ConsumeContext<TestEvent>>>();
        next.Send(context).Returns(Task.CompletedTask);

        return (processor, context, next, inboxId);
    }

    public sealed record TestEvent(string Value);
}
