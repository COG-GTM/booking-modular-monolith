using BuildingBlocks.Core.Event;
using BuildingBlocks.PersistMessageProcessor;
using MassTransit;

namespace BuildingBlocks.MassTransit;

// Inbox: makes consumption idempotent by keying the persisted message on the transport MessageId,
// which is stable across broker redeliveries (IEvent.EventId is regenerated on every access).
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
        var messageId = context.MessageId ?? NewId.NextGuid();

        var existing = (
            await _persistMessageProcessor.GetByFilterAsync(x =>
                x.Id == messageId && x.DeliveryType == MessageDeliveryType.Inbox
            )
        ).FirstOrDefault();

        if (existing?.MessageStatus == MessageStatus.Processed)
        {
            return; // duplicate delivery: already handled, acknowledge without running the consumer again
        }

        if (existing is null)
        {
            await _persistMessageProcessor.AddReceivedMessageAsync(
                new MessageEnvelope(context.Message, context.Headers.ToDictionary(x => x.Key, x => x.Value)),
                messageId
            );
        }

        await next.Send(context);
        await _persistMessageProcessor.ProcessInboxAsync(messageId);
    }

    public void Probe(ProbeContext context) { }
}
