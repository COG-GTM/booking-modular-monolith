using System.Diagnostics;
using System.Linq.Expressions;
using System.Text.Json;
using Ardalis.GuardClauses;
using BuildingBlocks.Core.Event;
using BuildingBlocks.Utils;
using MassTransit;
using MediatR;
using Microsoft.Extensions.Logging;

namespace BuildingBlocks.PersistMessageProcessor;

using Microsoft.EntityFrameworkCore;

public class PersistMessageProcessor<TModule> : IPersistMessageProcessor<TModule>
{
    private readonly ILogger<PersistMessageProcessor<TModule>> _logger;
    private readonly IMediator _mediator;
    private readonly IPersistMessageDbContext<TModule> _persistMessageDbContext;
    private readonly IPublishEndpoint _publishEndpoint;

    public PersistMessageProcessor(
        ILogger<PersistMessageProcessor<TModule>> logger,
        IMediator mediator,
        IPersistMessageDbContext<TModule> persistMessageDbContext,
        IPublishEndpoint publishEndpoint
    )
    {
        _logger = logger;
        _mediator = mediator;
        _persistMessageDbContext = persistMessageDbContext;
        _publishEndpoint = publishEndpoint;
    }

    public async Task PublishMessageAsync<TMessageEnvelope>(
        TMessageEnvelope messageEnvelope,
        CancellationToken cancellationToken = default
    )
        where TMessageEnvelope : MessageEnvelope
    {
        await SavePersistMessageAsync(messageEnvelope, MessageDeliveryType.Outbox, cancellationToken);
    }

    public Task<Guid> AddReceivedMessageAsync<TMessageEnvelope>(
        TMessageEnvelope messageEnvelope,
        CancellationToken cancellationToken = default
    )
        where TMessageEnvelope : MessageEnvelope
    {
        return SavePersistMessageAsync(messageEnvelope, MessageDeliveryType.Inbox, cancellationToken);
    }

    public async Task<bool> TryAddReceivedMessageAsync<TMessageEnvelope>(
        TMessageEnvelope messageEnvelope,
        Guid inboxId,
        CancellationToken cancellationToken = default
    )
        where TMessageEnvelope : MessageEnvelope
    {
        try
        {
            await SavePersistMessageAsync(messageEnvelope, MessageDeliveryType.Inbox, cancellationToken, inboxId);
            return true;
        }
        catch (DbUpdateException)
        {
            var rejected = _persistMessageDbContext.PersistMessage.Local.FirstOrDefault(x => x.Id == inboxId);
            if (rejected is not null)
                _persistMessageDbContext.PersistMessage.Entry(rejected).State = EntityState.Detached;

            var claimed = await _persistMessageDbContext
                .PersistMessage.AsNoTracking()
                .AnyAsync(x => x.Id == inboxId, cancellationToken);

            if (claimed)
                return false;

            throw;
        }
    }

    public Task<PersistMessage?> GetInboxMessageAsync(Guid inboxId, CancellationToken cancellationToken = default)
    {
        return _persistMessageDbContext.PersistMessage.FirstOrDefaultAsync(
            x => x.Id == inboxId && x.DeliveryType == MessageDeliveryType.Inbox,
            cancellationToken
        )!;
    }

    public async Task AddInternalMessageAsync<TCommand>(
        TCommand internalCommand,
        CancellationToken cancellationToken = default
    )
        where TCommand : class, IInternalCommand
    {
        await SavePersistMessageAsync(
            new MessageEnvelope(internalCommand),
            MessageDeliveryType.Internal,
            cancellationToken
        );
    }

    public async Task<IReadOnlyList<PersistMessage>> GetByFilterAsync(
        Expression<Func<PersistMessage, bool>> predicate,
        CancellationToken cancellationToken = default
    )
    {
        return (
            await _persistMessageDbContext.PersistMessage.Where(predicate).ToListAsync(cancellationToken)
        ).AsReadOnly();
    }

    public Task<PersistMessage> ExistMessageAsync(Guid messageId, CancellationToken cancellationToken = default)
    {
        return _persistMessageDbContext.PersistMessage.FirstOrDefaultAsync(
            x =>
                x.Id == messageId
                && x.DeliveryType == MessageDeliveryType.Inbox
                && x.MessageStatus == MessageStatus.Processed,
            cancellationToken
        );
    }

    public async Task ProcessAsync(
        Guid messageId,
        MessageDeliveryType deliveryType,
        CancellationToken cancellationToken = default
    )
    {
        var message = await _persistMessageDbContext.PersistMessage.FirstOrDefaultAsync(
            x => x.Id == messageId && x.DeliveryType == deliveryType,
            cancellationToken
        );

        if (message is null)
            return;

        switch (deliveryType)
        {
            case MessageDeliveryType.Internal:
                var sentInternalMessage = await ProcessInternalAsync(message, cancellationToken);
                if (sentInternalMessage)
                {
                    await ChangeMessageStatusAsync(message, cancellationToken);
                    break;
                }
                else
                {
                    return;
                }

            case MessageDeliveryType.Outbox:
                var sentOutbox = await ProcessOutboxAsync(message, cancellationToken);
                if (sentOutbox)
                {
                    await ChangeMessageStatusAsync(message, cancellationToken);
                    break;
                }
                else
                {
                    return;
                }
        }
    }

