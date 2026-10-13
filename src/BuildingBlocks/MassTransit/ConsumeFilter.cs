using BuildingBlocks.Core.Event;
using BuildingBlocks.PersistMessageProcessor;
using MassTransit;
using Microsoft.Extensions.Logging;

namespace BuildingBlocks.MassTransit;

// Inbox: records each received message and skips redeliveries that were already processed by this endpoint.
public class ConsumeFilter<T> : IFilter<ConsumeContext<T>>
    where T : class
{
    private readonly IPersistMessageProcessor _persistMessageProcessor;
    private readonly ILogger<ConsumeFilter<T>> _logger;

    public ConsumeFilter(IPersistMessageProcessor persistMessageProcessor, ILogger<ConsumeFilter<T>> logger)
    {
        _persistMessageProcessor = persistMessageProcessor;
        _logger = logger;
    }

    public async Task Send(ConsumeContext<T> context, IPipe<ConsumeContext<T>> next)
    {
        if (context.MessageId is not { } messageId)
        {
            await next.Send(context);
            return;
        }

        var inboxId = InboxMessageId.Create(messageId, context.ReceiveContext.InputAddress);

        var existing = (
            await _persistMessageProcessor.GetByFilterAsync(
                x => x.Id == inboxId && x.DeliveryType == MessageDeliveryType.Inbox,
                context.CancellationToken
            )
        ).FirstOrDefault();

        if (existing?.MessageStatus == MessageStatus.Processed)
        {
            _logger.LogInformation(
                "Skipping duplicate message {MessageId} of type {MessageType} on {InputAddress}.",
                messageId,
                typeof(T).Name,
                context.ReceiveContext.InputAddress
            );
            return;
        }

        if (existing is null)
        {
            await _persistMessageProcessor.AddReceivedMessageAsync(
                inboxId,
                new MessageEnvelope(context.Message, context.Headers.ToDictionary(x => x.Key, x => x.Value)),
                context.CancellationToken
            );
        }

        await next.Send(context);

        await _persistMessageProcessor.ProcessInboxAsync(inboxId, context.CancellationToken);
    }

    public void Probe(ProbeContext context)
    {
        context.CreateFilterScope("inbox");
    }
}
