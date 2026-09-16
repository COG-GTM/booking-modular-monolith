using BuildingBlocks.Core.Event;
using MassTransit;

namespace BuildingBlocks.Contracts.EventBus.Messages;

public record PaymentSubmitted(
    Guid Id,
    decimal Amount,
    string Currency,
    string DebtorSortCode,
    string DebtorAccountNumber,
    string CreditorSortCode,
    string CreditorAccountNumber,
    string Reference
) : IIntegrationEvent;

public record PaymentSettled(Guid Id) : IIntegrationEvent;

public record PaymentRejected(Guid Id, string RejectionReason) : IIntegrationEvent;

public record SchemeSettlementReceived(Guid PaymentId, bool IsSettled, string? RejectionReason) : IIntegrationEvent
{
    public Guid EventId { get; init; } = NewId.NextGuid();
}
