using BuildingBlocks.PersistMessageProcessor;
using MassTransit;

namespace BuildingBlocks.MassTransit;

// Handle inbox messages with masstransit pipeline
public class ConsumeFilter<T> : IFilter<ConsumeContext<T>>
    where T : class
{
    private readonly IPersistMessageProcessor _persistMessageProcessor;

    public ConsumeFilter(IPersistMessageProcessor persistMessageProcessor)
    {
        _persistMessageProcessor = persistMessageProcessor;
    }

    public async Task Send(ConsumeContext<T> context, IPipe<ConsumeContext<T>> next)
    {
        var messageId =
            context.MessageId
            ?? throw new InvalidOperationException("An inbox message must have a MassTransit MessageId.");
        await _persistMessageProcessor.ExecuteInboxAsync(
            messageId,
            context.ReceiveContext.InputAddress.AbsolutePath,
            () => next.Send(context),
            context.CancellationToken
        );
    }

    public void Probe(ProbeContext context) { }
}
