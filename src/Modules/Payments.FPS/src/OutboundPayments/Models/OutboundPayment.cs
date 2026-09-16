using BuildingBlocks.Core.Event;
using BuildingBlocks.Core.Model;
using Payments.FPS.Consumers.ReceivingSchemeSettlement.V1;
using Payments.FPS.OutboundPayments.Enums;
using Payments.FPS.OutboundPayments.Exceptions;
using Payments.FPS.OutboundPayments.Features.SubmittingPayment.V1;
using Payments.FPS.OutboundPayments.ValueObjects;

namespace Payments.FPS.OutboundPayments.Models;

public record OutboundPayment : Aggregate<OutboundPaymentId>
{
    public Amount Amount { get; private set; } = default!;
    public UkAccount DebtorAccount { get; private set; } = default!;
    public UkAccount CreditorAccount { get; private set; } = default!;
    public string Reference { get; private set; } = default!;
    public PaymentStatus Status { get; private set; }
    public string? RejectionReason { get; private set; }

    public static OutboundPayment Create(
        OutboundPaymentId id,
        Amount amount,
        UkAccount debtor,
        UkAccount creditor,
        string reference,
        bool isDeleted = false
    )
    {
        var payment = new OutboundPayment
        {
            Id = id,
            Amount = amount,
            DebtorAccount = debtor,
            CreditorAccount = creditor,
            Reference = reference,
            Status = PaymentStatus.Initiated,
            IsDeleted = isDeleted,
        };
        payment.AddDomainEvent(
            new PaymentInitiatedDomainEvent(
                id,
                amount.Value,
                amount.Currency,
                debtor.SortCode,
                debtor.AccountNumber,
                creditor.SortCode,
                creditor.AccountNumber,
                reference,
                payment.Status,
                isDeleted
            )
        );
        return payment;
    }

    public void Submit()
    {
        EnsureStatus(PaymentStatus.Initiated, PaymentStatus.Submitted);
        Status = PaymentStatus.Submitted;
        AddDomainEvent(
            new PaymentSubmittedDomainEvent(
                Id,
                Amount.Value,
                Amount.Currency,
                DebtorAccount.SortCode,
                DebtorAccount.AccountNumber,
                CreditorAccount.SortCode,
                CreditorAccount.AccountNumber,
                Reference,
                Status,
                IsDeleted
            )
        );
    }

    public void Settle()
    {
        EnsureStatus(PaymentStatus.Submitted, PaymentStatus.Settled);
        Status = PaymentStatus.Settled;
        AddDomainEvent(new PaymentSettledDomainEvent(Id, Status));
    }

    public void Reject(string reason)
    {
        EnsureStatus(PaymentStatus.Submitted, PaymentStatus.Rejected);
        Status = PaymentStatus.Rejected;
        RejectionReason = reason;
        AddDomainEvent(new PaymentRejectedDomainEvent(Id, Status, reason));
    }

    private void EnsureStatus(PaymentStatus expected, PaymentStatus target)
    {
        if (Status != expected)
            throw new InvalidPaymentStatusTransitionException(Status, target);
    }
}
