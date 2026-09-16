namespace Payments.FPS.Consumers.ReceivingSchemeSettlement.V1;

using Ardalis.GuardClauses;
using BuildingBlocks.Contracts.EventBus.Messages;
using BuildingBlocks.Core;
using BuildingBlocks.Core.Event;
using BuildingBlocks.PersistMessageProcessor;
using BuildingBlocks.Web;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Payments.FPS.Data;
using Payments.FPS.OutboundPayments.Enums;
using Payments.FPS.OutboundPayments.Exceptions;
using Payments.FPS.OutboundPayments.ValueObjects;

public record PaymentSettledDomainEvent(Guid Id, PaymentStatus Status) : IDomainEvent;

public record PaymentRejectedDomainEvent(Guid Id, PaymentStatus Status, string RejectionReason) : IDomainEvent;

public class ReceiveSchemeSettlementHandler(
    PaymentsDbContext db,
    IEventDispatcher eventDispatcher,
    IPersistMessageProcessor persistMessageProcessor,
    ILogger<ReceiveSchemeSettlementHandler> logger,
    IOptions<AppOptions> options
) : IConsumer<SchemeSettlementReceived>
{
    public async Task Consume(ConsumeContext<SchemeSettlementReceived> context)
    {
        Guard.Against.Null(context.Message, nameof(SchemeSettlementReceived));
        var messageId = context.Message.EventId;
        var received = await persistMessageProcessor.GetByFilterAsync(x =>
            x.Id == messageId && x.DeliveryType == MessageDeliveryType.Inbox
        );
        if (received.Count > 0)
        {
            logger.LogInformation(
                "Scheme settlement {MessageId} already received in {Application}, skipping",
                messageId,
                options.Value.Name
            );
            return;
        }
        await persistMessageProcessor.AddReceivedMessageAsync(
            new MessageEnvelope(context.Message, context.Headers.ToDictionary(x => x.Key, x => x.Value))
        );
        var payment = await db.OutboundPayments.SingleOrDefaultAsync(x =>
            x.Id == OutboundPaymentId.Of(context.Message.PaymentId)
        );
        if (payment is null)
            throw new OutboundPaymentNotFoundException();
        if (context.Message.IsSettled)
            payment.Settle();
        else
            payment.Reject(context.Message.RejectionReason ?? "Rejected by scheme");
        var events = payment.DomainEvents.ToList();
        await db.SaveChangesAsync();
        await eventDispatcher.SendAsync(events, typeof(IInternalCommand));
        await persistMessageProcessor.ProcessInboxAsync(messageId);
    }
}
