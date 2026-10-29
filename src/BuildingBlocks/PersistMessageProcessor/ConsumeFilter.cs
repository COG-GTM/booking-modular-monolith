using System.Security.Cryptography;
using System.Text;
using BuildingBlocks.Core.Event;
using BuildingBlocks.PersistMessageProcessor;
using MassTransit;
using Microsoft.Extensions.Logging;

namespace BuildingBlocks.MassTransit;

// Inbox: records every received message per consumer endpoint and skips redeliveries of messages that
// were already processed. Keyed on the broker MessageId (stable across redeliveries and outbox re-publishes).
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
        var inboxId = InboxMessageId.For(context);

        var existing = await _persistMessageProcessor.GetInboxMessageAsync(inboxId, context.CancellationToken);

        if (existing?.MessageStatus == MessageStatus.Processed)
        {
            _logger.LogInformation(
                "Skipping duplicate message {MessageId} on {InputAddress}; inbox record {InboxId} is already processed.",
                context.MessageId,
                context.ReceiveContext.InputAddress,
                inboxId
            );
            return;
        }

        if (existing is null)
        {
            var recorded = await _persistMessageProcessor.TryAddReceivedMessageAsync(
                new MessageEnvelope(context.Message, context.Headers.ToDictionary(x => x.Key, x => x.Value)),
                inboxId,
                context.CancellationToken
            );

            if (!recorded)
            {
                // A concurrent delivery of the same MessageId owns the inbox row; if it fails, the broker
                // redelivers that copy, so acknowledging this one does not lose the message.
                _logger.LogInformation(
                    "Skipping concurrent duplicate {MessageId} on {InputAddress}; inbox record {InboxId} is owned by another delivery.",
                    context.MessageId,
                    context.ReceiveContext.InputAddress,
                    inboxId
                );
                return;
            }
        }

        await next.Send(context);
        await _persistMessageProcessor.ProcessInboxAsync(inboxId, context.CancellationToken);
    }

    public void Probe(ProbeContext context)
    {
        context.CreateFilterScope("persistMessageInbox");
    }
}

public static class InboxMessageId
{
    // Deterministic per (receiving endpoint, MessageId), so the same message delivered to two consumer
    // queues gets two inbox rows and never collides with the sender's outbox row in a shared store.
    public static Guid For(ConsumeContext context)
    {
        var messageId = context.MessageId ?? throw new InvalidOperationException("Inbox requires a MessageId.");
        var endpoint = context.ReceiveContext?.InputAddress?.AbsolutePath.Trim('/') ?? string.Empty;

        return For(endpoint, messageId);
    }

    public static Guid For(string endpoint, Guid messageId)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes($"{endpoint}|{messageId:N}"));
        var guid = bytes.AsSpan(0, 16).ToArray();

        // RFC 4122 variant + version 8 (custom, name-based) bits.
        guid[6] = (byte)((guid[6] & 0x0F) | 0x80);
        guid[8] = (byte)((guid[8] & 0x3F) | 0x80);

        return new Guid(guid, bigEndian: true);
    }
}
