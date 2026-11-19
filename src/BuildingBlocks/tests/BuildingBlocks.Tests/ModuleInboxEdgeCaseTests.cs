using BuildingBlocks.Core.Event;
using BuildingBlocks.MassTransit;
using BuildingBlocks.PersistMessageProcessor;
using FluentAssertions;
using MassTransit;
using NSubstitute;
using Xunit;

namespace BuildingBlocks.Tests;

public class ModuleInboxEdgeCaseTests
{
    [Fact]
    public async Task concurrent_delivery_that_loses_the_insert_does_not_invoke_the_consumer()
    {
        var processor = Substitute.For<IPersistMessageProcessor<ModuleInboxEdgeCaseTests>>();
        var context = Context(Guid.NewGuid(), "passenger");
        var next = Substitute.For<IPipe<ConsumeContext<Received>>>();
        var id = InboxMessageId.For(context);
        processor.TryAddReceivedMessageAsync(Arg.Any<MessageEnvelope>(), id, context.CancellationToken).Returns(false);

        await new ConsumeFilter<ModuleInboxEdgeCaseTests, Received>(processor).Send(context, next);

        await processor
            .Received(1)
            .TryAddReceivedMessageAsync(
                Arg.Is<MessageEnvelope>(x =>
                    x.Message == context.Message && (Guid)x.Headers["MessageId"]! == context.MessageId
                ),
                id,
                context.CancellationToken
            );
        await next.DidNotReceive().Send(Arg.Any<ConsumeContext<Received>>());
        await processor.DidNotReceive().ProcessInboxAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task missing_transport_id_fails_before_reading_or_writing_the_inbox()
    {
        var processor = Substitute.For<IPersistMessageProcessor<ModuleInboxEdgeCaseTests>>();
        var next = Substitute.For<IPipe<ConsumeContext<Received>>>();
        var act = () =>
            new ConsumeFilter<ModuleInboxEdgeCaseTests, Received>(processor).Send(Context(null, "passenger"), next);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("Inbox requires a MessageId.");
        processor.ReceivedCalls().Should().BeEmpty();
        next.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public void inbox_identity_is_stable_per_endpoint_but_distinct_from_other_endpoints_and_outbox()
    {
        var id = Guid.NewGuid();
        var first = InboxMessageId.For(Context(id, "passenger"));
        first.Should().Be(InboxMessageId.For(Context(id, "passenger")));
        first.Should().NotBe(InboxMessageId.For(Context(id, "flight")));
        first.Should().NotBe(id);
    }

    private static ConsumeContext<Received> Context(Guid? id, string endpoint)
    {
        var context = Substitute.For<ConsumeContext<Received>>();
        context.MessageId.Returns(id);
        context.Message.Returns(new Received(Guid.NewGuid()));
        var receive = Substitute.For<ReceiveContext>();
        receive.InputAddress.Returns(new Uri($"rabbitmq://localhost/{endpoint}"));
        context.ReceiveContext.Returns(receive);
        var headers = Substitute.For<Headers>();
        headers.GetEnumerator().Returns(_ => Enumerable.Empty<HeaderValue>().GetEnumerator());
        context.Headers.Returns(headers);
        return context;
    }

    public sealed record Received(Guid Id) : IIntegrationEvent;
}
