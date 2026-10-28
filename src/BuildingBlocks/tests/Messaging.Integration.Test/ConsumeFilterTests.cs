using BuildingBlocks.MassTransit;
using BuildingBlocks.PersistMessageProcessor;
using MassTransit;
using NSubstitute;
using Xunit;

namespace Messaging.Integration.Test;

public class ConsumeFilterTests
{
    [Fact]
    public async Task Rejects_a_delivery_without_a_message_id_before_touching_the_inbox()
    {
        var processor = Substitute.For<IPersistMessageProcessor>();
        var next = Substitute.For<IPipe<ConsumeContext<ProbeEvent>>>();
        var context = Substitute.For<ConsumeContext<ProbeEvent>>();
        context.MessageId.Returns((Guid?)null);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => new ConsumeFilter<ProbeEvent>(processor).Send(context, next)
        );

        await processor
            .DidNotReceiveWithAnyArgs()
            .ExecuteInboxAsync(default, default!, default!, Arg.Any<CancellationToken>());
        await next.DidNotReceiveWithAnyArgs().Send(default!);
    }

    [Fact]
    public async Task Keys_the_receipt_by_message_id_and_receiving_endpoint_and_runs_the_pipe_inside_it()
    {
        var messageId = Guid.NewGuid();
        using var cancellation = new CancellationTokenSource();
        var processor = Substitute.For<IPersistMessageProcessor>();
        Func<Task>? consume = null;
        processor
            .ExecuteInboxAsync(
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                Arg.Do<Func<Task>>(x => consume = x),
                Arg.Any<CancellationToken>()
            )
            .Returns(Task.CompletedTask);
        var next = Substitute.For<IPipe<ConsumeContext<ProbeEvent>>>();
        var receive = Substitute.For<ReceiveContext>();
        receive.InputAddress.Returns(new Uri("rabbitmq://broker:5672/passenger-register-new-user-handler"));
        var context = Substitute.For<ConsumeContext<ProbeEvent>>();
        context.MessageId.Returns(messageId);
        context.ReceiveContext.Returns(receive);
        context.CancellationToken.Returns(cancellation.Token);

        await new ConsumeFilter<ProbeEvent>(processor).Send(context, next);

        await processor
            .Received(1)
            .ExecuteInboxAsync(
                messageId,
                "/passenger-register-new-user-handler",
                Arg.Any<Func<Task>>(),
                cancellation.Token
            );
        await next.DidNotReceiveWithAnyArgs().Send(default!);
        Assert.NotNull(consume);
        await consume();
        await next.Received(1).Send(context);
    }
}
