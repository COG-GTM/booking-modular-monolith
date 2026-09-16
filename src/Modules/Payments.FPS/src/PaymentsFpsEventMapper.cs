using BuildingBlocks.Contracts.EventBus.Messages;
using BuildingBlocks.Core;
using BuildingBlocks.Core.Event;
using Payments.FPS.Consumers.ReceivingSchemeSettlement.V1;
using Payments.FPS.OutboundPayments.Features;
using Payments.FPS.OutboundPayments.Features.SubmittingPayment.V1;

namespace Payments.FPS;

public sealed class PaymentsFpsEventMapper : IEventMapper
{
    public IIntegrationEvent? MapToIntegrationEvent(IDomainEvent @event) =>
        @event switch
        {
            PaymentSubmittedDomainEvent e => new PaymentSubmitted(
                e.Id,
                e.Amount,
                e.Currency,
                e.DebtorSortCode,
                e.DebtorAccountNumber,
                e.CreditorSortCode,
                e.CreditorAccountNumber,
                e.Reference
            ),
            PaymentSettledDomainEvent e => new PaymentSettled(e.Id),
            PaymentRejectedDomainEvent e => new PaymentRejected(e.Id, e.RejectionReason),
            _ => null,
        };

    public IInternalCommand? MapToInternalCommand(IDomainEvent @event) =>
        @event switch
        {
            PaymentSubmittedDomainEvent e => new CreateOutboundPaymentMongo(
                e.Id,
                e.Amount,
                e.Currency,
                e.DebtorSortCode,
                e.DebtorAccountNumber,
                e.CreditorSortCode,
                e.CreditorAccountNumber,
                e.Reference,
                e.Status,
                e.IsDeleted
            ),
            PaymentSettledDomainEvent e => new UpdateOutboundPaymentMongo(e.Id, e.Status, null),
            PaymentRejectedDomainEvent e => new UpdateOutboundPaymentMongo(e.Id, e.Status, e.RejectionReason),
            _ => null,
        };
}