    public async Task ProcessAllAsync(CancellationToken cancellationToken = default)
    {
        var messages = await _persistMessageDbContext
            .PersistMessage.Where(x =>
                x.MessageStatus != MessageStatus.Processed && x.DeliveryType != MessageDeliveryType.Inbox
            )
            .ToListAsync(cancellationToken);

        foreach (var message in messages)
        {
            await ProcessAsync(message.Id, message.DeliveryType, cancellationToken);
        }
    }

    public async Task ProcessInboxAsync(Guid messageId, CancellationToken cancellationToken = default)
    {
        var message = await _persistMessageDbContext.PersistMessage.FirstOrDefaultAsync(
            x =>
                x.Id == messageId
                && x.DeliveryType == MessageDeliveryType.Inbox
                && x.MessageStatus == MessageStatus.InProgress,
            cancellationToken
        );

        await ChangeMessageStatusAsync(message, cancellationToken);
    }

    private async Task<bool> ProcessOutboxAsync(PersistMessage message, CancellationToken cancellationToken)
    {
        var messageEnvelope = JsonSerializer.Deserialize<MessageEnvelope>(message.Data);

        if (messageEnvelope is null || messageEnvelope.Message is null)
            return false;

        var data = JsonSerializer.Deserialize(
            messageEnvelope.Message.ToString() ?? string.Empty,
            TypeProvider.GetFirstMatchingTypeFromCurrentDomainAssembly(message.DataType) ?? typeof(object)
        );

        if (data is not IEvent)
            return false;

        using var activity = PersistMessageTracing.StartProcessActivity(
            $"outbox publish {data.GetType().Name}",
            ActivityKind.Producer,
            message,
            messageEnvelope.Headers
        );

        await _publishEndpoint.Publish(
            data,
            context =>
            {
                // Stable MessageId: a re-publish after a crash is deduplicated by the consumer's inbox.
                context.MessageId = message.Id;
                // MassTransit writes its own traceparent for the publish span (a child of the activity above).
                foreach (var header in messageEnvelope.Headers.Where(h => !PersistMessageTracing.IsTraceHeader(h.Key)))
                    context.Headers.Set(header.Key, header.Value);
            },
            cancellationToken
        );

        _logger.LogInformation(
            "Message with id: {MessageId} and delivery type: {DeliveryType} processed from the persistence message store.",
            message.Id,
            message.DeliveryType
        );

        return true;
    }

    private async Task<bool> ProcessInternalAsync(PersistMessage message, CancellationToken cancellationToken)
    {
        var messageEnvelope = JsonSerializer.Deserialize<MessageEnvelope>(message.Data);

        if (messageEnvelope is null || messageEnvelope.Message is null)
            return false;

        var data = JsonSerializer.Deserialize(
            messageEnvelope.Message.ToString() ?? string.Empty,
            TypeProvider.GetFirstMatchingTypeFromCurrentDomainAssembly(message.DataType) ?? typeof(object)
        );

        if (data is not IInternalCommand internalCommand)
            return false;

        using var activity = PersistMessageTracing.StartProcessActivity(
            $"internal command {data.GetType().Name}",
            ActivityKind.Internal,
            message,
            messageEnvelope.Headers
        );

        await _mediator.Send(internalCommand, cancellationToken);

        _logger.LogInformation(
            "InternalCommand with id: {EventID} and delivery type: {DeliveryType} processed from the persistence message store.",
            message.Id,
            message.DeliveryType
        );

        return true;
    }

    private async Task<Guid> SavePersistMessageAsync(
        MessageEnvelope messageEnvelope,
        MessageDeliveryType deliveryType,
        CancellationToken cancellationToken = default,
        Guid? explicitId = null
    )
    {
        Guard.Against.Null(messageEnvelope.Message, nameof(messageEnvelope.Message));

        Guid id;
        if (explicitId is { } given)
            id = given;
        else if (
            deliveryType == MessageDeliveryType.Inbox
            && messageEnvelope.Headers.TryGetValue("MessageId", out var messageId)
            && Guid.TryParse(messageId?.ToString(), out var receivedId)
        )
            id = receivedId;
        else if (messageEnvelope.Message is IEvent message)
            id = message.EventId;
        else
            id = NewId.NextGuid();

        if (
            deliveryType == MessageDeliveryType.Inbox
            && explicitId is null
            && await _persistMessageDbContext.PersistMessage.AnyAsync(x => x.Id == id, cancellationToken)
        )
            return id;

        await _persistMessageDbContext.PersistMessage.AddAsync(
            new PersistMessage(
                id,
                messageEnvelope.Message.GetType().ToString(),
                JsonSerializer.Serialize(
                    deliveryType == MessageDeliveryType.Inbox
                        ? messageEnvelope
                        : PersistMessageTracing.WithCurrentTraceContext(messageEnvelope)
                ),
                deliveryType
            ),
            cancellationToken
        );

        await _persistMessageDbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Message with id: {MessageID} and delivery type: {DeliveryType} saved in persistence message store.",
            id,
            deliveryType.ToString()
        );

        return id;
    }

    private async Task ChangeMessageStatusAsync(PersistMessage? message, CancellationToken cancellationToken)
    {
        if (message is null)
            return;

        message.ChangeState(MessageStatus.Processed);

        _persistMessageDbContext.PersistMessage.Update(message);

        await _persistMessageDbContext.SaveChangesAsync(cancellationToken);
    }
}
