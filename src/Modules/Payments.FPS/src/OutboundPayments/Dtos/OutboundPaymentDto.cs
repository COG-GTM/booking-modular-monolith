using Payments.FPS.OutboundPayments.Enums;

namespace Payments.FPS.OutboundPayments.Dtos;

public record OutboundPaymentDto(
    Guid Id,
    decimal Amount,
    string Currency,
    string DebtorSortCode,
    string DebtorAccountNumber,
    string CreditorSortCode,
    string CreditorAccountNumber,
    string Reference,
    PaymentStatus Status,
    string? RejectionReason
);
