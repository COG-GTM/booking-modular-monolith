using Payments.FPS.OutboundPayments.Enums;

namespace Payments.FPS.OutboundPayments.Models;

public class OutboundPaymentReadModel
{
    public required Guid Id { get; init; }
    public required Guid OutboundPaymentId { get; init; }
    public required decimal Amount { get; init; }
    public required string Currency { get; init; }
    public required string DebtorSortCode { get; init; }
    public required string DebtorAccountNumber { get; init; }
    public required string CreditorSortCode { get; init; }
    public required string CreditorAccountNumber { get; init; }
    public required string Reference { get; init; }
    public required PaymentStatus Status { get; init; }
    public string? RejectionReason { get; init; }
    public required bool IsDeleted { get; init; }
}
