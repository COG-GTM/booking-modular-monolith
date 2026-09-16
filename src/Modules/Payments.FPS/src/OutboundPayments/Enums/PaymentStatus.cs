namespace Payments.FPS.OutboundPayments.Enums;

public enum PaymentStatus
{
    Unknown = 0,
    Initiated,
    Submitted,
    Settled,
    Rejected,
}
