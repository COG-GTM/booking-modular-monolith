using BuildingBlocks.Core.Event;
using BuildingBlocks.PersistMessageProcessor;
using MassTransit;
using Microsoft.Extensions.DependencyInjection;

namespace BuildingBlocks.MassTransit;

// Handle inbox messages with masstransit pipeline; the inbox record is written to the store owned by
// the consumer's module (resolved through the consumer's assembly).
public class ConsumeFilter<TConsumer, TMessage> : IFilter<ConsumerConsumeContext<TConsumer, TMessage>>
    where TConsumer : class
    where TMessage : class
{
    private readonly IPersistMessageProcessor _persistMessageProcessor;

    public ConsumeFilter(IServiceProvider serviceProvider)
    {
        _persistMessageProcessor = serviceProvider.GetPersistMessageProcessor(typeof(TConsumer).Assembly);
    }

    public async Task Send(
        ConsumerConsumeContext<TConsumer, TMessage> context,
        IPipe<ConsumerConsumeContext<TConsumer, TMessage>> next
    )
    {
        var id = await _persistMessageProcessor.AddReceivedMessageAsync(
            new MessageEnvelope(context.Message, context.Headers.ToDictionary(x => x.Key, x => x.Value))
        );

        var message = await _persistMessageProcessor.ExistMessageAsync(id);

        if (message is null)
        {
            await next.Send(context);
            await _persistMessageProcessor.ProcessInboxAsync(id);
        }
    }

    public void Probe(ProbeContext context)
    {
        context.CreateFilterScope("persistMessageInbox");
    }
}
