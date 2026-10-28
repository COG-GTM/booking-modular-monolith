using BuildingBlocks.MassTransit;
using BuildingBlocks.PersistMessageProcessor;
using MassTransit;
using NSubstitute;
using Xunit;

namespace Messaging.Integration.Test;

public class ConsumeFilterTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Rejects_an_endpoint_without_an_unambiguous_module_store(bool hasConsumer)
    {
        var receive = Substitute.For<ReceiveContext>();
        receive.InputAddress.Returns(new Uri("rabbitmq://broker/passenger-handler"));
        var context = Substitute.For<ConsumeContext<ProbeEvent>>();
        context.MessageId.Returns(Guid.NewGuid());
        context.ReceiveContext.Returns(receive);
        var filter = new ConsumeFilter<ProbeEvent>(
            Substitute.For<IServiceProvider>(),
            hasConsumer ? [new ConsumerModuleRegistration("passenger-handler", typeof(ProbeEvent).Assembly)] : [],
            []
        );
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => filter.Send(context, Substitute.For<IPipe<ConsumeContext<ProbeEvent>>>())
        );
    }

    [Fact]
    public async Task Resolves_the_receiving_consumers_module_not_the_message_assembly()
    {
        var services = Substitute.For<IServiceProvider>();
        var owningProcessor = Substitute.For<IPersistMessageProcessor<string>>();
        var messageModuleProcessor = Substitute.For<IPersistMessageProcessor<ProbeEvent>>();
        services.GetService(typeof(IPersistMessageProcessor<string>)).Returns(owningProcessor);
        services.GetService(typeof(IPersistMessageProcessor<ProbeEvent>)).Returns(messageModuleProcessor);
        var receive = Substitute.For<ReceiveContext>();
        receive.InputAddress.Returns(new Uri("rabbitmq://broker/passenger-handler"));
        var context = Substitute.For<ConsumeContext<ProbeEvent>>();
        var id = Guid.NewGuid();
        context.MessageId.Returns(id);
        context.ReceiveContext.Returns(receive);
        var filter = new ConsumeFilter<ProbeEvent>(
            services,
            [new ConsumerModuleRegistration("passenger-handler", typeof(string).Assembly)],
            [
                new ModulePersistenceRegistration("Sender", typeof(ProbeEvent)),
                new ModulePersistenceRegistration("Receiver", typeof(string)),
            ]
        );
        await filter.Send(context, Substitute.For<IPipe<ConsumeContext<ProbeEvent>>>());
        await owningProcessor
            .Received(1)
            .ExecuteInboxAsync(id, "/passenger-handler", Arg.Any<Func<Task>>(), Arg.Any<CancellationToken>());
        await messageModuleProcessor.DidNotReceiveWithAnyArgs().ExecuteInboxAsync(default, default!, default!, default);
    }

    private static ConsumeFilter<ProbeEvent> CreateFilter(IPersistMessageProcessor<ProbeEvent> processor)
    {
        var services = Substitute.For<IServiceProvider>();
        services.GetService(typeof(IPersistMessageProcessor<ProbeEvent>)).Returns(processor);
        return new ConsumeFilter<ProbeEvent>(
            services,
            [new ConsumerModuleRegistration("passenger-register-new-user-handler", typeof(ProbeEvent).Assembly)],
            [new ModulePersistenceRegistration("Probe", typeof(ProbeEvent))]
        );
    }

    [Fact]
    public async Task Rejects_a_delivery_without_a_message_id_before_touching_the_inbox()
    {
        var processor = Substitute.For<IPersistMessageProcessor<ProbeEvent>>();
        var next = Substitute.For<IPipe<ConsumeContext<ProbeEvent>>>();
        var context = Substitute.For<ConsumeContext<ProbeEvent>>();
        context.MessageId.Returns((Guid?)null);

        await Assert.ThrowsAsync<InvalidOperationException>(() => CreateFilter(processor).Send(context, next));

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
        var processor = Substitute.For<IPersistMessageProcessor<ProbeEvent>>();
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

        await CreateFilter(processor).Send(context, next);

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
