using Payments.FPS.OutboundPayments.Exceptions;

namespace Payments.FPS.OutboundPayments.ValueObjects;

public record OutboundPaymentId
{
    public Guid Value { get; }

    private OutboundPaymentId(Guid value) => Value = value;

    public static OutboundPaymentId Of(Guid value) =>
        value == Guid.Empty ? throw new InvalidOutboundPaymentIdException(value) : new(value);

    public static implicit operator Guid(OutboundPaymentId id) => id.Value;
}
